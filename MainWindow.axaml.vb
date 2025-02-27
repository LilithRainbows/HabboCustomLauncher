Imports System.IO
Imports System.IO.Compression
Imports System.IO.Pipes
Imports System.Net.Http
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text.Json
Imports System.Threading
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media
Imports Avalonia.Media.Imaging
Imports Avalonia.Platform
Imports Microsoft.Win32
Imports WindowsShortcutFactory

'PROBLEMA: Como se puede hacer para que se pueda definir manualmente un login code en lugar de solo leerlo desde el clipboard?
'SOLUCION: Al hacer click al boton se pregunta para introducir manualmente el codigo (aunque seria innecesario), mejor preguntar que hotel lanzar directamente? o tambien es innecesario? si todo es innecesario capaz convenga hacer que deje de ser un boton y pase a ser un label
'Quizas habria que revisar en https://images.habbo.com/habbo-native-clients/launcher/clientversions.json para purgar versiones ya invalidas

Partial Public Class MainWindow : Inherits Window
    Private WithEvents Window As Window
    Private WithEvents StartNewInstanceButton As CustomButton
    Private WithEvents StartNewInstanceButton2 As CustomButton
    Private WithEvents LoginCodeButton As CustomButton
    Private WithEvents ChangeUpdateSourceButton As CustomButton
    Private WithEvents HabboLogoButton As Image
    Private WithEvents GithubButton As Image
    Private WithEvents SulakeButton As Image
    Private WithEvents FooterButton As CustomButton
    Public CurrentLoginCode As LoginCode
    Public CurrentClientUrls As JsonClientUrls
    Public CurrentDownloadProgress As Integer
    Public UpdateSource As String = "AIR_Official"
    Public CurrentLanguageInt As Integer = 0
    Private ReadOnly HttpClient As New HttpClient()
    Private NamedPipeCancellationTokenSource As CancellationTokenSource
    Public UnixPatchName As String = "HabboAirLinuxPatch_x64.zip"

    Private LauncherUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HabboLauncher/1.0.41 Chrome/87.0.4280.141 Electron/11.3.0 Safari/537.36"


    Public Async Sub StartPipedLoginTicketListener()
        Try
            If NamedPipeCancellationTokenSource Is Nothing Then
                NamedPipeCancellationTokenSource = New CancellationTokenSource()
                While Not NamedPipeCancellationTokenSource.Token.IsCancellationRequested
                    Using pipeServer As New NamedPipeServerStream("HabboCustomLauncherBeta", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
                        Await pipeServer.WaitForConnectionAsync(NamedPipeCancellationTokenSource.Token)
                        Using reader As New StreamReader(pipeServer)
                            Dim arguments As String = Await reader.ReadLineAsync()
                            If arguments IsNot Nothing Then

                                If Window.IsActive = False Then
                                    Window.WindowState = WindowState.Minimized
                                    Await Task.Delay(100)
                                    Window.WindowState = WindowState.Normal
                                    Window.Activate()
                                End If

                                'Window.Topmost = True
                                'Window.WindowState = WindowState.Normal
                                'Window.Activate()
                                'Window.Topmost = False

                                If arguments = "main" = False Then
                                    Await Clipboard.SetTextAsync(arguments)
                                    Await CheckClipboardLoginCodeAsync()
                                End If
                            End If
                        End Using
                    End Using
                End While
            End If
        Catch
            'Console.WriteLine("PipeServer error!")
        End Try
        StopPipedLoginTicketListener()
    End Sub

    Public Sub StopPipedLoginTicketListener()
        Try
            If NamedPipeCancellationTokenSource IsNot Nothing Then
                NamedPipeCancellationTokenSource.Cancel()
                NamedPipeCancellationTokenSource.Dispose()
                NamedPipeCancellationTokenSource = Nothing
            End If
        Catch
        End Try
    End Sub

    Sub New()
        ' This call is required by the designer
        InitializeComponent()
    End Sub

    ' Auto-wiring does not work for VB, so do it manually
    ' Wires up the controls and optionally loads XAML markup and attaches dev tools (if Avalonia.Diagnostics package is referenced)
    Private Sub InitializeComponent(Optional loadXaml As Boolean = True)
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("es") Then
            CurrentLanguageInt = 1
        End If
        If loadXaml Then
            AvaloniaXamlLoader.Load(Me)
        End If
        'Example: Control = FindNameScope().Find("Control_Name")
        Window = FindNameScope().Find("Window")
        StartNewInstanceButton = Window.FindNameScope.Find("StartNewInstanceButton")
        StartNewInstanceButton2 = Window.FindNameScope.Find("StartNewInstanceButton2")
        LoginCodeButton = Window.FindNameScope.Find("LoginCodeButton")
        ChangeUpdateSourceButton = Window.FindNameScope.Find("ChangeUpdateSourceButton")
        ChangeUpdateSourceButton = Window.FindNameScope.Find("ChangeUpdateSourceButton")
        HabboLogoButton = Window.FindNameScope.Find("HabboLogoButton")
        GithubButton = Window.FindNameScope.Find("GithubButton")
        SulakeButton = Window.FindNameScope.Find("SulakeButton")
        FooterButton = Window.FindNameScope.Find("FooterButton")

        LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeNotDetected(CurrentLanguageInt)
        StartNewInstanceButton.Text = AppTranslator.UnknownClientVersion(CurrentLanguageInt)

        StartPipedLoginTicketListener()
        DisplayLauncherVersionOnFooter()
        RefreshUpdateSourceText()
        StartRecursiveClipboardLoginCodeCheckAsync()
        FixWindowsTLS()
        RegisterHabboProtocol()

        If (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Or RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) AndAlso RuntimeInformation.ProcessArchitecture = Architecture.Arm64 Then
            UnixPatchName = "HabboAirLinuxPatch_arm64.zip"
        End If
        If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
            UnixPatchName = "HabboAirOSXPatch.zip"
        End If

        For Each Argument In Environment.GetCommandLineArgs()
            If Argument.StartsWith("habbo://") Then
                Argument = Argument.Remove(0, Argument.IndexOf("?server=") + 8)
                Argument = Argument.Replace("&token=", ".")
                CopyToClipboard(Argument) 'Clipboard.SetTextAsync(Argument).Wait()
                CheckClipboardLoginCodeAsync()
            End If
        Next
    End Sub

    Public Async Sub CopyToClipboard(Argument As String)
        Await Clipboard.SetTextAsync(Argument)
    End Sub

    Private Function DisplayLauncherVersionOnFooter() As String
        FooterButton.BackColor = Color.Parse("Transparent")
        FooterButton.Text = "CustomLauncher version 13 (26/02/2025)"
    End Function

    Private Function DisplayCurrentUserOnFooter() As String
        If String.IsNullOrWhiteSpace(CurrentLoginCode.Username) Then
            DisplayLauncherVersionOnFooter()
        Else
            FooterButton.BackColor = Color.Parse("#8D31A500")
            Dim FinalUsername = CurrentLoginCode.Username
            If FinalUsername.Length > 15 Then
                FinalUsername = FinalUsername.Remove(15) & "..."
            End If
            FooterButton.Text = AppTranslator.PlayingAs(CurrentLanguageInt) & " " & FinalUsername
        End If
    End Function

    Private Sub StartNewInstanceButton_Click(sender As Object, e As RoutedEventArgs) Handles StartNewInstanceButton.Click
        If StartNewInstanceButton.Text = AppTranslator.RetryClientUpdatesCheck(CurrentLanguageInt) Then
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            UpdateClientButtonStatus()
        End If
        If StartNewInstanceButton.Text.StartsWith(AppTranslator.UpdateClientVersion(CurrentLanguageInt)) Then
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            UpdateClient()
        End If
        If StartNewInstanceButton.Text.StartsWith(AppTranslator.LaunchClientVersion(CurrentLanguageInt)) Then
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            LaunchClient()
        End If
    End Sub

    Public Async Function LaunchClient() As Task
        Try
            Dim ClientProcess As New Process
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then 'Windows
                ClientProcess.StartInfo.FileName = Path.Combine(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), "Habbo.exe")
            End If
            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then 'OSX
                ClientProcess.StartInfo.FileName = Path.Combine(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), "Habbo.app/Contents/MacOS/Habbo")
            Else 'Linux
                ClientProcess.StartInfo.FileName = Path.Combine(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), "Habbo")
            End If
            ClientProcess.StartInfo.Arguments = "-server " & CurrentLoginCode.ServerId & " -ticket " & CurrentLoginCode.SSOTicket
            Await Task.Run(Sub() ClientProcess.Start())
            Await Clipboard.SetTextAsync("")
        Catch
            StartNewInstanceButton.IsButtonDisabled = False
            StartNewInstanceButton2.IsButtonDisabled = False
            StartNewInstanceButton.Text = AppTranslator.LaunchClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
        End Try
    End Function

    Function MakeUnixExecutable(ByVal filePath As String) As Boolean
        Try
            ' Permisos: -rwxr--r--
            Dim executablePermissions As UnixFileMode = UnixFileMode.UserRead Or UnixFileMode.UserWrite Or UnixFileMode.UserExecute Or
                                                        UnixFileMode.GroupRead Or
                                                        UnixFileMode.OtherRead
            File.SetUnixFileMode(filePath, executablePermissions)
            Return True ' �xito
        Catch ex As Exception
            Console.WriteLine($"Error while making executable: {ex.Message}")
            Return False ' Fallo
        End Try
    End Function

    Public Async Function UpdateClient() As Task
        Try
            Dim ClientFolderPath = GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)
            Dim ClientFilePath = Path.Combine(ClientFolderPath, "ClientDownload.zip")
            Dim DownloadingClientHint = AppTranslator.DownloadingClient(CurrentLanguageInt)
            StartNewInstanceButton.Text = DownloadingClientHint
            IO.Directory.CreateDirectory(ClientFolderPath)

            Dim umaka = DownloadRemoteFileAsync(CurrentClientUrls.FlashWindowsUrl, ClientFilePath)
            Do Until umaka.IsCompleted
                StartNewInstanceButton.Text = DownloadingClientHint & " (" & CurrentDownloadProgress & "%)"
                Await Task.Delay(100)
            Loop
            StartNewInstanceButton.Text = AppTranslator.ExtractingClient(CurrentLanguageInt)

            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Await Task.Run(Sub() ZipFile.ExtractToDirectory(ClientFilePath, GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)))
                Await Task.Run(Sub() File.Delete(ClientFilePath))
            Else
                Dim itemsToSkip As New List(Of String) From {"Adobe AIR", "META-INF/signatures.xml", "META-INF/AIR/hash", "Habbo.exe"}
                Using archive As ZipArchive = ZipFile.OpenRead(ClientFilePath)
                    For Each entry As ZipArchiveEntry In archive.Entries
                        If itemsToSkip.Any(Function(item) entry.FullName.StartsWith(item, StringComparison.OrdinalIgnoreCase)) = False Then
                            Dim fullPath As String = Path.Combine(ClientFolderPath, entry.FullName)
                            Dim mydirectory = Path.GetDirectoryName(fullPath)
                            Directory.CreateDirectory(mydirectory)
                            If Directory.Exists(fullPath) = False Then
                                Await Task.Run(Sub() entry.ExtractToFile(fullPath, overwrite:=True))
                            End If
                        End If
                    Next
                End Using
                Await Task.Run(Sub() File.Delete(ClientFilePath))
                CopyLinuxPatch(ClientFolderPath)
                Await Task.Run(Sub() ZipFile.ExtractToDirectory(Path.Combine(ClientFolderPath, UnixPatchName), ClientFolderPath))
                File.Delete(Path.Combine(ClientFolderPath, UnixPatchName))
                UpdateUnixApplicationXML()
                If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then 'OSX
                    FixOSXClientStructure()
                    MakeUnixExecutable(Path.Combine(ClientFolderPath, "Habbo.app/Contents/MacOS/Habbo"))
                Else
                    MakeUnixExecutable(Path.Combine(ClientFolderPath, "Habbo")) 'Linux
                End If
            End If

            StartNewInstanceButton.IsButtonDisabled = False
            StartNewInstanceButton2.IsButtonDisabled = False
            StartNewInstanceButton.Text = AppTranslator.LaunchClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
        Catch ex As Exception
            'StartNewInstanceButton.BackColor = Colors.Red
            StartNewInstanceButton.IsButtonDisabled = False
            StartNewInstanceButton2.IsButtonDisabled = False
            StartNewInstanceButton.Text = AppTranslator.UpdateClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion

            'Clipboard.SetTextAsync(ex.ToString)
        End Try
    End Function

    Public Async Sub FixOSXClientStructure()
        ' Rutas de origen y destino
        Dim origen As String = GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)
        Dim destino As String = Path.Combine(origen, "Habbo.app/Contents/Resources")

        ' Exclusiones
        Dim carpetaExcluida As String = "Habbo.app"
        Dim archivoExcluido As String = "README.txt"

        ' Mover archivos
        For Each archivo In Directory.GetFiles(origen)
            Dim nombreArchivo As String = Path.GetFileName(archivo)
            If Not nombreArchivo.Equals(archivoExcluido, StringComparison.OrdinalIgnoreCase) Then
                Dim destinoArchivo As String = Path.Combine(destino, nombreArchivo)
                'Console.WriteLine(nombreArchivo & " > " & destino)
                File.Move(archivo, destinoArchivo)
            End If
        Next

        ' Mover carpetas
        For Each carpeta In Directory.GetDirectories(origen)
            Dim nombreCarpeta As String = Path.GetFileName(carpeta)
            If Not nombreCarpeta.Equals(carpetaExcluida, StringComparison.OrdinalIgnoreCase) Then
                Dim destinoCarpeta As String = Path.Combine(destino, nombreCarpeta)
                'Console.WriteLine(carpeta & " > " & destinoCarpeta)
                Directory.Move(carpeta, destinoCarpeta)
            End If
        Next
    End Sub

    Public Async Sub UpdateUnixApplicationXML()
        Dim ClientFolderPath = GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)
        Dim OriginalXmlPath As String = Path.Combine(ClientFolderPath, "META-INF/AIR/application.xml")
        Dim OriginalXmlVersionNumber As String
        Dim NewXmlPath As String = Path.Combine(ClientFolderPath, "application.xml")
        Dim xmlDoc As XDocument = XDocument.Load(OriginalXmlPath)
        OriginalXmlVersionNumber = xmlDoc.Root.Elements.First(Function(x) x.Name.LocalName = "versionLabel")
        xmlDoc = XDocument.Load(NewXmlPath)
        xmlDoc.Root.Elements.First(Function(x) x.Name.LocalName = "versionLabel").Value = OriginalXmlVersionNumber ' Reemplaza con el nuevo valor
        xmlDoc.Root.Elements.First(Function(x) x.Name.LocalName = "versionNumber").Value = OriginalXmlVersionNumber ' Reemplaza con el nuevo valor
        xmlDoc.Save(OriginalXmlPath)
        File.Delete(NewXmlPath)
    End Sub

    Public Async Sub CopyLinuxPatch(DestinationFolder As String)
        Dim resourceName As String = "avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/" & UnixPatchName
        Dim resourceStream As Stream = AssetLoader.Open(New Uri(resourceName))
        Using fileStream As FileStream = File.Create(Path.Combine(DestinationFolder, UnixPatchName))
            resourceStream.CopyTo(fileStream)
        End Using
    End Sub

    Public Async Sub CopyLinuxIcon(DestinationFolder As String)
        Dim resourceName As String = "avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/HabboCustomLauncherIcon.png"
        Dim resourceStream As Stream = AssetLoader.Open(New Uri(resourceName))
        Using fileStream As FileStream = File.Create(Path.Combine(DestinationFolder, "HabboCustomLauncherIcon.png"))
            resourceStream.CopyTo(fileStream)
        End Using
    End Sub

    Public Async Function DownloadRemoteFileAsync(RemoteFileUrl As String, DownloadFilePath As String) As Task(Of String)
        CurrentDownloadProgress = 0
        HttpClient.DefaultRequestHeaders.Clear()
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherUserAgent)
        Dim Response = Await HttpClient.GetAsync(RemoteFileUrl, HttpCompletionOption.ResponseHeadersRead)
        Dim totalSize = Response.Content.Headers.ContentLength
        Dim downloaded = 0
        Using stream = Await Response.Content.ReadAsStreamAsync()
            Using file = New FileStream(DownloadFilePath, FileMode.Create, FileAccess.Write)
                Dim buffer(1024) As Byte
                Dim bytesRead As Integer
                Do
                    bytesRead = Await stream.ReadAsync(buffer, 0, buffer.Length)
                    Await file.WriteAsync(buffer, 0, bytesRead)
                    downloaded += bytesRead
                    CurrentDownloadProgress = CInt(downloaded / totalSize * 100)
                Loop While bytesRead > 0
            End Using
        End Using
    End Function

    Private Async Sub StartRecursiveClipboardLoginCodeCheckAsync()
        Do While True
            Await Task.Delay(500)
            Await CheckClipboardLoginCodeAsync()
        Loop
    End Sub

    Private Async Function CheckClipboardLoginCodeAsync() As Task(Of Boolean)
        Try
            Dim ClipboardText = Await Clipboard.GetTextAsync()
            Dim ClipboardLoginCode As New LoginCode(ClipboardText)
            If String.IsNullOrWhiteSpace(ClipboardLoginCode.ServerUrl) Then
                Throw New Exception("Invalid clipboard login code")
            Else
                Dim OldLoginTicket As String = ""
                If CurrentLoginCode IsNot Nothing Then
                    OldLoginTicket = CurrentLoginCode.SSOTicket
                End If
                CurrentLoginCode = ClipboardLoginCode
                LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeDetected(CurrentLanguageInt) & " [" & ClipboardLoginCode.ServerId.Replace("hh", "").ToUpper & "]"
                If OldLoginTicket = ClipboardLoginCode.SSOTicket = False Then


                    If Window.IsActive = False Then
                        Window.WindowState = WindowState.Minimized
                        Await Task.Delay(100)
                        Window.WindowState = WindowState.Normal
                        Window.Activate()
                    End If

                    'Window.Topmost = True
                    'Window.WindowState = WindowState.Normal
                    'Window.Activate()
                    'Window.Topmost = False

                    DisplayCurrentUserOnFooter()
                    Await UpdateClientButtonStatus()
                    Return True
                End If
                'Await Application.Current.Clipboard.SetTextAsync("ServerId: " & LoginCode.ServerId & " - ServerUrl: " & LoginCode.ServerUrl & " - SSOTicket: " & LoginCode.SSOTicket)
            End If
        Catch
            CurrentLoginCode = Nothing
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeNotDetected(CurrentLanguageInt)
            StartNewInstanceButton.Text = AppTranslator.UnknownClientVersion(CurrentLanguageInt)
            DisplayLauncherVersionOnFooter()
        End Try
        Return False
    End Function

    Public Async Function CleanDeprecatedClients() As Task
        'AGREGAR OPCION PARA HABILITAR/DESHABILITAR LA LIMPIEZA AUTOMATICA DE CLIENTES OBSOLETOS?
        Try
            StartNewInstanceButton.Text = "Cleaning deprecated clients"
            Dim JsonRoot As JsonElement = JsonDocument.Parse(Await GetRemoteJsonAsync("https://images.habbo.com/habbo-native-clients/launcher/clientversions.json")).RootElement
            Dim ValidClientVersions As String() = JsonRoot.GetProperty("win").GetProperty("air").EnumerateArray().Select(Function(x) x.GetString()).ToArray
            For Each InstalledClientVersion In Directory.GetDirectories(GetPossibleClientPath("")).Select(Function(x) Path.GetFileName(x))
                If IsNumeric(InstalledClientVersion) AndAlso ValidClientVersions.Contains(InstalledClientVersion) = False Then
                    Await Task.Run(Sub() Directory.Delete(GetPossibleClientPath(InstalledClientVersion), True))
                End If
            Next
        Catch
            'We ignore the error
        End Try
    End Function

    Public Async Function UpdateClientButtonStatus() As Task
        Try
            StartNewInstanceButton.Text = AppTranslator.ClientUpdatesCheck(CurrentLanguageInt)
            CurrentClientUrls = New JsonClientUrls(Await GetRemoteJsonAsync("https://" & CurrentLoginCode.ServerUrl & "/gamedata/clienturls"))
            CleanDeprecatedClients() 'No se si lo ideal seria ponerlo aca o solo en UpdateCliente, lo malo seria que de esa forma si un cliente se actualiza a un server actualiza a una version de cliente ya existe entonces no se eliminaria la version anterior a menos que se vuelva a actualizar.
            If Directory.Exists(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)) Then 'Abria que verificar swf o mejor aun que exista un archivo READY para asegurarse que se completo todo el proceso de modificaicon
                StartNewInstanceButton.Text = AppTranslator.LaunchClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
            Else
                StartNewInstanceButton.Text = AppTranslator.UpdateClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
            End If
        Catch
            'StartNewInstanceButton.BackColor = Media.Color.FromRgb(200, 0, 0)
            StartNewInstanceButton.Text = AppTranslator.RetryClientUpdatesCheck(CurrentLanguageInt)
        End Try
        StartNewInstanceButton.IsButtonDisabled = False
        StartNewInstanceButton2.IsButtonDisabled = False
    End Function

    Public Function GetPossibleClientPath(ClientVersion As String) As String
        Dim AppDataFolderPath As String = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        Return Path.Combine(AppDataFolderPath, "Habbo Launcher/downloads/air/" & ClientVersion)
    End Function

    Public Async Function GetRemoteJsonAsync(JsonUrl As String) As Task(Of String)
        HttpClient.DefaultRequestHeaders.Clear()
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherUserAgent)
        Dim Response As HttpResponseMessage = Await HttpClient.GetAsync(JsonUrl)
        If Response.IsSuccessStatusCode Then
            Return Await Response.Content.ReadAsStringAsync()
        Else
            Return ""
        End If
    End Function

    Private Sub LoginCodeButton_Click(sender As Object, e As EventArgs) Handles LoginCodeButton.Click
        If LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeNotDetected(CurrentLanguageInt) = False Then
            Return
        End If
        Dim HabboAvatarSettingsUrl As String = "https://www.habbo.com/settings/avatars"
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("pt") Then
            HabboAvatarSettingsUrl = "https://www.habbo.com.br/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("es") Then
            HabboAvatarSettingsUrl = "https://www.habbo.es/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("de") Then
            HabboAvatarSettingsUrl = "https://www.habbo.de/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("fr") Then
            HabboAvatarSettingsUrl = "https://www.habbo.fr/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("it") Then
            HabboAvatarSettingsUrl = "https://www.habbo.it/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower = "tr" Then
            HabboAvatarSettingsUrl = "https://www.habbo.com.tr/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("nl") Then
            HabboAvatarSettingsUrl = "https://www.habbo.nl/settings/avatars"
        End If
        If Globalization.CultureInfo.CurrentCulture.Name.ToLower = "fi" Then
            HabboAvatarSettingsUrl = "https://www.habbo.fi/settings/avatars"
        End If
        Try
            Process.Start(New ProcessStartInfo(HabboAvatarSettingsUrl) With {.UseShellExecute = True})
        Catch
            'Error while launching habbo avatar settings url
        End Try
    End Sub

    Private Sub RefreshUpdateSourceText()
        Select Case UpdateSource
            Case "AIR_Official"
                ChangeUpdateSourceButton.Text = AppTranslator.UpdateSourceOfficialAir(CurrentLanguageInt)
            Case "AIR_Plus"
                ChangeUpdateSourceButton.Text = AppTranslator.UpdateSourceAirPlus(CurrentLanguageInt)
            Case Else
                ChangeUpdateSourceButton.Text = AppTranslator.UpdateSourceOfficialUnity(CurrentLanguageInt)
        End Select
    End Sub

    Private Sub ChangeUpdateSourceButton_Click(sender As Object, e As EventArgs) Handles ChangeUpdateSourceButton.Click
        Select Case UpdateSource
            Case "AIR_Official"
                ChangeUpdateSourceButton.Text = AppTranslator.UpdateSourceAirPlus(CurrentLanguageInt)
            Case "AIR_Plus"
                ChangeUpdateSourceButton.Text = AppTranslator.UpdateSourceOfficialUnity(CurrentLanguageInt)
            Case Else
                ChangeUpdateSourceButton.Text = AppTranslator.UpdateSourceOfficialAir(CurrentLanguageInt)
        End Select
    End Sub

    Private Sub StartNewInstanceButton2_Click(sender As Object, e As EventArgs) Handles StartNewInstanceButton2.Click
        'Temporalmente elimina la instalacion actual, en un futuro deberia abrirse una ventana con varias opciones
        '(Por ejemplo usar una version especifica ya descargada del cliente, borrar instalacion existente, borrar todas instalaciones, etc.)
        Try
            Directory.Delete(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), True)
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            UpdateClientButtonStatus()
        Catch ex As Exception
            'Ignore error
        End Try
    End Sub

    Public Function RegisterHabboProtocol() As Boolean
        Try
            Dim UriScheme = "habbo"
            Dim FriendlyName = "Habbo Custom Launcher"
            Dim applicationLocation As String = Process.GetCurrentProcess().MainModule.FileName
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Using key = Registry.CurrentUser.CreateSubKey("SOFTWARE\Classes\" & UriScheme)
                    key.SetValue("", "URL:" & FriendlyName)
                    key.SetValue("URL Protocol", "")

                    Using defaultIcon = key.CreateSubKey("DefaultIcon")
                        defaultIcon.SetValue("", applicationLocation & ",1")
                    End Using

                    Using commandKey = key.CreateSubKey("shell\open\command")
                        commandKey.SetValue("", """" & applicationLocation & """ ""%1""")
                    End Using
                End Using
                Return True
            End If
            If RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Or RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) Then
                AddStartMenuShortcut() 'xdg protocol association requires an start menu shortcut
                Dim processInfo As New ProcessStartInfo("xdg-mime", "default HabboCustomLauncher.desktop x-scheme-handler/habbo") With {
                    .UseShellExecute = False,
                    .CreateNoWindow = False
                }
                Process.Start(processInfo)?.WaitForExit()
                Return True
            End If
            Throw New Exception("Could not register protocol")
        Catch
            'MsgBox(AppTranslator.ProtocolRegError(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
            Return False
        End Try
    End Function

    Public Sub FixWindowsTLS()
        Try
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Using key = Registry.CurrentUser.CreateSubKey("Software\Microsoft\Windows\CurrentVersion\Internet Settings")
                    If key.GetValue("SecureProtocols") < 2048 Then 'johnou implementation
                        key.SetValue("SecureProtocols", key.GetValue("SecureProtocols") + 2048)
                    End If
                    If String.IsNullOrEmpty(key.GetValue("DefaultSecureProtocols")) = False Then
                        If key.GetValue("DefaultSecureProtocols") < 2048 Then
                            key.SetValue("DefaultSecureProtocols", key.GetValue("DefaultSecureProtocols") + 2048)
                        End If
                    End If
                End Using
                Dim NeedExtraSteps As Boolean = False
                Using key = Registry.LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client")
                    If key Is Nothing = False Then
                        If (key.GetValue("DisabledByDefault") = "1") Or (key.GetValue("Enabled") = "0") Then
                            NeedExtraSteps = True
                        End If
                    End If
                End Using
                If NeedExtraSteps = True Then
                    If WindowsUserIsAdmin() Then
                        Using key = Registry.LocalMachine.CreateSubKey("SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client")
                            key.SetValue("DisabledByDefault", 0)
                            key.SetValue("Enabled", 1)
                        End Using
                    Else
                        'MsgBox(AppTranslator.TLSFixAdminRightsError(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
                        Environment.Exit(0)
                    End If
                End If
            End If
        Catch
            Console.WriteLine("Could not fix Windows TLS.")
        End Try
    End Sub

    Function WindowsUserIsAdmin() As Boolean
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
            Dim identity As WindowsIdentity = WindowsIdentity.GetCurrent()
            Dim principal As WindowsPrincipal = New WindowsPrincipal(identity)
            Return principal.IsInRole(WindowsBuiltInRole.Administrator)
        Else
            Return False
        End If
    End Function

    Private Sub FooterButton_Click(sender As Object, e As EventArgs) Handles FooterButton.Click
        If FooterButton.Text.StartsWith(AppTranslator.PlayingAs(CurrentLanguageInt)) Then
            Dim ProfileUrl = "https://" & CurrentLoginCode.ServerUrl & "/profile/" & CurrentLoginCode.Username
            Try
                Process.Start(New ProcessStartInfo(ProfileUrl) With {.UseShellExecute = True})
            Catch
                'Error while launching habbo profile url
            End Try
        End If
    End Sub

    Private Sub GithubButton_PointerPressed(sender As Object, e As Avalonia.Input.PointerPressedEventArgs) Handles GithubButton.PointerPressed
        Try
            Process.Start(New ProcessStartInfo("https://github.com/LilithRainbows/HabboCustomLauncherBeta") With {.UseShellExecute = True})
        Catch
            'Error while launching github url
        End Try
    End Sub

    Private Sub SulakeButton_PointerPressed(sender As Object, e As Avalonia.Input.PointerPressedEventArgs) Handles SulakeButton.PointerPressed
        Try
            Process.Start(New ProcessStartInfo("https://www.sulake.com/habbo/") With {.UseShellExecute = True})
        Catch
            'Error while launching sulake url
        End Try
    End Sub

    Private Sub HabboLogoButton_PointerEntered(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles HabboLogoButton.PointerEntered
        HabboLogoButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/habbo-logo-big-2.png")))
    End Sub

    Private Sub HabboLogoButton_PointerExited(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles HabboLogoButton.PointerExited
        HabboLogoButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/habbo-logo-big.png")))
    End Sub

    Private Sub GithubButton_PointerEntered(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles GithubButton.PointerEntered
        GithubButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/github-icon-2.png")))
    End Sub

    Private Sub GithubButton_PointerExited(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles GithubButton.PointerExited
        GithubButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/github-icon.png")))
    End Sub

    Private Sub SulakeButtonButton_PointerEntered(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles SulakeButton.PointerEntered
        SulakeButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/habbo-footer-2.png")))
    End Sub

    Private Sub SulakeButtonButton_PointerExited(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles SulakeButton.PointerExited
        SulakeButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/habbo-footer.png")))
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As WindowClosingEventArgs) Handles Me.Closing
        If NamedPipeCancellationTokenSource.IsCancellationRequested = False Then
            StopPipedLoginTicketListener()
        End If
    End Sub

    Private Sub HabboLogoButton_PointerPressed(sender As Object, e As Avalonia.Input.PointerPressedEventArgs) Handles HabboLogoButton.PointerPressed
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Or RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Or RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) Then
            Dim AddDesktopShortcutMenuItem As New MenuItem With {.Header = AppTranslator.AddDesktopShortcut(CurrentLanguageInt)}
            AddHandler AddDesktopShortcutMenuItem.Click, AddressOf AddDesktopShortcut

            Dim AddStartMenuShortcutMenuItem As New MenuItem With {.Header = AppTranslator.AddStartMenuShortcut(CurrentLanguageInt)}
            AddHandler AddStartMenuShortcutMenuItem.Click, AddressOf AddStartMenuShortcut

            'Dim ToggleAutomaticHabboProtocolMenuItem As New MenuItem With {.Header = AppTranslator.AutomaticHabboProtocol(CurrentLanguageInt) & " (" & AppTranslator.Enabled(CurrentLanguageInt).ToLower & ")"}
            'AddHandler ToggleAutomaticHabboProtocolMenuItem.Click, AddressOf ToggleAutomaticHabboProtocol

            Dim contextMenu As New ContextMenu
            contextMenu.Items.Add(AddDesktopShortcutMenuItem)
            contextMenu.Items.Add(AddStartMenuShortcutMenuItem)
            'contextMenu.Items.Add(ToggleAutomaticHabboProtocolMenuItem)
            If HabboLogoButton.ContextMenu IsNot Nothing Then
                HabboLogoButton.ContextMenu.Close()
            End If
            HabboLogoButton.ContextMenu = contextMenu
            HabboLogoButton.ContextMenu.Open()
        Else
            Dim contextMenu As New ContextMenu
            contextMenu.Items.Add(New MenuItem With {.Header = "Advanced options are not yet available on OSX!"})
            If HabboLogoButton.ContextMenu IsNot Nothing Then
                HabboLogoButton.ContextMenu.Close()
            End If
            HabboLogoButton.ContextMenu = contextMenu
            HabboLogoButton.ContextMenu.Open()
        End If
    End Sub

    Private Sub AddDesktopShortcut()
        CreateShortcut(Environment.ProcessPath, "HabboCustomLauncherBeta", True)
    End Sub

    Private Sub AddStartMenuShortcut()
        CreateShortcut(Environment.ProcessPath, "HabboCustomLauncher", False)
    End Sub

    Sub ToggleAutomaticHabboProtocol()
        'TODO
    End Sub

    Sub CreateShortcut(appPath As String, appName As String, isDesktop As Boolean)
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
            Using shortcut = New WindowsShortcut With {.Path = appPath}
                If isDesktop Then
                    shortcut.Save(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), appName & ".lnk"))
                Else
                    Dim StartMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
                    Directory.CreateDirectory(StartMenuPath)
                    shortcut.Save(Path.Combine(StartMenuPath, appName & ".lnk"))
                End If
            End Using
        ElseIf RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
            'Dim desktopPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), appName)
            'Dim process = System.Diagnostics.Process.Start("ln", $"-s ""{appPath}"" ""{desktopPath}""")
            'process.WaitForExit()
            Console.WriteLine("Not implemented yet!")
        Else 'Linux
            Dim ShortcutPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop")
            Dim IconsPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".icons")
            If isDesktop = False Then
                ShortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications")
            End If
            Dim shortcutContent As String =
                $"[Desktop Entry]
                Type=Application
                Name={appName}
                Exec=""{appPath}"" %U
                Terminal=false
                Icon=HabboCustomLauncherIcon.png
                Categories=Game;
                MimeType=x-scheme-handler/habbo;".Replace("                ", "")
            Directory.CreateDirectory(IconsPath)
            Directory.CreateDirectory(ShortcutPath)
            CopyLinuxIcon(IconsPath)
            File.WriteAllText(Path.Combine(ShortcutPath, appName & ".desktop"), shortcutContent)
            MakeUnixExecutable(Path.Combine(ShortcutPath, appName & ".desktop"))
        End If
    End Sub
