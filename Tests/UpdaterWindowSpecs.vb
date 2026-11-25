Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Headless
Imports Avalonia.Interactivity
Imports Avalonia.Media
Imports Avalonia.Threading

Friend Module TestProgram
    <STAThread>
    Public Function Main() As Integer
        Dim root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hcl-updater-tests-" & Guid.NewGuid().ToString("N")))
        Try
            Dim clipboard = New LoginCode("hhes.fixture.V4.Tester", False)
            Dim protocol = New LoginCode("habbo://hab?server=hhes&token=fixture.V4.Tester", True)
            Assert(clipboard.ServerId = "hhes" AndAlso clipboard.ServerUrl = protocol.ServerUrl AndAlso
                   clipboard.SSOTicket = protocol.SSOTicket AndAlso protocol.Username = "Tester",
                   "existing AIR login code and habbo protocol still parse the same fixture")
            UpdaterSpecs.RunAsync(root.FullName).GetAwaiter().GetResult()
            AppBuilder.Configure(Of App)().UseHeadless(New AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting()
            Application.Current.Resources("NewSegoeUI") = FontFamily.Default
            Using stopLoop As New CancellationTokenSource(TimeSpan.FromSeconds(30))
                Dim result As Task = Nothing
                Dispatcher.UIThread.Post(
                    Sub()
                        result = RunWindowTestsAsync(root.FullName)
                        result.GetAwaiter().OnCompleted(Sub() stopLoop.Cancel())
                    End Sub)
                Dispatcher.UIThread.MainLoop(stopLoop.Token)
                If result Is Nothing OrElse Not result.IsCompleted Then Throw New TimeoutException("Window tests did not complete.")
                result.GetAwaiter().GetResult()
            End Using
            Return 0
        Catch ex As Exception
            Console.Error.WriteLine(ex.ToString())
            Return 1
        Finally
            root.Delete(recursive:=True)
        End Try
    End Function

    Private Async Function RunWindowTestsAsync(root As String) As Task
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US")
        Dim owner As New Window()
        owner.Show()

        Dim gameLoadingWindow As New LoadingWindow()
        Assert(Not gameLoadingWindow.FindControl(Of CustomButton)("UpdateActionButton").IsVisible AndAlso
               Not gameLoadingWindow.FindControl(Of ProgressBar)("LauncherDownloadProgress").IsVisible AndAlso
               gameLoadingWindow.FindControl(Of CustomButton)("MainMenuButton").Text = LauncherUpdaterTranslator.ReturnToMainMenu(0),
               "game loading window has its original controls")

        Dim started As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim canceled As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Using client As New HttpClient(New ReplyHandler(
            Async Function(request, cancellation)
                started.TrySetResult(True)
                Try
                    Return Await StallAsync(request, cancellation)
                Catch ex As OperationCanceledException When cancellation.IsCancellationRequested
                    canceled.TrySetResult(True)
                    Throw
                End Try
            End Function))
            Dim dialog As New LoadingWindow()
            dialog.ConfigureLauncherUpdates(36, New LauncherUpdates(client), root)
            Dim lifetime = dialog.ShowDialog(owner)
            Await started.Task.WaitAsync(TimeLimit)
            Click(dialog, "CloseButton")
            Await lifetime.WaitAsync(TimeLimit)
            Await canceled.Task.WaitAsync(TimeLimit)
            Assert(owner.IsVisible AndAlso Not dialog.IsVisible, "closing the updater cancels its query and keeps its owner open")
        End Using

        Dim attempts As Integer
        Dim bodyStarted As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Using client As New HttpClient(New ReplyHandler(
            Function(request, cancel)
                If request.RequestUri.Host = "api.github.com" Then
                    If Interlocked.Increment(attempts) = 1 Then
                        Return Task.FromException(Of HttpResponseMessage)(New HttpRequestException("Offline"))
                    End If
                    Return Task.FromResult(MetadataResponse())
                End If
                Return Task.FromResult(DownloadResponse(New StreamContent(New ControlledStream(bodyStarted, Timeout.InfiniteTimeSpan))))
            End Function))
            Dim dialog As New LoadingWindow()
            Dim folder = Path.Combine(root, "window")
            dialog.ConfigureLauncherUpdates(36, New LauncherUpdates(client), folder)
            Dim lifetime = dialog.ShowDialog(owner)
            Await WaitForStatusAsync(dialog, LauncherUpdateText.ConnectionError(0))
            Click(dialog, "UpdateActionButton")
            Await WaitForStatusAsync(dialog, String.Format(LauncherUpdateText.Available(0), 36, 37))
            Assert(attempts = 2, "failed release check can be retried in the same window")
            Click(dialog, "UpdateActionButton")
            Await bodyStarted.Task.WaitAsync(TimeLimit)
            Assert(dialog.FindControl(Of ProgressBar)("LauncherDownloadProgress").IsVisible, "progress bar is displayed during download")
            Click(dialog, "MainMenuButton")
            Await WaitForStatusAsync(dialog, LauncherUpdateText.DownloadCanceled(0))
            Assert(dialog.IsVisible AndAlso Directory.GetFiles(folder).Length = 0, "cancel button keeps the dialog open and removes the partial")
            Click(dialog, "MainMenuButton")
            Await lifetime.WaitAsync(TimeLimit)
            Assert(owner.IsVisible, "closing the dialog after cancellation leaves the launcher open")
        End Using

        owner.Close()
    End Function

    Private Sub Click(window As LoadingWindow, name As String)
        Dim control = window.FindControl(Of CustomButton)(name)
        Dim button = DirectCast(control.FindNameScope().Find("InnerButton"), Button)
        button.RaiseEvent(New RoutedEventArgs(Button.ClickEvent))
    End Sub

    Private Async Function WaitForStatusAsync(window As LoadingWindow, expected As String) As Task
        Dim elapsed = Stopwatch.StartNew()
        While window.StatusLabel.Text <> expected AndAlso elapsed.Elapsed < TimeLimit
            Await Task.Delay(20)
        End While
        If window.StatusLabel.Text <> expected Then
            Throw New InvalidOperationException($"Expected '{expected}', got '{window.StatusLabel.Text}'.")
        End If
    End Function
End Module
