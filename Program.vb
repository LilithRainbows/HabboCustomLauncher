Imports System.IO
Imports System.IO.Pipes
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Avalonia
Imports Avalonia.Media


Module Program
    Private appMutex As Mutex

    <STAThread>
    Sub Main(args As String())
        Dim mutexName As String = "HabboCustomLauncherBeta"
        Dim isNewInstance As Boolean
        appMutex = New Mutex(True, mutexName, isNewInstance)
        If Not isNewInstance Then
            For Each Argument In Environment.GetCommandLineArgs()
                If Argument.StartsWith("habbo://") Then
                    Argument = Argument.Remove(0, Argument.IndexOf("?server=") + 8)
                    Argument = Argument.Replace("&token=", ".")
                    SendLoginTicketToMainInstance(Argument)
                    Exit For
                End If
            Next
            SendLoginTicketToMainInstance("main")
            Return
        End If

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
        appMutex.ReleaseMutex()
    End Sub

    Private Sub SendLoginTicketToMainInstance(LoginTicket As String)
        Try
            Using pipeClient As New NamedPipeClientStream(".", "HabboCustomLauncherBeta", PipeDirection.Out)
                pipeClient.Connect(1000)
                Using writer As New StreamWriter(pipeClient)
                    writer.WriteLine(LoginTicket)
                    writer.Flush()
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine("Error al enviar los argumentos: " & ex.Message)
        End Try
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