End Class

Public Class JsonClientUrls
    Public ReadOnly FlashWindowsVersion As Integer
    Public ReadOnly FlashWindowsUrl As String

    Public Sub New(JsonString As String)
        'Application.Current.Clipboard.SetTextAsync(JsonString).Wait()
        Dim JsonRoot As JsonElement = JsonDocument.Parse(JsonString).RootElement
        FlashWindowsVersion = Integer.Parse(JsonRoot.GetProperty("flash-windows-version").GetString())
        FlashWindowsUrl = JsonRoot.GetProperty("flash-windows").GetString()
    End Sub
End Class

Public Class LoginCode
    Public ReadOnly SSOTicket As String = ""
    Public ReadOnly ServerId As String = ""
    Public ReadOnly ServerUrl As String = ""
    Public ReadOnly Username As String = ""

    Public Sub New(LoginCode As String)
        If CheckLoginCode(LoginCode) Then
            Dim LoginServerId As String = LoginCode.Split(".")(0) 'Example: hhes
            Dim LoginTicket As String = LoginCode.Split(".")(1) & "." & LoginCode.Split(".")(2) 'Example: 11111111-1111-1111-1111-111111111111-11111111.V4
            If GetCharCount(LoginCode, ".") > 2 Then
                Username = LoginCode.Split(".")(3) 'Example: LilithRainbows
            End If
            SSOTicket = LoginTicket
            ServerId = LoginServerId
            ServerUrl = GetHabboServerUrl(ServerId)
        End If
    End Sub

    Private Function CheckLoginCode(LoginCode As String) As Boolean
        If GetCharCount(LoginCode, ".") >= 2 Then
            For Each HabboServer In GetHabboServers()
                If LoginCode.StartsWith(HabboServer.Id & ".") Then
                    Return True
                End If
            Next
        End If
        Return False
    End Function

    Private Function GetHabboServerUrl(ServerId As String) As String
        For Each HabboServer In GetHabboServers()
            If HabboServer.Id = ServerId Then
                Return HabboServer.Url
            End If
        Next
        Return ""
    End Function

    Private Function GetHabboServers() As List(Of HabboServer)
        Return New List(Of HabboServer) From {
            New HabboServer("hhus", "www.habbo.com"),
            New HabboServer("hhfr", "www.habbo.fr"),
            New HabboServer("hhes", "www.habbo.es"),
            New HabboServer("hhbr", "www.habbo.com.br"),
            New HabboServer("hhfi", "www.habbo.fi"),
            New HabboServer("hhtr", "www.habbo.com.tr"),
            New HabboServer("hhde", "www.habbo.de"),
            New HabboServer("hhnl", "www.habbo.nl"),
            New HabboServer("hhit", "www.habbo.it"),
            New HabboServer("local", "localhost:s3dcom:3000"),
            New HabboServer("hhs1", "s1.varoke.net"),
            New HabboServer("hhs2", "sandbox.habbo.com"),
            New HabboServer("duke", "duke.varoke.net"),
            New HabboServer("d63", "d63.varoke.net"),
            New HabboServer("dev", "dev.varoke.net"),
            New HabboServer("hhxd", "habbox.varoke.net"),
            New HabboServer("hhxp", "www.habbox.game")
        }
    End Function

    Private Function GetCharCount(Input As String, RequestedChar As Char) As Integer
        Return Input.Count(Function(x) x = RequestedChar)
    End Function
