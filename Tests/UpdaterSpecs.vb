Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading

Friend Module UpdaterSpecs
    Friend ReadOnly Payload As Byte() = Enumerable.Range(0, 140000).Select(Function(n) CByte(n Mod 256)).ToArray()
    Friend ReadOnly TimeLimit As TimeSpan = TimeSpan.FromSeconds(5)
    Private Const Asset As String = "HabboCustomLauncher.exe"
    Private Const AssetRoot As String = "https://github.com/LilithRainbows/HabboCustomLauncher/releases/download/"

    Friend Async Function RunAsync(root As String) As Task
        Dim token = CancellationToken.None
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(MetadataResponse("v37", "Legacy notes say Latest version: 12"))))
            Dim service As New LauncherUpdates(client)
            Assert((Await service.CheckAsync(36, token)).Version = 37, "numeric tag offers a newer update")
            Assert(Await service.CheckAsync(37, token) Is Nothing, "same version stays current")
            Assert(Await service.CheckAsync(38, token) Is Nothing, "no downgrade")
        End Using
        Using client As New HttpClient(New ReplyHandler(Function(request, cancel) Task.FromResult(MetadataResponse())))
            Assert((Await (New LauncherUpdates(client)).CheckAsync(36, token)).Version = 37, "legacy latest release notes remain supported")
        End Using
        Using client As New HttpClient(New ReplyHandler(Function(request, cancel) Task.FromResult(MetadataResponse("latest", "Latest version: 37.1"))))
            Await ExpectAsync(Of InvalidDataException)(Function() (New LauncherUpdates(client)).CheckAsync(36, token), "ambiguous version rejected")
        End Using
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(MetadataResponse(assetUrl:=AssetRoot.Replace("LilithRainbows", "OtherOwner")))))
            Await ExpectAsync(Of InvalidDataException)(Function() (New LauncherUpdates(client)).CheckAsync(36, token), "only the official asset is accepted")
        End Using

        Await CheckNetworkFailuresAsync()
        Await CheckDownloadsAsync(root)
        Await CheckDownloadTimeoutsAsync(root)
    End Function

    Private Async Function CheckNetworkFailuresAsync() As Task
        Dim calls As Integer
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel)
                calls += 1
                Dim response As New HttpResponseMessage(HttpStatusCode.Forbidden)
                response.Headers.RetryAfter = New RetryConditionHeaderValue(TimeSpan.FromMinutes(2))
                Return Task.FromResult(response)
            End Function))
            Dim service As New LauncherUpdates(client)
            For attempt = 1 To 2
                Await ExpectFailureAsync(Function() service.CheckAsync(36, CancellationToken.None), LauncherUpdateFailure.RateLimit)
            Next
            Assert(calls = 1, "GitHub rate limit prevents another request during the cooldown")
        End Using
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(New HttpResponseMessage(HttpStatusCode.Forbidden) With {
                .Content = New StringContent("{""message"":""You have exceeded a secondary rate limit.""}")
            })))
            Dim service As New LauncherUpdates(client)
            Await ExpectFailureAsync(Function() service.CheckAsync(36, CancellationToken.None), LauncherUpdateFailure.RateLimit)
        End Using
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromException(Of HttpResponseMessage)(New HttpRequestException("Offline"))))
            Await ExpectAsync(Of HttpRequestException)(Function() (New LauncherUpdates(client)).CheckAsync(36, CancellationToken.None), "offline error surfaced")
        End Using
        Using client As New HttpClient(New ReplyHandler(AddressOf StallAsync))
            Dim service As New LauncherUpdates(client, checkTimeout:=TimeSpan.FromMilliseconds(100))
            Await ExpectAsync(Of TimeoutException)(Function() service.CheckAsync(36, CancellationToken.None), "release check timeout")
            Using cancellation As New CancellationTokenSource(TimeSpan.FromMilliseconds(100))
                Dim patient As New LauncherUpdates(client)
                Await ExpectAsync(Of OperationCanceledException)(Function() patient.CheckAsync(36, cancellation.Token), "cancel while checking")
            End Using
        End Using
    End Function

    Private Async Function CheckDownloadsAsync(root As String) As Task
        Dim token = CancellationToken.None
        Dim version = NewUpdate()
        Dim folder = Path.Combine(root, "recovery")
        Directory.CreateDirectory(folder)
        Dim installed = Path.Combine(root, "HabboCustomLauncher.exe")
        Await File.WriteAllTextAsync(installed, "installed launcher")
        Dim destination = Path.Combine(folder, "HabboCustomLauncher-v37.exe")
        Dim corrupt As Byte() = Payload.ToArray()
        corrupt(0) = 1
        Await File.WriteAllBytesAsync(destination, corrupt)

        Using corruptClient As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(DownloadResponse(New ByteArrayContent(corrupt)))))
            Dim service As New LauncherUpdates(corruptClient)
            Await ExpectFailureAsync(Function() service.DownloadAsync(version, folder, token), LauncherUpdateFailure.Integrity)
            Assert(File.ReadAllBytes(destination).SequenceEqual(corrupt) AndAlso Directory.GetFiles(folder).Length = 1,
                   "bad transfer leaves the previous file and removes its partial")
        End Using
        Dim calls As Integer
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel)
                Interlocked.Increment(calls)
                Return Task.FromResult(DownloadResponse(New ByteArrayContent(Payload)))
            End Function))
            Dim service As New LauncherUpdates(client)
            Assert(Await service.DownloadAsync(version, folder, token) = destination, "valid download recovers a corrupt existing file")
            Assert(Await LauncherUpdates.IsVerifiedAsync(destination, version, token), "recovered file matches published SHA-256")
            Assert(Await service.DownloadAsync(version, folder, token) = destination AndAlso calls = 1, "verified file is reused")
            Await File.WriteAllBytesAsync(destination, corrupt)
            Assert(Not Await LauncherUpdates.IsVerifiedAsync(destination, version, token), "tampering after staging is detected on recheck")

            Dim previousCalls = calls
            Dim noSpace As New LauncherUpdates(client, availableSpace:=Function(path) 1L)
            Await ExpectFailureAsync(Function() noSpace.DownloadAsync(version, Path.Combine(root, "disk-full"), token), LauncherUpdateFailure.DiskFull)
            Assert(calls = previousCalls, "insufficient space stops transfer before requesting the asset")
        End Using
        Dim waiting As Integer
        Dim allArrived As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Using client As New HttpClient(New ReplyHandler(
            Async Function(request, cancellation)
                If Interlocked.Increment(waiting) = 8 Then allArrived.TrySetResult(True)
                Await allArrived.Task.WaitAsync(cancellation).ConfigureAwait(False)
                Return DownloadResponse(New ByteArrayContent(Payload))
            End Function))
            Dim service As New LauncherUpdates(client)
            Dim parallelFolder = Path.Combine(root, "parallel")
            Using deadline As New CancellationTokenSource(TimeLimit)
                Dim jobs = Enumerable.Range(1, 8).Select(Function(i) service.DownloadAsync(version, parallelFolder, deadline.Token)).ToArray()
                Dim paths = Await Task.WhenAll(jobs)
                Assert(paths.Distinct().Count() = 1 AndAlso Directory.GetFiles(parallelFolder).Length = 1 AndAlso
                       Await LauncherUpdates.IsVerifiedAsync(paths(0), version, token), "eight concurrent downloads leave one valid file")
            End Using
        End Using
        Assert(File.ReadAllText(installed) = "installed launcher", "update success and failure preserve the installed launcher")
    End Function

    Private Async Function CheckDownloadTimeoutsAsync(root As String) As Task
        Dim token = CancellationToken.None
        Dim update = NewUpdate()
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(DownloadResponse(New StreamContent(New InterruptedStream())))))
            Dim service As New LauncherUpdates(client)
            Dim folder = Path.Combine(root, "interrupted")
            Await ExpectFailureAsync(Function() service.DownloadAsync(update, folder, token), LauncherUpdateFailure.Transfer)
            Assert(Directory.GetFiles(folder).Length = 0, "connection failure after the first chunk leaves no staged file")
        End Using
        Using client As New HttpClient(New ReplyHandler(AddressOf StallAsync))
            Dim service As New LauncherUpdates(client, headerTimeout:=TimeSpan.FromMilliseconds(100))
            Await ExpectAsync(Of TimeoutException)(Function() service.DownloadAsync(update, Path.Combine(root, "headers"), token), "download header timeout")
        End Using
        Dim started As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(DownloadResponse(New StreamContent(New ControlledStream(started, Timeout.InfiniteTimeSpan))))))
            Dim service As New LauncherUpdates(client, downloadTimeout:=TimeSpan.FromMilliseconds(200))
            Dim folder = Path.Combine(root, "body-timeout")
            Await ExpectAsync(Of TimeoutException)(Function() service.DownloadAsync(update, folder, token), "download body timeout")
            Assert(started.Task.IsCompleted AndAlso Directory.GetFiles(folder).Length = 0, "body timeout cleans the partial")
        End Using

        started = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(DownloadResponse(New StreamContent(New ControlledStream(started, Timeout.InfiniteTimeSpan))))))
            Dim service As New LauncherUpdates(client)
            Dim folder = Path.Combine(root, "canceled")
            Using cancellation As New CancellationTokenSource()
                Dim task = service.DownloadAsync(update, folder, cancellation.Token)
                Await started.Task.WaitAsync(TimeLimit)
                cancellation.Cancel()
                Await ExpectAsync(Of OperationCanceledException)(Function() task, "cancel during body download")
                Assert(Directory.GetFiles(folder).Length = 0, "user cancellation removes the partial")
            End Using
        End Using

        started = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel) Task.FromResult(DownloadResponse(New StreamContent(New ControlledStream(started, TimeSpan.FromMilliseconds(300)))))))
            Dim service As New LauncherUpdates(client, headerTimeout:=TimeSpan.FromMilliseconds(100))
            Dim staged = Await service.DownloadAsync(update, Path.Combine(root, "slow"), token).WaitAsync(TimeLimit)
            Assert(Await LauncherUpdates.IsVerifiedAsync(staged, update, token), "a slow body can outlive its header deadline")
        End Using
    End Function

    Friend Function MetadataResponse(Optional tag As String = "latest", Optional notes As String = "`Latest version: 37`",
                                     Optional assetUrl As String = Nothing) As HttpResponseMessage
        Dim metadata = New With {
            .tag_name = tag, .draft = False, .prerelease = False, .body = notes,
            .assets = {New With {
                .name = Asset, .size = Payload.LongLength, .digest = "sha256:" & Convert.ToHexString(SHA256.HashData(Payload)),
                .browser_download_url = If(assetUrl, AssetRoot & tag & "/" & Asset)
            }}
        }
        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent(JsonSerializer.Serialize(metadata))}
    End Function

    Friend Function DownloadResponse(content As HttpContent) As HttpResponseMessage
        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = content}
    End Function

    Private Function NewUpdate() As LauncherUpdate
        Return New LauncherUpdate(37, New Uri(AssetRoot & "latest/" & Asset), Payload.LongLength, SHA256.HashData(Payload))
    End Function

    Friend Async Function StallAsync(request As HttpRequestMessage, cancellation As CancellationToken) As Task(Of HttpResponseMessage)
        Await Task.Delay(Timeout.InfiniteTimeSpan, cancellation)
        Throw New InvalidOperationException("The test request should have been canceled.")
    End Function

    Friend Sub Assert(condition As Boolean, label As String)
        If Not condition Then Throw New InvalidOperationException(label)
        Console.WriteLine("PASS: " & label)
    End Sub

    Private Async Function ExpectAsync(Of T As Exception)(action As Func(Of Task), label As String) As Task
        Try
            Await action().WaitAsync(TimeLimit)
        Catch ex As Exception When GetType(T).IsInstanceOfType(ex) AndAlso
            (GetType(T) IsNot GetType(TimeoutException) OrElse ex.InnerException IsNot Nothing)
            Assert(True, label)
            Return
        End Try
        Throw New InvalidOperationException("Expected " & GetType(T).Name & ": " & label)
    End Function

    Private Async Function ExpectFailureAsync(action As Func(Of Task), expected As LauncherUpdateFailure) As Task
        Try
            Await action().WaitAsync(TimeLimit)
        Catch ex As LauncherUpdateException When ex.Failure = expected
            Assert(True, expected.ToString() & " reported")
            Return
        End Try
        Throw New InvalidOperationException("Expected " & expected.ToString())
    End Function

    Friend NotInheritable Class ReplyHandler
        Inherits HttpMessageHandler

        Private ReadOnly Respond As Func(Of HttpRequestMessage, CancellationToken, Task(Of HttpResponseMessage))

        Friend Sub New(respond As Func(Of HttpRequestMessage, CancellationToken, Task(Of HttpResponseMessage)))
            Me.Respond = respond
        End Sub

        Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
            Return Respond(request, cancellationToken)
        End Function
    End Class

    Private NotInheritable Class InterruptedStream
        Inherits MemoryStream

        Friend Sub New()
            MyBase.New(Payload, writable:=False)
        End Sub

        Public Overrides Function ReadAsync(buffer As Memory(Of Byte), Optional cancellationToken As CancellationToken = Nothing) As ValueTask(Of Integer)
            If Position > 0 Then
                Return New ValueTask(Of Integer)(Task.FromException(Of Integer)(New IOException("The test connection closed.")))
            End If
            Return MyBase.ReadAsync(buffer, cancellationToken)
        End Function
    End Class

    Friend NotInheritable Class ControlledStream
        Inherits MemoryStream

        Private ReadOnly Started As TaskCompletionSource(Of Boolean)
        Private ReadOnly Delay As TimeSpan
        Private FirstRead As Boolean = True

        Friend Sub New(started As TaskCompletionSource(Of Boolean), delay As TimeSpan)
            MyBase.New(Payload, writable:=False)
            Me.Started = started
            Me.Delay = delay
        End Sub

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides Function ReadAsync(buffer As Memory(Of Byte), Optional cancellationToken As CancellationToken = Nothing) As ValueTask(Of Integer)
            Return New ValueTask(Of Integer)(ReadControlledAsync(buffer, cancellationToken))
        End Function

        Private Async Function ReadControlledAsync(buffer As Memory(Of Byte), token As CancellationToken) As Task(Of Integer)
            If FirstRead Then
                FirstRead = False
                Started.TrySetResult(True)
                Await Task.Delay(Delay, token).ConfigureAwait(False)
            End If
            Return Await MyBase.ReadAsync(buffer, token).ConfigureAwait(False)
        End Function
    End Class
End Module
