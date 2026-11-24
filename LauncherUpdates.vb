Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading

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
    Private Shared ReadOnly VersionLine As New Regex("(?m)^[ `]*Latest version:[ \t]*(?<version>[0-9]+)\b",
                                                     RegexOptions.CultureInvariant Or RegexOptions.IgnoreCase)
    Private ReadOnly Client As HttpClient

    Friend Sub New(client As HttpClient)
        Me.Client = client
    End Sub

    Friend Shared Function CreateClient() As HttpClient
        Dim client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(30), .MaxResponseContentBufferSize = 1024 * 1024}
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HabboCustomLauncher")
        Return client
    End Function

    Friend Async Function CheckAsync(installedVersion As Integer, cancellation As CancellationToken) As Task(Of LauncherUpdate)
        Using response = Await Client.GetAsync(ReleasesApi, cancellation).ConfigureAwait(False)
            response.EnsureSuccessStatusCode()
            Using stream = Await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(False),
                  document = Await JsonDocument.ParseAsync(stream, cancellationToken:=cancellation).ConfigureAwait(False)
                Dim release = document.RootElement
                Dim version As Integer
                Dim match = VersionLine.Match(If(release.GetProperty("body").GetString(), ""))
                If Not match.Success OrElse Not Integer.TryParse(match.Groups("version").Value, NumberStyles.None,
                                                                  CultureInfo.InvariantCulture, version) OrElse version <= 0 Then
                    Throw New InvalidDataException("The release does not declare a valid launcher version.")
                End If
                Dim tag = release.GetProperty("tag_name").GetString()
                If release.GetProperty("draft").GetBoolean() OrElse release.GetProperty("prerelease").GetBoolean() OrElse
                    String.IsNullOrWhiteSpace(tag) Then Throw New InvalidDataException("Expected a stable launcher release.")
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
            End Using
        End Using
    End Function

    Friend Async Function DownloadAsync(update As LauncherUpdate, folder As String, cancellation As CancellationToken) As Task(Of String)
        cancellation.ThrowIfCancellationRequested()
        Directory.CreateDirectory(folder)
        Dim destination = Path.Combine(folder, $"HabboCustomLauncher-v{update.Version}.exe")
        If File.Exists(destination) Then Return Await VerifyExistingAsync(destination, update, cancellation).ConfigureAwait(False)

        Dim temporary = destination & "." & Guid.NewGuid().ToString("N") & ".partial"
        Try
            Using timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
                timeout.CancelAfter(TimeSpan.FromMinutes(12))
                Dim token = timeout.Token
                Using response = Await Client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(False)
                    response.EnsureSuccessStatusCode()
                    If response.Content.Headers.ContentLength.HasValue AndAlso response.Content.Headers.ContentLength.Value <> update.Size Then
                        Throw New InvalidDataException("The response size did not match the release asset.")
                    End If
                    Using input = Await response.Content.ReadAsStreamAsync(token).ConfigureAwait(False),
                          output As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync:=True),
                          hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                        Dim buffer(65535) As Byte
                        Dim written As Long = 0
                        Do
                            Dim count = Await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(False)
                            If count = 0 Then Exit Do
                            If count > update.Size - written Then Throw New InvalidDataException("The download is larger than the release asset.")
                            Await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(False)
                            hash.AppendData(buffer, 0, count)
                            written += count
                        Loop
                        If written <> update.Size OrElse Not CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), update.Digest) Then
                            Throw New InvalidDataException("The launcher download did not match its published size and checksum.")
                        End If
                        Await output.FlushAsync(token).ConfigureAwait(False)
                    End Using
                End Using
                token.ThrowIfCancellationRequested()
                Try
                    File.Move(temporary, destination)
                    Return destination
                Catch ex As IOException When File.Exists(destination)
                    ' Another download may have finished first.
                End Try
                Return Await VerifyExistingAsync(destination, update, token).ConfigureAwait(False)
            End Using
        Finally
            Try
                File.Delete(temporary)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Debug.WriteLine(ex)
            End Try
        End Try
    End Function

    Private Shared Async Function VerifyExistingAsync(destination As String, update As LauncherUpdate,
                                                      cancellation As CancellationToken) As Task(Of String)
        Using existing = File.OpenRead(destination), sha = SHA256.Create()
            If existing.Length = update.Size AndAlso CryptographicOperations.FixedTimeEquals(
                Await sha.ComputeHashAsync(existing, cancellation).ConfigureAwait(False), update.Digest) Then
                cancellation.ThrowIfCancellationRequested()
                Return destination
            End If
        End Using
        Throw New IOException("A different file already exists at the update destination.")
    End Function
End Class