End Class

Public Class HabboServer
    Public ReadOnly Id As String = ""
    Public ReadOnly Url As String = ""

    Public Sub New(ServerId As String, ServerUrl As String)
        Id = ServerId
        Url = ServerUrl
    End Sub
End Class

Public Class AppTranslator
    '0=English 1=Spanish
    Public Shared DownloadingClient As String() = {
        "Downloading client",
        "Descargando cliente"
    }
    Public Shared ExtractingClient As String() = {
        "Extracting client",
        "Extrayendo cliente"
    }
    Public Shared PlayingAs As String() = {
        "Playing as",
        "Jugando como"
    }
    Public Shared ClipboardLoginCodeDetected As String() = {
        "Clipboard login code detected",
        "Codigo de inicio de sesion del portapapeles detectado"
    }
    Public Shared ClipboardLoginCodeNotDetected As String() = {
        "Clipboard login code not detected",
        "Codigo de inicio de sesion del portapapeles no detectado"
    }
    Public Shared UnknownClientVersion As String() = {
        "Unknown client version",
        "Version del cliente desconocida"
    }
    Public Shared UpdateSourceOfficialAir As String() = {
        "Current update source: AIR (Official)",
        "Fuente de actualizaciones: AIR (Oficial)"
    }
    Public Shared UpdateSourceAirPlus As String() = {
        "Current update source: AIR Plus (Unofficial)",
        "Fuente de actualizaciones: AIR Plus (No oficial)"
    }
    Public Shared UpdateSourceOfficialUnity As String() = {
        "Current update source: Unity (Official)",
        "Fuente de actualizaciones: Unity (Oficial)"
    }
    Public Shared RetryClientUpdatesCheck As String() = {
        "Retry to check for client updates",
        "Reintentar verificar actualizaciones del cliente"
    }
    Public Shared ClientUpdatesCheck As String() = {
        "Checking for client updates",
        "Verificando actualizaciones del cliente"
    }
    Public Shared UpdateClientVersion As String() = {
        "Update client to version",
        "Actualizar cliente a la version"
    }
    Public Shared LaunchClientVersion As String() = {
        "Launch client version",
        "Ejecutar cliente version"
    }
    Public Shared Enabled As String() = {
        "Enabled",
        "Habilitado"
    }
    Public Shared Disabled As String() = {
        "Disabled",
        "Deshabilitado"
    }
    Public Shared AddDesktopShortcut As String() = {
        "Add shortcut to desktop",
        "A�adir acceso directo al escritorio"
    }
    Public Shared AddStartMenuShortcut As String() = {
        "Add shortcut to start menu",
        "A�adir acceso directo al menu de inicio"
    }
    Public Shared AutomaticHabboProtocol As String() = {
        "Automatic habbo protocol",
        "Habbo protocol automatico"
    }
End Class