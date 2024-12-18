Imports System.Reflection
Imports System.Runtime.InteropServices
Imports Avalonia
Imports Avalonia.Media

Module Program

    <STAThread>
    Sub Main(args As String())
        Dim AvaloniaApp = BuildAvaloniaApp()
        Dim osVersion = Environment.OSVersion.Version
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) AndAlso osVersion.Major = 6 AndAlso osVersion.Minor = 1 Then 'Usando Windows 7 se define renderizado por software debido a que el usuario probablemente tenga una gpu demasiado antigua para soportar opengl de forma adecuada (gma3600 por ejemplo da problemas)
            Dim Win32Options As New Win32PlatformOptions With {
                .RenderingMode = {Win32RenderingMode.Software},
                .CompositionMode = {Win32CompositionMode.RedirectionSurface}
                }
            AvaloniaApp.With(Win32Options)
        End If
        AvaloniaApp.StartWithClassicDesktopLifetime(args)
    End Sub

    Public Function BuildAvaloniaApp() As AppBuilder
        Dim FontFamilyName = "avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/Segoe-UI-Variable-Static-Text.ttf#Segoe UI Variable"
        Dim FontOptions As New FontManagerOptions With {
            .DefaultFamilyName = FontFamilyName,
            .FontFallbacks = {New FontFallback With {
            .FontFamily = New FontFamily(FontFamilyName)
            }}}
        'Alternativa:
        'Dim FontOptions As New FontManagerOptions With {
        '    .DefaultFamilyName = Nothing
        '}
        Return AppBuilder.Configure(Of App) _
            .UsePlatformDetect() _
            .LogToTrace() _
            .With(FontOptions) _
            .WithSystemFontSource(New Uri(FontFamilyName, UriKind.Absolute))
    End Function

End Module
