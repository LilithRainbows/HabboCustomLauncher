Imports System.Globalization
Imports System.IO
Imports System.IO.Pipes
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.ApplicationLifetimes
Imports Avalonia.Markup.Xaml

Partial Public Class App
    Inherits Application
    Private LockFileStream As FileStream = Nothing

    Public Overrides Sub Initialize()
        AvaloniaXamlLoader.Load(Me)
    End Sub

    Public Overrides Sub OnFrameworkInitializationCompleted()
        Dim desktop As IClassicDesktopStyleApplicationLifetime = Nothing
        desktop = TryCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
        If desktop IsNot Nothing Then
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown

            If IsAppAlreadyRunning() Then
                HandleAppAlreadyRunning()
            Else
                desktop.MainWindow = Nothing
                Dim LauncherMainWindow = New MainWindow() 'MainWindow will decide which window should be shown
            End If

            'MyBase.OnFrameworkInitializationCompleted()
        End If
    End Sub

    Public Function IsAppAlreadyRunning() As Boolean
        Try
            Dim DestinationFolder = Path.Combine(MainWindow.GetAppDataPath, "Habbo Launcher")
            Directory.CreateDirectory(DestinationFolder)
            LockFileStream = New FileStream(
                Path.Combine(DestinationFolder, "HabboAirPlus.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                0,
                False)
            Return False
        Catch ex As Exception
            Return True
        End Try
    End Function

    Public Async Sub HandleAppAlreadyRunning()
        Dim HabboProtocol = Environment.GetCommandLineArgs().FirstOrDefault(Function(x) x.StartsWith("habbo://"), "")
        Await SendLoginTicketToMainInstance(HabboProtocol)
        Process.GetCurrentProcess.Kill()
    End Sub

    Private Async Function SendLoginTicketToMainInstance(LoginTicket As String) As Task(Of Boolean)
        Try
            Using pipeClient As New NamedPipeClientStream(".", "HabboCustomLauncherBeta", PipeDirection.Out)
                Await pipeClient.ConnectAsync(1000)
                Using writer As New StreamWriter(pipeClient)
                    Await writer.WriteLineAsync(LoginTicket)
                    Await writer.FlushAsync()
                    Return True
                End Using
            End Using
        Catch ex As Exception
            'Console.WriteLine("Error while sending login ticket to main instance: " & ex.Message)
            Return False
        End Try
    End Function

End Class
