Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text.Json
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media
Imports Avalonia.Platform
Imports Microsoft.Win32

'PROBLEMA: Como se puede hacer para que se pueda definir manualmente un login code en lugar de solo leerlo desde el clipboard?
'SOLUCION: Al hacer click al boton se pregunta para introducir manualmente el codigo (aunque seria innecesario), mejor preguntar que hotel lanzar directamente? o tambien es innecesario? si todo es innecesario capaz convenga hacer que deje de ser un boton y pase a ser un label
'Quizas habria que revisar en https://images.habbo.com/habbo-native-clients/launcher/clientversions.json para purgar versiones ya invalidas
'Ver que hacer con el tema de avalonia, si se lo actualiza a la ultima version capaz anda mejor/mas rapido pero no andaria bien en windows 7 (si es que siquiera abre) (recordar problema de fonts en windows 7 con versiones nuevas de avalonia y su posible solucion usando text to image)

Partial Public Class MainWindow : Inherits Window
    Private WithEvents Window As Window
    Private WithEvents StartNewInstanceButton As CustomButton
    Private WithEvents StartNewInstanceButton2 As CustomButton
    Private WithEvents LoginCodeButton As CustomButton
    Private WithEvents ChangeUpdateSourceButton As CustomButton
    Private WithEvents GithubButton As Image
    'Private WithEvents CopyrightLabel As Label
    Private WithEvents FooterButton As CustomButton
    Public CurrentLoginCode As LoginCode
    Public CurrentClientUrls As JsonClientUrls
    Public CurrentDownloadProgress As Integer
    Public UpdateSource As String = "AIR_Official"
    Public CurrentLanguageInt As Integer = 0
    Private ReadOnly HttpClient As New HttpClient()
    Private LauncherUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HabboLauncher/1.0.41 Chrome/87.0.4280.141 Electron/11.3.0 Safari/537.36"

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
        GithubButton = Window.FindNameScope.Find("GithubButton")
        'CopyrightLabel = Window.FindNameScope.Find("CopyrightLabel")
        'CopyrightLabel.Content = AppTranslator.Copyright(CurrentLanguageInt)
        FooterButton = Window.FindNameScope.Find("FooterButton")
        DisplayLauncherVersionOnFooter()
        RefreshUpdateSourceText()
        CheckClipboardLoginCodeAsync()
        FixWindowsTLS()
    End Sub

    Private Function DisplayLauncherVersionOnFooter() As String
        FooterButton.BackColor = Color.Parse("Transparent")
        FooterButton.Text = "CustomLauncher version 1 (18/12/2024)"
    End Function

    Private Function DisplayCurrentUserOnFooter() As String
        If String.IsNullOrWhiteSpace(CurrentLoginCode.Username) Then
            DisplayLauncherVersionOnFooter()
        Else
            FooterButton.BackColor = Color.Parse("#8D31A500")
            FooterButton.Text = AppTranslator.PlayingAs(CurrentLanguageInt) & " " & CurrentLoginCode.Username
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
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                ClientProcess.StartInfo.FileName = Path.Combine(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), "Habbo.exe")
            Else
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

    Function MakeExecutable(ByVal filePath As String) As Boolean
        Try
            ' Permisos: -rwxr--r--
            Dim executablePermissions As UnixFileMode = UnixFileMode.UserRead Or UnixFileMode.UserWrite Or UnixFileMode.UserExecute Or
                                                        UnixFileMode.GroupRead Or
                                                        UnixFileMode.OtherRead
            File.SetUnixFileMode(filePath, executablePermissions)
            Return True ' Éxito
        Catch ex As Exception
            Console.WriteLine($"Error: {ex.Message}")
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
                Await Task.Run(Sub() ZipFile.ExtractToDirectory(Path.Combine(ClientFolderPath, "HabboAirLinuxPatch_x64.zip"), ClientFolderPath))
                File.Delete(Path.Combine(ClientFolderPath, "HabboAirLinuxPatch_x64.zip"))
                UpdateApplicationXML()
                MakeExecutable(Path.Combine(ClientFolderPath, "Habbo"))
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

    Public Async Sub UpdateApplicationXML()
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
        Dim resourceName As String = "avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/HabboAirLinuxPatch_x64.zip"
        Dim resourceStream As Stream = AssetLoader.Open(New Uri(resourceName))
        Using fileStream As FileStream = File.Create(Path.Combine(DestinationFolder, "HabboAirLinuxPatch_x64.zip"))
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

    Private Async Sub CheckClipboardLoginCodeAsync()
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
                    DisplayCurrentUserOnFooter()
                    Await UpdateClientButtonStatus()
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
        Await Task.Delay(500)
        CheckClipboardLoginCodeAsync()
    End Sub

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
        Clipboard.SetTextAsync("hhes..")
        CheckClipboardLoginCodeAsync()
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
        'Temporalmente elimina la instalacion actual, en un futuro deberia abrirse uan ventana con varias opciones
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
        Dim ProfileUrl = "https://" & CurrentLoginCode.ServerUrl & "/profile/" & CurrentLoginCode.Username
        Process.Start(New ProcessStartInfo(ProfileUrl) With {.UseShellExecute = True})
    End Sub

    Private Sub GithubButton_PointerPressed(sender As Object, e As Avalonia.Input.PointerPressedEventArgs) Handles GithubButton.PointerPressed
        Process.Start(New ProcessStartInfo("https://github.com/LilithRainbows/HabboCustomLauncherBeta") With {.UseShellExecute = True})
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
End Class