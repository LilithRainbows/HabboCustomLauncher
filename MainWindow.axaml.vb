Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports Avalonia
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

Partial Public Class MainWindow : Inherits Window
    Public WithEvents LoadingWindowChild As LoadingWindow
    Private LoadingWindowClientLaunchRequested As Boolean = False
    Private WithEvents Window As Window
    Private WithEvents TitleBarLabel As Label
    Private WithEvents CloseButton As CustomButton
    Public WithEvents StartNewInstanceButton As CustomButton
    Private WithEvents StartNewInstanceButton2 As CustomButton
    Private WithEvents LoginCodeButton As CustomButton
    Private WithEvents ChangeUpdateSourceButton As CustomButton
    Private WithEvents ChangeUpdateSourceButton2 As CustomButton
    Private WithEvents HabboLogoButton As Image
    Private WithEvents GithubButton As Image
    Private WithEvents SulakeButton As Image
    Private WithEvents FooterButton As CustomButton
    Public CurrentLoginCode As LoginCode
    Public CurrentClientUrls As JsonClientUrls
    Public CurrentDownloadProgress As Integer
    Public UpdateSource As String = "AIR_Plus"
    Public CurrentLanguageInt As Integer = 0
    Private ReadOnly HttpClient As New HttpClient()
    Public UnixPatchName As String = "HabboAirLinuxPatch_x64.zip" 'Depending on the platform, it can automatically become HabboAirLinuxPatch_x64.zip and HabboAirOSXPatch.zip
    Public WindowsPatchName As String = "HabboAirWindowsPatch_x86.zip" 'Depending on the architecture, it can automatically become HabboAirWindowsPatch_x64.zip
    Public AirPlusPatchName As String = "HabboAirPlusPatch.zip"
    Public LauncherShortcutOSXPatchName As String = "LauncherShortcutOSXPatch.zip"
    Public AirPlusClientURL = "https://github.com/LilithRainbows/HabboAirPlus/releases/download/latest/HabboAir.swf"
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
        TitleBarLabel = Window.FindNameScope.Find("TitleBarLabel")
        CloseButton = FindNameScope().Find("CloseButton")
        StartNewInstanceButton = Window.FindNameScope.Find("StartNewInstanceButton")
        StartNewInstanceButton2 = Window.FindNameScope.Find("StartNewInstanceButton2")
        LoginCodeButton = Window.FindNameScope.Find("LoginCodeButton")
        ChangeUpdateSourceButton = Window.FindNameScope.Find("ChangeUpdateSourceButton")
        ChangeUpdateSourceButton2 = Window.FindNameScope.Find("ChangeUpdateSourceButton2")
        HabboLogoButton = Window.FindNameScope.Find("HabboLogoButton")
        GithubButton = Window.FindNameScope.Find("GithubButton")
        SulakeButton = Window.FindNameScope.Find("SulakeButton")
        FooterButton = Window.FindNameScope.Find("FooterButton")

        Singleton.GetCurrentInstance().ScaleMainGrid(Window)
        Singleton.GetCurrentInstance().MainWindow = Me

        LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeNotDetected(CurrentLanguageInt)
        StartNewInstanceButton.Text = AppTranslator.UnknownClientVersion(CurrentLanguageInt)

        DisplayLauncherVersionOnFooter()
        RefreshUpdateSourceText()
        FixWindowsTLS()
        RegisterHabboProtocol()

        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) AndAlso RuntimeInformation.OSArchitecture = Architecture.X64 Then
            WindowsPatchName = "HabboAirWindowsPatch_x64.zip"
        End If
        If (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Or RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) AndAlso RuntimeInformation.ProcessArchitecture = Architecture.Arm64 Then
            UnixPatchName = "HabboAirLinuxPatch_arm64.zip"
        End If
        If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
            If OperatingSystem.IsMacOSVersionAtLeast(26, 0) Then
                UnixPatchName = "HabboAirOSXTahoePatch.zip"
            Else
                UnixPatchName = "HabboAirOSXPatch.zip"
            End If
        End If

        LoadSavedUpdateSource()

        Dim HabboProtocol = Environment.GetCommandLineArgs().FirstOrDefault(Function(x) x.StartsWith("habbo://"), "")
        If HabboProtocol = "" Then
            Window.Show()
        Else
            LoadingWindowChild = New LoadingWindow()
            LoadingWindowChild.StatusLabel.Text = AppTranslator.GenericLoading(CurrentLanguageInt) & " ..." 'Generic loading
            LoadingWindowChild.Show()
            CopyToClipboard(HabboProtocol)
        End If
        StartRecursiveClipboardLoginCodeCheckAsync()
    End Sub

    Public Function GetAirPatchNameForCurrentOS() As String
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
            Return WindowsPatchName
        Else
            Return UnixPatchName
        End If
    End Function

    Public Async Function CopyToClipboard(Argument As String) As Task(Of Boolean)
        Try
            Await Clipboard.SetTextAsync(Argument)
        Catch ex As Exception
            Return False
        End Try
        Return True
    End Function

    Private Function DisplayLauncherVersionOnFooter() As String
        FooterButton.BackColor = Color.Parse("Transparent")
        FooterButton.Text = "CustomLauncher version 28 (18/04/2026)"
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
            ChangeUpdateSourceButton.IsButtonDisabled = True
            ChangeUpdateSourceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            UpdateClientButtonStatus()
        End If
        If StartNewInstanceButton.Text.StartsWith(AppTranslator.UpdateClientVersion(CurrentLanguageInt)) Then
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            ChangeUpdateSourceButton.IsButtonDisabled = True
            ChangeUpdateSourceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            UpdateClient()
        End If
        If StartNewInstanceButton.Text.StartsWith(AppTranslator.LaunchClientVersion(CurrentLanguageInt)) Then
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            ChangeUpdateSourceButton.IsButtonDisabled = True
            ChangeUpdateSourceButton2.IsButtonDisabled = True
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
                ClientProcess.StartInfo.FileName = Path.Combine(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), "Habbo.app", "Contents", "MacOS", "Habbo")
            Else 'Linux
                ClientProcess.StartInfo.FileName = Path.Combine(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), "Habbo")
            End If
            ClientProcess.StartInfo.Arguments = "-server " & CurrentLoginCode.ServerId & " -ticket " & CurrentLoginCode.SSOTicket
            Await Task.Run(Sub() ClientProcess.Start())
            CurrentLoginCode = Nothing
        Catch ex As Exception
            StartNewInstanceButton.IsButtonDisabled = False
            StartNewInstanceButton2.IsButtonDisabled = False
            ChangeUpdateSourceButton.IsButtonDisabled = False
            ChangeUpdateSourceButton2.IsButtonDisabled = False
            StartNewInstanceButton.Text = AppTranslator.LaunchClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
            MsgBox(AppTranslator.ErrorDebugClipboardHint(CurrentLanguageInt), AppTranslator.ClientLaunchError(CurrentLanguageInt), ex.Message)
        End Try
    End Function

    Public Async Function MsgBox(Title As String, Message As String, Optional ClipboardDebugContent As String = "") As Task(Of Boolean)
        Dim ErrorDialog As New MessageBox()
        ErrorDialog.ConfigureContent(Title, Message, ClipboardDebugContent)
        Do While Window.IsVisible = False
            Await Task.Delay(100)
        Loop
        Await ErrorDialog.ShowDialog(Window)
        Return True
    End Function

    Function MakeUnixExecutable(ByVal filePath As String) As Boolean
        Dim process As New Process()
        process.StartInfo.FileName = "chmod"
        process.StartInfo.Arguments = $"+x ""{filePath}"""
        process.StartInfo.UseShellExecute = False
        process.StartInfo.CreateNoWindow = True
        process.Start()
        process.WaitForExit()
    End Function

    Sub CodesignFile(filePath As String)
        Dim p As New Process()
        p.StartInfo.FileName = "/usr/bin/codesign"
        p.StartInfo.Arguments = $"--force --timestamp=none --sign - ""{filePath}"""
        p.StartInfo.UseShellExecute = False
        p.StartInfo.CreateNoWindow = True
        p.Start()
        p.WaitForExit()
    End Sub

    Public Sub UnzipFile(sourcezip As String, destinationfolder As String, overwrite As Boolean, Optional itemsToSkip As List(Of String) = Nothing, Optional IgnoreIOExceptions As Boolean = False)
        Dim basePath As String = Path.GetFullPath(destinationfolder)
        Using archive As ZipArchive = ZipFile.OpenRead(sourcezip)
            For Each entry As ZipArchiveEntry In archive.Entries
                Try
                    Dim relativePath As String = entry.FullName.Replace("/"c, Path.DirectorySeparatorChar).Replace("\"c, Path.DirectorySeparatorChar)

                    ' ---- FILTRO DE EXCLUSIÓN ----
                    If itemsToSkip IsNot Nothing Then

                        Dim normalized = relativePath.TrimStart(Path.DirectorySeparatorChar)
                        Dim skipEntry As Boolean = False

                        For Each skip In itemsToSkip
                            Dim skipNorm = skip.Replace("/"c, Path.DirectorySeparatorChar).Replace("\"c, Path.DirectorySeparatorChar)
                            If String.Equals(normalized, skipNorm, StringComparison.OrdinalIgnoreCase) Then
                                skipEntry = True
                                Exit For
                            End If
                            If normalized.StartsWith(skipNorm & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then
                                skipEntry = True
                                Exit For
                            End If
                        Next
                        If skipEntry Then Continue For
                    End If
                    ' -----------------------------

                    Dim destinationFilePath As String = Path.GetFullPath(Path.Combine(basePath, relativePath))
                    If Not destinationFilePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) Then
                        Throw New IOException("Zip slip error!")
                    End If
                    If String.IsNullOrEmpty(entry.Name) Then
                        Directory.CreateDirectory(destinationFilePath)
                        Continue For
                    End If
                    Dim dir As String = Path.GetDirectoryName(destinationFilePath)
                    If Not String.IsNullOrEmpty(dir) Then
                        Directory.CreateDirectory(dir)
                    End If
                    entry.ExtractToFile(destinationFilePath, overwrite)
                Catch ex As IOException
                    If Not IgnoreIOExceptions Then Throw
                End Try
            Next
        End Using
    End Sub

    Sub ReplaceSwfVersion(rutaArchivo As String, nuevoValorInt As Integer)
        Dim datos As Byte() = File.ReadAllBytes(rutaArchivo)
        datos(3) = CByte(nuevoValorInt)
        File.WriteAllBytes(rutaArchivo, datos)
    End Sub

    Function GetSwfType(SwfPath As String) As String
        Using br As New BinaryReader(File.OpenRead(SwfPath))
            br.BaseStream.Seek(0, SeekOrigin.Begin)
            Return Encoding.UTF8.GetString(br.ReadBytes(4))
        End Using
        Throw New Exception("GetSwfType failed!")
    End Function


    Public Async Function UpdateClient() As Task
        Try
            Dim ClientFolderPath = GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)
            Dim ClientFilePath = Path.Combine(ClientFolderPath, "ClientDownload.zip")
            If UpdateSource = "AIR_Plus" Then
                ClientFilePath = Path.Combine(ClientFolderPath, "HabboAir.swf")
            End If
            Dim DownloadingClientHint = AppTranslator.DownloadingClient(CurrentLanguageInt)
            StartNewInstanceButton.Text = DownloadingClientHint
            Directory.CreateDirectory(ClientFolderPath)


            Dim ClientUrl = CurrentClientUrls.FlashWindowsUrl
            Dim umaka = DownloadRemoteFileAsync(ClientUrl, ClientFilePath)
            Do Until umaka.IsCompleted
                StartNewInstanceButton.Text = DownloadingClientHint & " (" & CurrentDownloadProgress & "%)"
                Await Task.Delay(100)
            Loop
            StartNewInstanceButton.Text = AppTranslator.ExtractingClient(CurrentLanguageInt)


            Await Task.Run(Sub() CopyEmbeddedAsset(GetAirPatchNameForCurrentOS, ClientFolderPath))
            Await Task.Run(Sub() UnzipFile(Path.Combine(ClientFolderPath, GetAirPatchNameForCurrentOS), ClientFolderPath, True))
            File.Delete(Path.Combine(ClientFolderPath, GetAirPatchNameForCurrentOS))


            If UpdateSource = "AIR_Plus" Then
                Await Task.Run(Sub() CopyEmbeddedAsset(AirPlusPatchName, ClientFolderPath))
                Await Task.Run(Sub() UnzipFile(Path.Combine(ClientFolderPath, AirPlusPatchName), ClientFolderPath, True))
                File.Delete(Path.Combine(ClientFolderPath, AirPlusPatchName))
            Else
                Dim itemsToSkip As New List(Of String) From {"Adobe AIR", "META-INF/signatures.xml", "META-INF/AIR/hash", "Habbo.exe"}
                Await Task.Run(Sub() UnzipFile(ClientFilePath, ClientFolderPath, True, itemsToSkip))
                Await Task.Run(Sub() File.Delete(ClientFilePath))
                Dim ClientSwfType = GetSwfType(Path.Combine(ClientFolderPath, "HabboAir.swf"))
                If ClientSwfType.StartsWith("cWS") Or ClientSwfType.StartsWith("fWS") Or ClientSwfType.StartsWith("zWS") Then
                    Await Task.Run(Sub() AirSwfDecryptor.FlashCrypto.DecryptFile(Path.Combine(ClientFolderPath, "HabboAir.swf"), Path.Combine(ClientFolderPath, "HabboAir.swf"))) 'The swf is decrypted (if needed) so that it can later be edited for OSX (the user can also see/edit it)
                End If
            End If

            UpdateAirApplicationXML()

            If IO.File.ReadAllText(Path.Combine(ClientFolderPath, "META-INF", "AIR", "application.xml")).Contains("<extensions>") Then
                If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                    Dim HabboAirExtensionsOSXPatchName = "HabboAirExtensionsOSXPatch.zip"
                    Await Task.Run(Sub() CopyEmbeddedAsset(HabboAirExtensionsOSXPatchName, ClientFolderPath))
                    Await Task.Run(Sub() UnzipFile(Path.Combine(ClientFolderPath, HabboAirExtensionsOSXPatchName), ClientFolderPath, True))
                    File.Delete(Path.Combine(ClientFolderPath, HabboAirExtensionsOSXPatchName))
                End If
                If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                    Dim HabboAirExtensionsWindowsPatchName = "HabboAirExtensionsWindowsPatch.zip"
                    Await Task.Run(Sub() CopyEmbeddedAsset(HabboAirExtensionsWindowsPatchName, ClientFolderPath))
                    Await Task.Run(Sub() UnzipFile(Path.Combine(ClientFolderPath, HabboAirExtensionsWindowsPatchName), ClientFolderPath, True))
                    File.Delete(Path.Combine(ClientFolderPath, HabboAirExtensionsWindowsPatchName))
                End If
            End If

            Dim AirCustomLicensePath = Path.Combine(ClientFolderPath, "license.txt")
            If IO.File.Exists(AirCustomLicensePath) Then
                IO.File.Move(AirCustomLicensePath, Path.Combine(ClientFolderPath, "META-INF", "AIR", "license.txt"), True)
            End If

            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                If OperatingSystem.IsMacOSVersionAtLeast(26, 0) Then
                    ReplaceSwfVersion(Path.Combine(ClientFolderPath, "HabboAir.swf"), 51) 'OSX Tahoe and later needs AIR 51+ to avoid keyboard shortcuts bugs
                Else
                    ReplaceSwfVersion(Path.Combine(ClientFolderPath, "HabboAir.swf"), 50) 'OSX is limited to AIR version 50.2.3.8 to improve performance and provide compatibility with OSX 10.12+, so the swf version will be forced to 50
                End If
            Else
                ReplaceSwfVersion(Path.Combine(ClientFolderPath, "HabboAir.swf"), 51) 'Windows and Linux works with AIR 51+
            End If

            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                FixOSXClientStructure()
                Dim ExecutableFiles As New List(Of String) From {
                    Path.Combine(ClientFolderPath, "Habbo.app", "Contents", "Frameworks", "DiscordRichPresence.framework", "Versions", "A", "DiscordRichPresence"),
                    Path.Combine(ClientFolderPath, "Habbo.app", "Contents", "Frameworks", "Adobe AIR.framework", "Versions", "1.0", "Adobe AIR"),
                    Path.Combine(ClientFolderPath, "Habbo.app", "Contents", "MacOS", "Habbo")
                }
                For Each ExecutableFile In ExecutableFiles
                    If File.Exists(ExecutableFile) Then
                        MakeUnixExecutable(ExecutableFile)
                    End If
                Next
                Dim CodesignFilesOrDirectories As New List(Of String) From {
                    Path.Combine(ClientFolderPath, "Habbo.app", "Contents", "Frameworks", "DiscordRichPresence.framework"),
                    Path.Combine(ClientFolderPath, "Habbo.app", "Contents", "Frameworks", "Adobe AIR.framework"),
                    Path.Combine(ClientFolderPath, "Habbo.app", "Contents", "MacOS", "Habbo"),
                    Path.Combine(ClientFolderPath, "Habbo.app")
                }
                For Each CodesignFileOrDirectory In CodesignFilesOrDirectories
                    If File.Exists(CodesignFileOrDirectory) OrElse Directory.Exists(CodesignFileOrDirectory) Then
                        CodesignFile(CodesignFileOrDirectory)
                    End If
                Next
            ElseIf RuntimeInformation.IsOSPlatform(OSPlatform.Windows) = False Then
                MakeUnixExecutable(Path.Combine(ClientFolderPath, "Habbo")) 'Linux
            End If

            If UpdateSource = "AIR_Plus" Then
                Dim AirPlusClientLatestVersion = Await GetRemoteLastModifiedHeaderEpoch(AirPlusClientURL)
                If CurrentClientUrls.FlashWindowsVersion = AirPlusClientLatestVersion = False Then
                    Throw New Exception("AirPlus remote client version mismatch") 'Es muy poco probable pero este error ocurre si el identificador de la version remota de airplus cambio desde que se inicio el proceso de descarga
                End If
            End If

            File.WriteAllText(Path.Combine(ClientFolderPath, "VERSION.txt"), CurrentClientUrls.FlashWindowsVersion)
            If UpdateSource = "AIR_Plus" = False Then
                AddOrUpdateInstallation(CurrentClientUrls.FlashWindowsVersion, ClientFolderPath, "air", 0)
            End If

            StartNewInstanceButton.IsButtonDisabled = False
            StartNewInstanceButton2.IsButtonDisabled = False
            ChangeUpdateSourceButton.IsButtonDisabled = False
            ChangeUpdateSourceButton2.IsButtonDisabled = False
            StartNewInstanceButton.Text = AppTranslator.LaunchClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
        Catch ex As Exception
            'StartNewInstanceButton.BackColor = Colors.Red
            StartNewInstanceButton.IsButtonDisabled = False
            StartNewInstanceButton2.IsButtonDisabled = False
            ChangeUpdateSourceButton.IsButtonDisabled = False
            ChangeUpdateSourceButton2.IsButtonDisabled = False
            StartNewInstanceButton.Text = AppTranslator.RetryClientUpdatesCheck(CurrentLanguageInt)
            'Clipboard.SetTextAsync(ex.ToString)
            MsgBox(AppTranslator.ErrorDebugClipboardHint(CurrentLanguageInt), AppTranslator.ClientUpdateError(CurrentLanguageInt), ex.Message)
        End Try
    End Function

    Public Sub FixOSXClientStructure()
        ' Rutas de origen y destino
        Dim origen As String = GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)
        Dim destino As String = Path.Combine(origen, "Habbo.app", "Contents", "Resources")
        Directory.CreateDirectory(destino)

        ' Exclusiones
        Dim carpetaExcluida As String = "Habbo.app"
        Dim archivoExcluido As String = "README.txt"

        ' Mover archivos
        For Each archivo In Directory.GetFiles(origen)
            Dim nombreArchivo As String = Path.GetFileName(archivo)
            If Not nombreArchivo.Equals(archivoExcluido, StringComparison.OrdinalIgnoreCase) Then
                Dim destinoArchivo As String = Path.Combine(destino, nombreArchivo)
                If Not String.Equals(archivo, destinoArchivo, StringComparison.OrdinalIgnoreCase) Then
                    File.Move(archivo, destinoArchivo, True)
                End If
            End If
        Next

        ' Mover carpetas
        For Each carpeta In Directory.GetDirectories(origen)
            Dim nombreCarpeta As String = Path.GetFileName(carpeta)
            If Not nombreCarpeta.Equals(carpetaExcluida, StringComparison.OrdinalIgnoreCase) Then
                Dim destinoCarpeta = Path.Combine(destino, nombreCarpeta)
                MoveMerge(carpeta, destinoCarpeta)
            End If
        Next
    End Sub

    Sub MoveMerge(sourceDir As String, targetDir As String)
        Directory.CreateDirectory(targetDir)
        For Each CurrFile In Directory.GetFiles(sourceDir)
            Dim destFile = Path.Combine(targetDir, Path.GetFileName(CurrFile))
            If Not String.Equals(CurrFile, destFile, StringComparison.OrdinalIgnoreCase) Then
                File.Move(CurrFile, destFile, True)
            End If
        Next
        For Each CurrDir In Directory.GetDirectories(sourceDir)
            Dim destSubDir = Path.Combine(targetDir, Path.GetFileName(CurrDir))
            MoveMerge(CurrDir, destSubDir)
        Next
        If Directory.Exists(sourceDir) AndAlso Not Directory.EnumerateFileSystemEntries(sourceDir).Any() Then
            Directory.Delete(sourceDir)
        End If
    End Sub

    Public Sub UpdateAirApplicationXML()
        Dim ClientFolderPath = GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion)
        Dim OriginalXmlPath As String = Path.Combine(ClientFolderPath, "META-INF", "AIR", "application.xml")
        Dim OriginalXmlVersionNumber As String
        Dim OriginalXmlExtensionsNode As XElement
        Dim NewXmlPath As String = Path.Combine(ClientFolderPath, "application.xml")
        Dim xmlDoc As New XDocument()
        If IO.File.Exists(OriginalXmlPath) Then
            xmlDoc = XDocument.Load(OriginalXmlPath)
            OriginalXmlVersionNumber = xmlDoc.Root.Elements.First(Function(x) x.Name.LocalName = "versionLabel")
            OriginalXmlExtensionsNode = xmlDoc.Root.Elements().FirstOrDefault(Function(x) x.Name.LocalName = "extensions")
        Else
            OriginalXmlVersionNumber = "1.0"
            OriginalXmlExtensionsNode = Nothing
        End If
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) = False AndAlso RuntimeInformation.IsOSPlatform(OSPlatform.OSX) = False Then
            OriginalXmlExtensionsNode = Nothing 'In the future, AIR extensions support for Linux, if possible, should be added.
        End If
        xmlDoc = XDocument.Load(NewXmlPath)
        xmlDoc.Root.Elements.First(Function(x) x.Name.LocalName = "versionLabel").Value = OriginalXmlVersionNumber ' Reemplaza con el nuevo valor
        xmlDoc.Root.Elements.First(Function(x) x.Name.LocalName = "versionNumber").Value = OriginalXmlVersionNumber ' Reemplaza con el nuevo valor
        If OriginalXmlExtensionsNode IsNot Nothing Then
            Dim NewXmlNamespace As XNamespace = xmlDoc.Root.Name.Namespace
            xmlDoc.Root.Add(New XElement(NewXmlNamespace + OriginalXmlExtensionsNode.Name.LocalName, OriginalXmlExtensionsNode.Elements().Select(Function(e) New XElement(NewXmlNamespace + e.Name.LocalName, e.Value))))
        End If
        xmlDoc.Save(OriginalXmlPath)
        File.Delete(NewXmlPath)
    End Sub

    Public Sub CopyEmbeddedAsset(AssetName As String, DestinationFolder As String)
        Dim resourceName As String = "avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/" & AssetName
        Dim resourceStream As Stream = AssetLoader.Open(New Uri(resourceName))
        Using fileStream As FileStream = File.Create(Path.Combine(DestinationFolder, AssetName))
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
        Await CheckClipboardLoginCodeAsync()
        Do While True
            Await Task.Delay(500)
            Await CheckClipboardLoginCodeAsync()
        Loop
    End Sub


    Public Async Function EnsureCurrentWindowFocus() As Task(Of Boolean)
        If LoadingWindowChild Is Nothing And Window.IsActive = False Then
            Await EnsureWindowFocus(Me)
        End If
        If LoadingWindowChild IsNot Nothing AndAlso LoadingWindowChild.IsActive = False Then
            Await EnsureWindowFocus(LoadingWindowChild)
        End If
        Return True
    End Function

    Private Async Function CheckClipboardLoginCodeAsync() As Task(Of Boolean)
        Try

            Dim ClipboardText = Await Clipboard.GetTextAsync()


            If ClipboardText.StartsWith("hcl_main_focus_") Then
                ClipboardText = ClipboardText.Replace("hcl_main_focus_", "")
                Await EnsureCurrentWindowFocus()
                Await CopyToClipboard(ClipboardText)
            End If
            Dim ClipboardLoginCode As New LoginCode(ClipboardText)
            If String.IsNullOrWhiteSpace(ClipboardLoginCode.ServerUrl) Then
                Throw New Exception("Invalid clipboard login code")
            Else
                Dim OldLoginTicket As String = ""
                If CurrentLoginCode IsNot Nothing Then
                    OldLoginTicket = CurrentLoginCode.SSOTicket
                End If
                CurrentLoginCode = ClipboardLoginCode
                Await CopyToClipboard("")
                LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeDetected(CurrentLanguageInt) & " [" & ClipboardLoginCode.ServerId.Replace("hh", "").ToUpper & "]"
                If OldLoginTicket = ClipboardLoginCode.SSOTicket = False Then
                    Await EnsureCurrentWindowFocus()
                    DisplayCurrentUserOnFooter()
                    Await UpdateClientButtonStatus()
                    Return True
                End If
                'Await Application.Current.Clipboard.SetTextAsync("ServerId: " & LoginCode.ServerId & " - ServerUrl: " & LoginCode.ServerUrl & " - SSOTicket: " & LoginCode.SSOTicket)
            End If
        Catch ex As Exception
            If CurrentLoginCode IsNot Nothing Then
                Return False 'Ignore invalid clipboard login codes if there is already a valid login code
            End If
            CurrentLoginCode = Nothing
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            ChangeUpdateSourceButton.IsButtonDisabled = True
            ChangeUpdateSourceButton2.IsButtonDisabled = True
            LoginCodeButton.Text = AppTranslator.ClipboardLoginCodeNotDetected(CurrentLanguageInt)
            StartNewInstanceButton.Text = AppTranslator.UnknownClientVersion(CurrentLanguageInt)
            DisplayLauncherVersionOnFooter()
        End Try
        Return False
    End Function

    Public Async Function CleanDeprecatedClients() As Task
        'AGREGAR OPCION PARA HABILITAR/DESHABILITAR LA LIMPIEZA AUTOMATICA DE CLIENTES OBSOLETOS?
        Try
            If UpdateSource = "AIR_Official" Then
                StartNewInstanceButton.Text = "Cleaning deprecated clients"
                Dim JsonRoot As JsonElement = JsonDocument.Parse(Await GetRemoteJsonAsync("https://images.habbo.com/habbo-native-clients/launcher/clientversions.json")).RootElement
                Dim ValidClientVersions As String() = JsonRoot.GetProperty("win").GetProperty("air").EnumerateArray().Select(Function(x) x.GetString()).ToArray
                For Each InstalledClientVersion In Directory.GetDirectories(GetPossibleClientPath("")).Select(Function(x) Path.GetFileName(x))
                    If IsNumeric(InstalledClientVersion) AndAlso ValidClientVersions.Contains(InstalledClientVersion) = False Then
                        Await Task.Run(Sub() Directory.Delete(GetPossibleClientPath(InstalledClientVersion), True))
                    End If
                Next
            End If
        Catch
            'We ignore the error
        End Try
    End Function

    Public Async Function GetRemoteLastModifiedHeaderEpoch(url As String) As Task(Of String)
        HttpClient.DefaultRequestHeaders.Clear()
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36")
        Dim request As New HttpRequestMessage(HttpMethod.Head, url)
        Dim response As HttpResponseMessage = Await HttpClient.SendAsync(request)
        If response.Headers.Contains("x-ms-creation-time") Then
            Dim lastmodified = response.Headers.GetValues("x-ms-creation-time").FirstOrDefault()
            Dim dateTimeUtc As DateTime = DateTime.ParseExact(lastmodified, "ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            Dim epochTime As Long = CType((dateTimeUtc - New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, Long)
            Return epochTime.ToString()
        Else
            Throw New Exception("Last modified header not found")
        End If
    End Function

    Public Function IsClientVersionExists(Optional ClientVersion As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(ClientVersion) Then
                ClientVersion = CurrentClientUrls.FlashWindowsVersion
            End If
            Dim ClientPath = GetPossibleClientPath(ClientVersion)
            Return Directory.Exists(ClientPath) AndAlso (File.Exists(Path.Combine(ClientPath, "VERSION.txt")) OrElse InstallationExists(ClientVersion, "air"))
        Catch
            Return False
        End Try
    End Function

    Public Async Function UpdateClientButtonStatus() As Task
        StartNewInstanceButton.IsButtonDisabled = True
        StartNewInstanceButton2.IsButtonDisabled = True
        ChangeUpdateSourceButton.IsButtonDisabled = True
        ChangeUpdateSourceButton2.IsButtonDisabled = True
        Try
            Dim IsClientUpdated As Boolean = False
            StartNewInstanceButton.Text = AppTranslator.ClientUpdatesCheck(CurrentLanguageInt)
            If UpdateSource = "AIR_Official" Then
                CurrentClientUrls = New JsonClientUrls(Await GetRemoteJsonAsync("https://" & CurrentLoginCode.ServerUrl & "/gamedata/clienturls"))
            End If
            If UpdateSource = "AIR_Plus" Then
                Dim AirPlusClientLatestVersion = Await GetRemoteLastModifiedHeaderEpoch(AirPlusClientURL)
                CurrentClientUrls = New JsonClientUrls(("{'flash-windows-version':'" & AirPlusClientLatestVersion & "','flash-windows':'" & AirPlusClientURL & "'}").Replace("'", Chr(34)))
            End If

            IsClientUpdated = IsClientVersionExists()
            'Await CleanDeprecatedClients() 'No se si lo ideal seria ponerlo aca o solo en UpdateClient, lo malo seria que de esa forma si un cliente se actualiza a un server actualiza a una version de cliente ya existe entonces no se eliminaria la version anterior a menos que se vuelva a actualizar.

            If IsClientUpdated Then 'Abria que verificar swf o mejor aun que exista un archivo READY para asegurarse que se completo todo el proceso de modificacion
                StartNewInstanceButton.Text = AppTranslator.LaunchClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
            Else
                StartNewInstanceButton.Text = AppTranslator.UpdateClientVersion(CurrentLanguageInt) & " " & CurrentClientUrls.FlashWindowsVersion
            End If

        Catch ex As Exception
            'StartNewInstanceButton.BackColor = Media.Color.FromRgb(200, 0, 0)
            StartNewInstanceButton.Text = AppTranslator.RetryClientUpdatesCheck(CurrentLanguageInt)
            MsgBox(AppTranslator.ErrorDebugClipboardHint(CurrentLanguageInt), AppTranslator.ClientUpdatesCheckError(CurrentLanguageInt), ex.Message)
        End Try
        StartNewInstanceButton.IsButtonDisabled = False
        StartNewInstanceButton2.IsButtonDisabled = False
        ChangeUpdateSourceButton.IsButtonDisabled = False
        ChangeUpdateSourceButton2.IsButtonDisabled = False
    End Function

    Public Function GetAppDataPath() As String
        Dim AppDataFolderPath As String = "" 'Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        If String.IsNullOrWhiteSpace(AppDataFolderPath) Then
            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                AppDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            ElseIf RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                AppDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming")
            Else
                AppDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            End If
        End If
        Directory.CreateDirectory(AppDataFolderPath)
        Return AppDataFolderPath
    End Function

    Public Function GetPossibleClientPath(ClientVersion As String) As String
        Dim ClientType = "air"
        If UpdateSource = "AIR_Plus" Then
            ClientType = "airplus"
        End If
        Return Path.Combine(GetAppDataPath, "Habbo Launcher", "downloads", ClientType, ClientVersion)
    End Function

    Public Sub SaveCurrentUpdateSource()
        Dim DestinationFolder = Path.Combine(GetAppDataPath, "Habbo Launcher", "downloads")
        Directory.CreateDirectory(DestinationFolder)
        IO.File.WriteAllText(Path.Combine(DestinationFolder, "UpdateSource.txt"), UpdateSource)
    End Sub

    Public Sub LoadSavedUpdateSource()
        Dim DestinationFile = Path.Combine(GetAppDataPath, "Habbo Launcher", "downloads", "UpdateSource.txt")
        If File.Exists(DestinationFile) Then
            Dim SavedSource = File.ReadAllText(DestinationFile)
            Dim AllowedSources As String() = {"AIR_Plus", "AIR_Official"}
            If AllowedSources.Contains(SavedSource) Then
                UpdateSource = SavedSource
                RefreshUpdateSourceText()
            End If
        End If
    End Sub

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
            CurrentLoginCode = Nothing
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
        Dim CurrentUpdateSourceLabel = AppTranslator.CurrentUpdateSource(CurrentLanguageInt)
        Select Case UpdateSource
            Case "AIR_Official"
                ChangeUpdateSourceButton.Text = CurrentUpdateSourceLabel & ": AIR Classic"
            Case "AIR_Plus"
                ChangeUpdateSourceButton.Text = CurrentUpdateSourceLabel & ": AIR Plus"
            Case Else
                ChangeUpdateSourceButton.Text = CurrentUpdateSourceLabel & ": Unknown"
        End Select
    End Sub

    Public Function GetCurrentUpdateSourceName()
        Select Case UpdateSource
            Case "AIR_Official"
                Return "AIR Classic"
            Case "AIR_Plus"
                Return "AIR Plus"
            Case Else
                Return "Unknown"
        End Select
    End Function

    Private Sub ChangeUpdateSourceButton_Click(sender As Object, e As EventArgs) Handles ChangeUpdateSourceButton.Click
        Select Case UpdateSource
            Case "AIR_Official"
                UpdateSource = "AIR_Plus"
            Case Else
                UpdateSource = "AIR_Official"
        End Select
        SaveCurrentUpdateSource()
        RefreshUpdateSourceText()
        UpdateClientButtonStatus()
    End Sub

    Private Sub StartNewInstanceButton2_Click(sender As Object, e As EventArgs) Handles StartNewInstanceButton2.Click
        'Temporalmente elimina la instalacion actual, en un futuro deberia abrirse una ventana con varias opciones
        '(Por ejemplo usar una version especifica ya descargada del cliente, borrar instalacion existente, borrar todas instalaciones, etc.)
        Try
            Directory.Delete(GetPossibleClientPath(CurrentClientUrls.FlashWindowsVersion), True)
            RemoveInstallation(CurrentClientUrls.FlashWindowsVersion, "air")
            StartNewInstanceButton.IsButtonDisabled = True
            StartNewInstanceButton2.IsButtonDisabled = True
            ChangeUpdateSourceButton.IsButtonDisabled = True
            ChangeUpdateSourceButton2.IsButtonDisabled = True
            FocusManager.ClearFocus()
            UpdateClientButtonStatus()
        Catch ex As Exception
            MsgBox(AppTranslator.ErrorDebugClipboardHint(CurrentLanguageInt), AppTranslator.ClientDeleteError(CurrentLanguageInt), ex.Message)
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
            Process.Start(New ProcessStartInfo("https://github.com/LilithRainbows/HabboCustomLauncher") With {.UseShellExecute = True})
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
        If HabboLogoButton.ContextMenu IsNot Nothing AndAlso HabboLogoButton.ContextMenu.IsOpen Then
            Return
        End If
        HabboLogoButton.Source = New Bitmap(AssetLoader.Open(New Uri("avares://" & Assembly.GetExecutingAssembly().GetName().Name & "/Assets/habbo-logo-big-2.png")))
    End Sub

    Private Sub HabboLogoButton_PointerExited(sender As Object, e As Avalonia.Input.PointerEventArgs) Handles HabboLogoButton.PointerExited
        If HabboLogoButton.ContextMenu IsNot Nothing AndAlso HabboLogoButton.ContextMenu.IsOpen Then
            Return
        End If
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
        Process.GetCurrentProcess.Kill()
    End Sub

    Private Sub HabboLogoButton_ContextMenuClosed(sender As Object, e As EventArgs)
        HabboLogoButton_PointerExited(Nothing, Nothing)
    End Sub

    Private Sub HabboLogoButton_PointerPressed(sender As Object, e As Avalonia.Input.PointerPressedEventArgs) Handles HabboLogoButton.PointerPressed
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
            HabboLogoButton.ContextMenu = Nothing
        End If
        HabboLogoButton.ContextMenu = contextMenu
        AddHandler HabboLogoButton.ContextMenu.Closed, AddressOf HabboLogoButton_ContextMenuClosed
        HabboLogoButton.ContextMenu.Open()
    End Sub

    Private Sub AddDesktopShortcut()
        CreateShortcut(Environment.ProcessPath, "HabboCustomLauncher", True)
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

            Dim OSXDownloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            CopyEmbeddedAsset(LauncherShortcutOSXPatchName, OSXDownloadFolder)

            ZipFile.ExtractToDirectory(Path.Combine(OSXDownloadFolder, LauncherShortcutOSXPatchName), OSXDownloadFolder, True)
            File.Delete(Path.Combine(OSXDownloadFolder, LauncherShortcutOSXPatchName))

            Dim scriptPath As String = Path.Combine(OSXDownloadFolder, "HabboCustomLauncherShortcut.sh")
            Dim originalScriptContent = File.ReadAllText(scriptPath, New Text.UTF8Encoding(False))
            originalScriptContent = originalScriptContent.Replace("%HabboCustomLauncherAppPath%", appPath)
            If isDesktop Then
                originalScriptContent = originalScriptContent.Replace("/Applications/", "$HOME/Desktop/")
            End If
            File.WriteAllText(scriptPath, originalScriptContent, New Text.UTF8Encoding(False))

            MakeUnixExecutable(scriptPath)
            Dim process As New Process()
            process.StartInfo.FileName = "/bin/bash"
            process.StartInfo.Arguments = "-c """"" & scriptPath & """"""
            process.StartInfo.UseShellExecute = False
            process.StartInfo.CreateNoWindow = True
            process.Start()
            process.WaitForExit()
            File.Delete(Path.Combine(OSXDownloadFolder, "HabboCustomLauncherShortcut.sh"))

        Else 'Linux
            Dim ShortcutPath As String = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            If String.IsNullOrWhiteSpace(ShortcutPath) Then
                ShortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop")
            End If
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
            CopyEmbeddedAsset("HabboCustomLauncherIcon.png", IconsPath)
            File.WriteAllText(Path.Combine(ShortcutPath, appName & ".desktop"), shortcutContent)
            MakeUnixExecutable(Path.Combine(ShortcutPath, appName & ".desktop"))
        End If
    End Sub

    Private Sub ChangeUpdateSourceButton2_Click(sender As Object, e As EventArgs) Handles ChangeUpdateSourceButton2.Click
        Dim contextMenu As New ContextMenu
        Dim ClientHint As String = AppTranslator.ClassicAirClientHint(CurrentLanguageInt)
        If UpdateSource = "AIR_Plus" Then
            ClientHint = AppTranslator.AirPlusClientHint(CurrentLanguageInt)
        End If
        contextMenu.Items.Add(New MenuItem With {.Header = ClientHint})
        If ChangeUpdateSourceButton2.ContextMenu IsNot Nothing Then
            ChangeUpdateSourceButton2.ContextMenu.Close()
        End If
        ChangeUpdateSourceButton2.ContextMenu = contextMenu
        ChangeUpdateSourceButton2.ContextMenu.Open()
    End Sub

    Private Sub TitleBarLabel_PointerPressed(sender As Object, e As Input.PointerPressedEventArgs) Handles TitleBarLabel.PointerPressed
        ' Solo con botón izquierdo
        If e.GetCurrentPoint(TitleBarLabel).Properties.IsLeftButtonPressed Then
            ' Avalonia se encarga de DPI y límites automáticamente
            Me.BeginMoveDrag(e)
        End If
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Window.Close()
    End Sub

    Private Async Function EnsureWindowFocus(RequestedWindow As Window) As Task(Of Boolean)
        Try
            RequestedWindow.Show() 'Quizas convendria hacer un ShowDialog(Window) especificamente para el LoadingWindow pero luego queda abierto en el background, reformar codigo! Quizas usando luego Window.Owner desde el LoadingWindow
            If RequestedWindow.IsActive = False Then
                RequestedWindow.WindowState = WindowState.Minimized
                Await Task.Delay(100)
                RequestedWindow.WindowState = WindowState.Normal
                RequestedWindow.Activate()
            End If
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub StartNewInstanceButton_PropertyChanged(sender As Object, e As AvaloniaPropertyChangedEventArgs) Handles StartNewInstanceButton.PropertyChanged
        If e.Property.Name = "Text" Then
            If LoadingWindowChild IsNot Nothing Then
                If StartNewInstanceButton.Text.StartsWith(AppTranslator.RetryClientUpdatesCheck(CurrentLanguageInt)) Then 'Update fail
                    LoadingWindowChild.MainMenuRequested = True
                    LoadingWindowChild.Close()
                    Return
                End If
                If StartNewInstanceButton.Text.StartsWith(AppTranslator.LaunchClientVersion(CurrentLanguageInt)) Then 'Launch client
                    LoadingWindowClientLaunchRequested = True
                    'StartNewInstanceButton_Click(Nothing, Nothing)
                    LaunchClientFromLoadingWindowWithDelay(3)
                    Return
                End If
                If StartNewInstanceButton.Text.StartsWith(AppTranslator.UnknownClientVersion(CurrentLanguageInt)) AndAlso LoadingWindowClientLaunchRequested = True Then 'Client launched
                    Process.GetCurrentProcess.Kill()
                    Return
                End If
                If StartNewInstanceButton.Text.StartsWith(AppTranslator.UpdateClientVersion(CurrentLanguageInt)) Then 'Update client
                    StartNewInstanceButton_Click(Nothing, Nothing)
                    Return
                End If
                If StartNewInstanceButton.Text.StartsWith(AppTranslator.ClientUpdatesCheck(CurrentLanguageInt)) Or StartNewInstanceButton.Text.StartsWith(AppTranslator.DownloadingClient(CurrentLanguageInt)) Or StartNewInstanceButton.Text.StartsWith(AppTranslator.ExtractingClient(CurrentLanguageInt)) Then 'Client update check or downloading or extracting
                    LoadingWindowChild.StatusLabel.Text = e.NewValue
                Else
                    LoadingWindowChild.StatusLabel.Text = AppTranslator.GenericLoading(CurrentLanguageInt) & " ..." 'Generic loading
                End If
            End If
        End If
    End Sub

    Private Async Sub LaunchClientFromLoadingWindowWithDelay(DelaySeconds As Integer)
        Do Until DelaySeconds = 0 Or LoadingWindowChild Is Nothing
            LoadingWindowChild.StatusLabel.Text = AppTranslator.GenericLoading(CurrentLanguageInt) & " " & GetCurrentUpdateSourceName() & " (" & DelaySeconds & "s)" 'Generic loading
            Await Task.Delay(1000)
            DelaySeconds -= 1
        Loop
        If LoadingWindowChild IsNot Nothing Then
            LoadingWindowChild.StatusLabel.Text = AppTranslator.GenericLoading(CurrentLanguageInt) & " " & GetCurrentUpdateSourceName()
            StartNewInstanceButton_Click(Nothing, Nothing)
        End If
    End Sub

    Private Sub LoadingWindowChild_Closed(sender As Object, e As EventArgs) Handles LoadingWindowChild.Closed
        'If LoadingWindowCloseRequested = True Then
        '    LoadingWindowChild = Nothing
        '    EnsureWindowFocus(Me)
        'Else
        '    Window.Close()
        'End If
    End Sub

    Private Sub MainWindow_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        If LoadingWindowChild IsNot Nothing Then
            EnsureWindowFocus(LoadingWindowChild)
        End If
    End Sub

    Public Sub AddOrUpdateInstallation(version As String, path As String, client As String, lastModified As Long)
        Dim jsonPath = IO.Path.Combine(GetAppDataPath, "Habbo Launcher", "versions.json")
        Dim root As JsonObject

        Try
            If File.Exists(jsonPath) Then
                Dim txt = File.ReadAllText(jsonPath)
                If Not String.IsNullOrWhiteSpace(txt) Then
                    root = JsonNode.Parse(txt).AsObject()
                Else
                    root = New JsonObject()
                End If
            Else
                root = New JsonObject()
            End If
        Catch
            root = New JsonObject()
        End Try

        If root("installations") Is Nothing Then
            root("installations") = New JsonArray()
        End If

        Dim installations As JsonArray = root("installations")

        Dim existing As JsonObject = Nothing

        For Each item As JsonObject In installations
            If item("version")?.ToString() = version AndAlso item("client")?.ToString() = client Then
                existing = item
                Exit For
            End If
        Next

        If existing IsNot Nothing Then
            existing("path") = path
            existing("lastModified") = lastModified
        Else
            installations.Add(New JsonObject From {
                {"version", version},
                {"path", path},
                {"client", client},
                {"lastModified", lastModified}
            })
        End If

        Dim dir = IO.Path.GetDirectoryName(jsonPath)
        If Not String.IsNullOrWhiteSpace(dir) AndAlso Not Directory.Exists(dir) Then
            Directory.CreateDirectory(dir)
        End If

        File.WriteAllText(jsonPath, root.ToJsonString(New JsonSerializerOptions With {.WriteIndented = True}))

    End Sub

    Public Sub RemoveInstallation(version As String, client As String)
        Dim jsonPath = IO.Path.Combine(GetAppDataPath, "Habbo Launcher", "versions.json")
        If Not File.Exists(jsonPath) Then Exit Sub

        Dim root As JsonObject

        Try
            Dim txt = File.ReadAllText(jsonPath)
            If String.IsNullOrWhiteSpace(txt) Then Exit Sub
            root = JsonNode.Parse(txt).AsObject()
        Catch
            Exit Sub
        End Try

        Dim installations As JsonArray = root("installations")
        If installations Is Nothing Then Exit Sub

        For i As Integer = installations.Count - 1 To 0 Step -1
            Dim item As JsonObject = installations(i)
            If item("version")?.ToString() = version AndAlso item("client")?.ToString() = client Then
                installations.RemoveAt(i)
            End If
        Next

        File.WriteAllText(jsonPath, root.ToJsonString(New JsonSerializerOptions With {.WriteIndented = True}))

    End Sub

    Public Function InstallationExists(version As String, client As String) As Boolean
        Dim jsonPath = IO.Path.Combine(GetAppDataPath, "Habbo Launcher", "versions.json")
        If Not File.Exists(jsonPath) Then Return False

        Try
            Dim txt = File.ReadAllText(jsonPath)
            If String.IsNullOrWhiteSpace(txt) Then Return False

            Dim root = JsonNode.Parse(txt).AsObject()
            Dim installations As JsonArray = root("installations")

            If installations Is Nothing Then Return False

            For Each item As JsonObject In installations
                If item("version")?.ToString() = version AndAlso item("client")?.ToString() = client Then
                    Return True
                End If
            Next
        Catch
        End Try

        Return False

    End Function

End Class

Public Class JsonClientUrls
    Public ReadOnly FlashWindowsVersion As String
    Public ReadOnly FlashWindowsUrl As String

    Public Sub New(JsonString As String)
        Dim JsonRoot As JsonElement = JsonDocument.Parse(JsonString).RootElement
        FlashWindowsVersion = JsonRoot.GetProperty("flash-windows-version").GetString()
        FlashWindowsUrl = JsonRoot.GetProperty("flash-windows").GetString()
    End Sub
End Class

Public Class LoginCode
    Public ReadOnly SSOTicket As String = ""
    Public ReadOnly ServerId As String = ""
    Public ReadOnly ServerUrl As String = ""
    Public ReadOnly Username As String = ""

    Public Sub New(LoginCode As String)
        If LoginCode.StartsWith("habbo://") And LoginCode.Contains("server=") Then 'Example: habbo://hab?server=hhes&token=11111111-1111-1111-1111-111111111111-11111111.V4.LilithRainbows
            LoginCode = LoginCode.Remove(0, LoginCode.IndexOf("?server=") + 8)
            LoginCode = LoginCode.Replace("&token=", ".")
        End If
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
    Public Shared GenericLoading As String() = {
        "Loading",
        "Cargando"
    }
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
    Public Shared CurrentUpdateSource As String() = {
        "Current update source",
        "Fuente de actualizaciones"
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
        "Añadir acceso directo al escritorio"
    }
    Public Shared AddStartMenuShortcut As String() = {
        "Add shortcut to start menu",
        "Añadir acceso directo al menu de inicio"
    }
    Public Shared AutomaticHabboProtocol As String() = {
        "Automatic habbo protocol",
        "Habbo protocol automatico"
    }
    Public Shared ClientLaunchError As String() = {
        "Client could not be launched!",
        "No se pudo ejecutar el cliente!"
    }
    Public Shared ClientUpdateError As String() = {
        "Client could not be updated!",
        "No se pudo actualizar el cliente!"
    }
    Public Shared ClientDeleteError As String() = {
        "Client version could not be deleted!",
        "No se pudo eliminar la version del cliente!"
    }
    Public Shared ClientUpdatesCheckError As String() = {
        "Client updates could not be checked!",
        "No se pudo comprobar las actualizaciones del cliente!"
    }
    Public Shared ErrorDebugClipboardHint As String() = {
        "Error (CTRL + C to copy technical details)",
        "Error (CTRL + C para copiar detalles tecnicos)"
    }
    Public Shared ClassicAirClientHint As String() = {
        "The official classic Habbo client without modifications.",
        "El cliente clasico oficial de Habbo sin modificaciones."
    }
    Public Shared AirPlusClientHint As String() = {
        "The classic Habbo client with unofficial modifications.",
        "El cliente clasico de Habbo con modificaciones no oficiales."
    }
End Class