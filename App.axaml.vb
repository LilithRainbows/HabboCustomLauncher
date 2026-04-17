Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.ApplicationLifetimes
Imports Avalonia.Markup.Xaml

Public Partial Class App
    Inherits Application

    Public Overrides Sub Initialize()
        AvaloniaXamlLoader.Load(Me)
    End Sub

    Public Overrides Sub OnFrameworkInitializationCompleted()
        Dim desktop As IClassicDesktopStyleApplicationLifetime = Nothing
        desktop = TryCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
        If desktop IsNot Nothing Then
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown
            If desktop.Args.Contains("already_running") Then
                Dim HabboProtocol = Environment.GetCommandLineArgs().FirstOrDefault(Function(x) x.StartsWith("habbo://"), "")
                Dim EmptyWindow = New Window()
                EmptyWindow.Clipboard.SetTextAsync("hcl_main_focus_" & HabboProtocol).Wait()
                Process.GetCurrentProcess.Kill()
                Return
            End If
            desktop.MainWindow = Nothing
            Dim LauncherMainWindow = New MainWindow() 'MainWindow will decide which window should be shown
        End If
        MyBase.OnFrameworkInitializationCompleted()
    End Sub

End Class
