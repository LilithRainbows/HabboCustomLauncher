Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading

Friend Enum LauncherUpdateFailure
    RateLimit
    DiskFull
    Integrity
    Transfer
End Enum

Friend NotInheritable Class LauncherUpdateException
    Inherits IOException

    Friend ReadOnly Property Failure As LauncherUpdateFailure

    Friend Sub New(failure As LauncherUpdateFailure, message As String, Optional inner As Exception = Nothing)
        MyBase.New(message, inner)
        Me.Failure = failure
    End Sub
End Class

Friend NotInheritable Class LauncherUpdate
    Friend ReadOnly Property Version As Integer
    Friend ReadOnly Property DownloadUri As Uri
    Friend ReadOnly Property Size As Long
    Friend ReadOnly Property Digest As Byte()

    Friend Sub New(version As Integer, downloadUri As Uri, size As Long, digest As Byte())
        Me.Version = version
        Me.DownloadUri = downloadUri
        Me.Size = size
        Me.Digest = digest
    End Sub
End Class

Friend NotInheritable Class LauncherUpdates
    Friend Const ReleasesPage As String = "https://github.com/LilithRainbows/HabboCustomLauncher/releases/latest"
    Private Const ReleasesApi As String = "https://api.github.com/repos/LilithRainbows/HabboCustomLauncher/releases/latest"
    Private Const AssetName As String = "HabboCustomLauncher.exe"
    Private Const MaximumAssetSize As Long = 256L * 1024 * 1024
    Private Shared ReadOnly VersionLine As New Regex("(?m)^[ `]*Latest version:[ \t]*(?<version>[0-9]+)(?=[\s`]|$)",
                                                     RegexOptions.CultureInvariant Or RegexOptions.IgnoreCase)
    Private Shared ReadOnly VersionTag As New Regex("\Av?(?<version>[0-9]+)\z", RegexOptions.CultureInvariant Or RegexOptions.IgnoreCase)
    Private Shared ReadOnly PublishGate As New SemaphoreSlim(1, 1)
    Private ReadOnly Client As HttpClient
    Private ReadOnly CheckTimeout As TimeSpan
    Private ReadOnly HeaderTimeout As TimeSpan
    Private ReadOnly DownloadTimeout As TimeSpan
    Private ReadOnly AvailableSpace As Func(Of String, Long?)
    Private RateLimitUntilUtcTicks As Long

    Friend Sub New(client As HttpClient, Optional checkTimeout As TimeSpan? = Nothing,
                   Optional headerTimeout As TimeSpan? = Nothing, Optional downloadTimeout As TimeSpan? = Nothing,
                   Optional availableSpace As Func(Of String, Long?) = Nothing)
        Me.Client = client
        Me.CheckTimeout = If(checkTimeout, TimeSpan.FromSeconds(15))
        Me.HeaderTimeout = If(headerTimeout, TimeSpan.FromSeconds(30))
        Me.DownloadTimeout = If(downloadTimeout, TimeSpan.FromMinutes(12))
        Me.AvailableSpace = If(availableSpace, AddressOf GetAvailableSpace)
    End Sub

    Friend Shared Function CreateClient() As HttpClient
        Dim client As New HttpClient With {.Timeout = Timeout.InfiniteTimeSpan, .MaxResponseContentBufferSize = 1024 * 1024}
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HabboCustomLauncher")
        Return client
    End Function

    Friend Async Function CheckAsync(installedVersion As Integer, cancellation As CancellationToken) As Task(Of LauncherUpdate)
        cancellation.ThrowIfCancellationRequested()
        If DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(RateLimitUntilUtcTicks) Then Throw RateLimitError()

        Using deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
            deadline.CancelAfter(CheckTimeout)
            Try
                Using response = Await Client.GetAsync(ReleasesApi, deadline.Token).ConfigureAwait(False)
                    Await CheckRateLimitAsync(response, deadline.Token).ConfigureAwait(False)
                    response.EnsureSuccessStatusCode()
                    Using stream = Await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(False),
                          document = Await JsonDocument.ParseAsync(stream, cancellationToken:=deadline.Token).ConfigureAwait(False)
                        Return ParseRelease(document.RootElement, installedVersion)
                    End Using
                End Using
            Catch ex As OperationCanceledException When Not cancellation.IsCancellationRequested AndAlso deadline.IsCancellationRequested
                Throw New TimeoutException("The release check timed out.", ex)
            End Try
        End Using
    End Function

    Private Shared Function ParseRelease(release As JsonElement, installedVersion As Integer) As LauncherUpdate
        Dim tag = release.GetProperty("tag_name").GetString()
        If String.IsNullOrWhiteSpace(tag) OrElse release.GetProperty("draft").GetBoolean() OrElse
            release.GetProperty("prerelease").GetBoolean() Then Throw New InvalidDataException("Expected a stable launcher release.")

        Dim match = VersionTag.Match(tag)
        Dim notes As JsonElement
        If Not match.Success AndAlso tag = "latest" AndAlso release.TryGetProperty("body", notes) AndAlso
            notes.ValueKind = JsonValueKind.String Then match = VersionLine.Match(notes.GetString())

        Dim version As Integer
        If Not match.Success OrElse Not Integer.TryParse(match.Groups("version").Value, NumberStyles.None,
                                                        CultureInfo.InvariantCulture, version) OrElse version <= 0 Then
            Throw New InvalidDataException("The release does not declare a valid launcher version.")
        End If
        If version <= installedVersion Then Return Nothing

        Dim officialUrl = $"https://github.com/LilithRainbows/HabboCustomLauncher/releases/download/{Uri.EscapeDataString(tag)}/{AssetName}"
        For Each asset In release.GetProperty("assets").EnumerateArray()
            If asset.GetProperty("name").GetString() <> AssetName Then Continue For
            Dim size = asset.GetProperty("size").GetInt64()
            Dim digest As JsonElement
            If size <= 0 OrElse size > MaximumAssetSize OrElse
                Not String.Equals(asset.GetProperty("browser_download_url").GetString(), officialUrl, StringComparison.Ordinal) OrElse
                Not asset.TryGetProperty("digest", digest) OrElse digest.ValueKind <> JsonValueKind.String Then
                Throw New InvalidDataException("The official launcher download or its checksum is missing.")
            End If
            Dim checksum = digest.GetString()
            If checksum Is Nothing OrElse Not Regex.IsMatch(checksum, "\Asha256:[0-9a-fA-F]{64}\z") Then
                Throw New InvalidDataException("The release does not contain a valid SHA-256 checksum.")
            End If
            Return New LauncherUpdate(version, New Uri(officialUrl), size, Convert.FromHexString(checksum.Substring(7)))
        Next
        Throw New InvalidDataException("This release does not have a Windows launcher download.")
    End Function

    Private Async Function CheckRateLimitAsync(response As HttpResponseMessage, cancellation As CancellationToken) As Task
        If response.StatusCode <> HttpStatusCode.TooManyRequests AndAlso response.StatusCode <> HttpStatusCode.Forbidden Then Return
        Dim retry = response.Headers.RetryAfter
        Dim remaining As IEnumerable(Of String) = Nothing
        Dim limitReached = response.Headers.TryGetValues("X-RateLimit-Remaining", remaining) AndAlso remaining.Contains("0")
        If response.StatusCode = HttpStatusCode.Forbidden AndAlso retry Is Nothing AndAlso Not limitReached Then
            Dim message = Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False)
            If Not message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) Then Return
        End If

        Dim now = DateTimeOffset.UtcNow
        Dim retryAt = now.AddMinutes(1)
        Dim reset As IEnumerable(Of String) = Nothing
        Dim seconds As Long
        If retry IsNot Nothing AndAlso retry.Delta.HasValue Then
            retryAt = now.Add(retry.Delta.Value)
        ElseIf retry IsNot Nothing AndAlso retry.Date.HasValue Then
            retryAt = retry.Date.Value
        ElseIf response.Headers.TryGetValues("X-RateLimit-Reset", reset) AndAlso
            Long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, seconds) AndAlso
            seconds <= 253402300799L Then
            retryAt = DateTimeOffset.FromUnixTimeSeconds(seconds)
        End If
        Interlocked.Exchange(RateLimitUntilUtcTicks, Math.Max(now.AddSeconds(1).UtcTicks, retryAt.UtcTicks))
        Throw RateLimitError()
    End Function

    Private Shared Function RateLimitError() As LauncherUpdateException
        Return New LauncherUpdateException(LauncherUpdateFailure.RateLimit, "GitHub temporarily limited release checks.")
    End Function

    Friend Async Function DownloadAsync(update As LauncherUpdate, folder As String, cancellation As CancellationToken,
                                        Optional progress As IProgress(Of Long) = Nothing) As Task(Of String)
        cancellation.ThrowIfCancellationRequested()
        Directory.CreateDirectory(folder)
        Dim destination = Path.Combine(folder, $"HabboCustomLauncher-v{update.Version}.exe")
        Dim alreadyValid = Await IsVerifiedAsync(destination, update, cancellation).ConfigureAwait(False)
        cancellation.ThrowIfCancellationRequested()
        If alreadyValid Then Return destination
        Dim free = AvailableSpace(folder)
        If free.HasValue AndAlso free.Value < update.Size Then
            Throw New LauncherUpdateException(LauncherUpdateFailure.DiskFull, "There is not enough space for the launcher update.")
        End If

        Dim temporary = destination & "." & Guid.NewGuid().ToString("N") & ".partial"
        Try
            Using response = Await ReceiveDownloadHeadersAsync(update.DownloadUri, cancellation).ConfigureAwait(False),
                  deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
                response.EnsureSuccessStatusCode()
                deadline.CancelAfter(DownloadTimeout)
                Dim token = deadline.Token
                Try
                    If response.Content.Headers.ContentLength.HasValue AndAlso response.Content.Headers.ContentLength.Value <> update.Size Then
                        Throw IntegrityError()
                    End If
                    Using input = Await response.Content.ReadAsStreamAsync(token).ConfigureAwait(False),
                          output As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync:=True),
                          hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                        Dim buffer(65535) As Byte
                        Dim written As Long = 0
                        Dim reporting = If(progress Is Nothing, Nothing, Stopwatch.StartNew())
                        Do
                            Dim count = Await ReadDownloadAsync(input, buffer, token).ConfigureAwait(False)
                            If count = 0 Then Exit Do
                            If count > update.Size - written Then Throw IntegrityError()
                            Await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(False)
                            hash.AppendData(buffer, 0, count)
                            written += count
                            If reporting IsNot Nothing AndAlso (reporting.ElapsedMilliseconds >= 250 OrElse written = update.Size) Then
                                progress.Report(written)
                                reporting.Restart()
                            End If
                        Loop
                        If written <> update.Size OrElse Not CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), update.Digest) Then
                            Throw IntegrityError()
                        End If
                        Await output.FlushAsync(token).ConfigureAwait(False)
                    End Using
                    Await PublishVerifiedAsync(temporary, destination, update, token).ConfigureAwait(False)
                    Return destination
                Catch ex As OperationCanceledException When Not cancellation.IsCancellationRequested AndAlso deadline.IsCancellationRequested
                    Throw New TimeoutException("The launcher download timed out.", ex)
                Catch ex As IOException When (ex.HResult And &HFFFF) = 39 OrElse (ex.HResult And &HFFFF) = 112
                    Throw New LauncherUpdateException(LauncherUpdateFailure.DiskFull, "The drive ran out of space.", ex)
                End Try
            End Using
        Finally
            Try
                File.Delete(temporary)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Debug.WriteLine(ex)
            End Try
        End Try
    End Function

    Private Shared Async Function PublishVerifiedAsync(temporary As String, destination As String, update As LauncherUpdate,
                                                       token As CancellationToken) As Task
        Await PublishGate.WaitAsync(token).ConfigureAwait(False)
        Try
            If Await IsVerifiedAsync(destination, update, token).ConfigureAwait(False) Then Return
            token.ThrowIfCancellationRequested()
            Dim failure As Exception
            Try
                File.Move(temporary, destination, overwrite:=True)
                Return
            Catch ex As Exception When (TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException) AndAlso
                File.Exists(destination)
                failure = ex
            End Try
            ' A different launcher process could have staged the same version.
            If Not Await IsVerifiedAsync(destination, update, token).ConfigureAwait(False) Then Throw failure
        Finally
            PublishGate.Release()
        End Try
    End Function

    Friend Shared Async Function IsVerifiedAsync(destination As String, update As LauncherUpdate,
                                                 cancellation As CancellationToken) As Task(Of Boolean)
        Try
            Using existing As New FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read Or FileShare.Delete, 65536, useAsync:=True),
                  sha = SHA256.Create()
                Return existing.Length = update.Size AndAlso CryptographicOperations.FixedTimeEquals(
                    Await sha.ComputeHashAsync(existing, cancellation).ConfigureAwait(False), update.Digest)
            End Using
        Catch ex As Exception When TypeOf ex Is FileNotFoundException OrElse TypeOf ex Is DirectoryNotFoundException
            Return False
        End Try
    End Function

    Private Async Function ReceiveDownloadHeadersAsync(uri As Uri, cancellation As CancellationToken) As Task(Of HttpResponseMessage)
        Using deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
            deadline.CancelAfter(HeaderTimeout)
            Try
                Return Await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(False)
            Catch ex As OperationCanceledException When Not cancellation.IsCancellationRequested AndAlso deadline.IsCancellationRequested
                Throw New TimeoutException("The download headers timed out.", ex)
            End Try
        End Using
    End Function

    Private Shared Async Function ReadDownloadAsync(input As Stream, buffer As Byte(), token As CancellationToken) As Task(Of Integer)
        Try
            Return Await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(False)
        Catch ex As IOException
            token.ThrowIfCancellationRequested()
            Throw New LauncherUpdateException(LauncherUpdateFailure.Transfer, "The response ended before the download completed.", ex)
        End Try
    End Function

    Private Shared Function IntegrityError() As LauncherUpdateException
        Return New LauncherUpdateException(LauncherUpdateFailure.Integrity, "The download did not match the published size and checksum.")
    End Function

    Private Shared Function GetAvailableSpace(folder As String) As Long?
        Try
            Return New DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))).AvailableFreeSpace
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' Some redirected profiles do not expose free space.
            Return Nothing
        End Try
    End Function
End Class
