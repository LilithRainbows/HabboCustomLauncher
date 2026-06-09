Imports System.Reflection
Imports System.Runtime.InteropServices
Imports Avalonia
Imports Avalonia.Media

Module Program
    <STAThread>
    Sub Main(args As String())
        Try
            StartAvaloniaApp(args)
        Catch
            'App startup error
        End Try
        Process.GetCurrentProcess.Kill()
    End Sub

    Private Sub StartAvaloniaApp(NewWindowArgs As String())
        Dim AvaloniaApp = BuildAvaloniaApp()
        Dim osVersion = Environment.OSVersion.Version
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then 'AndAlso osVersion.Major = 6 AndAlso osVersion.Minor = 1 'When using Windows 7, software rendering is used because the user likely has a very old GPU (for example, a GMA 3600).
            Dim Win32Options As New Win32PlatformOptions With {
                .RenderingMode = {Win32RenderingMode.Software},
                .CompositionMode = {Win32CompositionMode.RedirectionSurface}
                }
            AvaloniaApp.With(Win32Options)
        End If
        AvaloniaApp.StartWithClassicDesktopLifetime(NewWindowArgs)
    End Sub

    Public Function BuildAvaloniaApp() As AppBuilder
        Dim FontFamilyName = "avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/Segoe-UI-Variable-Static-Text.ttf#Segoe UI Variable"
        Dim FontOptions As New FontManagerOptions With {
            .DefaultFamilyName = FontFamilyName,
            .FontFallbacks = {New FontFallback With {
            .FontFamily = New FontFamily(FontFamilyName)
            }}}
        Return AppBuilder.Configure(Of App) _
            .UsePlatformDetect() _
            .LogToTrace() _
            .With(FontOptions) _
            .WithSystemFontSource(New Uri(FontFamilyName, UriKind.Absolute))
    End Function

End Module