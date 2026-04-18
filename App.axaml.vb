Imports System.Globalization
Imports System.Reflection
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.ApplicationLifetimes
Imports Avalonia.Markup.Xaml

Partial Public Class App
    Inherits Application

    Public Overrides Sub Initialize()
        AvaloniaXamlLoader.Load(Me)
    End Sub

    Public Overrides Sub OnFrameworkInitializationCompleted()
        Dim desktop As IClassicDesktopStyleApplicationLifetime = Nothing
        desktop = TryCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
        If desktop IsNot Nothing Then
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown

            Dim CustomWindowScale = Environment.GetCommandLineArgs().FirstOrDefault(Function(x) x.StartsWith("-scale="), 0)
            CustomWindowScale = CustomWindowScale.Replace("-scale=", "")
            If IsNumeric(CustomWindowScale) = False Then
                CustomWindowScale = 0
            End If
            Singleton.GetCurrentInstance().CustomWindowScale = Double.Parse(CustomWindowScale, CultureInfo.InvariantCulture)

            If desktop.Args.Contains("already_running") Then
                HandleAppAlreadyRunning()
            Else
                desktop.MainWindow = Nothing
                Dim LauncherMainWindow = New MainWindow() 'MainWindow will decide which window should be shown
            End If

            'MyBase.OnFrameworkInitializationCompleted()
        End If
    End Sub

    Public Async Sub HandleAppAlreadyRunning()
        Dim HabboProtocol = Environment.GetCommandLineArgs().FirstOrDefault(Function(x) x.StartsWith("habbo://"), "")
        Dim EmptyWindow = New Window()
        If HabboProtocol = "" Then
            HabboProtocol = Await EmptyWindow.Clipboard.GetTextAsync()
        End If
        Await EmptyWindow.Clipboard.SetTextAsync("hcl_main_focus_" & HabboProtocol)
        Process.GetCurrentProcess.Kill()
    End Sub

End Class
