Imports Avalonia
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
        if desktop IsNot Nothing Then
            desktop.MainWindow = New MainWindow()
        End If

        MyBase.OnFrameworkInitializationCompleted()
    End Sub

End Class
