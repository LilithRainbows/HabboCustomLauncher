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
        Try
            Dim mutexName As String = "HabboCustomLauncherBeta"
            Dim isNewInstance As Boolean
            appMutex = New Mutex(True, mutexName, isNewInstance)
            If Not isNewInstance Then
                For Each Argument In Environment.GetCommandLineArgs()
                    Dim LoginCode As String = ""
                    If TryConvertHabboProtocolToLoginCode(Argument, LoginCode) Then
                        SendLoginTicketToMainInstance(LoginCode)
                        Return
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
        Catch
            'App startup error
        End Try
        Try
            appMutex.ReleaseMutex()
        Catch
            'Error while releasing mutex
        End Try
        Environment.Exit(0)
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
            Console.WriteLine("Error while sending login ticket to main instance: " & ex.Message)
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

    Public Function TryConvertHabboProtocolToLoginCode(rawArgument As String, ByRef loginCode As String) As Boolean
        loginCode = ""
        If String.IsNullOrWhiteSpace(rawArgument) Then
            Return False
        End If

        Dim trimmedArgument = rawArgument.Trim().Trim("\"""c, "'"c)
        If trimmedArgument.StartsWith("habbo://", StringComparison.OrdinalIgnoreCase) = False Then
            Return False
        End If

        Dim query As String = ""
        Dim queryIndex As Integer = trimmedArgument.IndexOf("?"c)
        If queryIndex >= 0 AndAlso queryIndex < trimmedArgument.Length - 1 Then
            query = trimmedArgument.Substring(queryIndex + 1)
        End If

        If String.IsNullOrWhiteSpace(query) Then
            Return False
        End If

        Dim server As String = ""
        Dim token As String = ""
        For Each part In query.Split("&"c)
            Dim keyValue = part.Split("="c, 2)
            If keyValue.Length <> 2 Then
                Continue For
            End If
            Dim key = keyValue(0).Trim().ToLowerInvariant()
            Dim value = Uri.UnescapeDataString(keyValue(1).Trim())

            If key = "server" Then
                server = value
            ElseIf key = "token" Then
                token = value
            End If
        Next

        If String.IsNullOrWhiteSpace(server) Or String.IsNullOrWhiteSpace(token) Then
            Return False
        End If

        loginCode = server & "." & token
        Return True
    End Function

End Module
