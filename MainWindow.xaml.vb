Imports System.ComponentModel
Imports System.IO
Imports System.Security.Principal
Imports System.Windows.Threading
Imports System.Xml
Imports Microsoft.Win32

Class MainWindow
    Public RequestedURI As String = ""
    Public RequestedServer As String = ""
    Public RequestedTicket As String = ""
    Public CurrentLanguageInt As Integer = 0
    Public WithEvents ClientMaximizerTimer As New DispatcherTimer
    Public WithEvents ClientMaximizerBW As New ExtendedBackgroundWorker
    Public ClientProcesses As New List(Of Process)
    Public LoadingWindow As LoadingWindow
    Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If System.Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("es") Then
            CurrentLanguageInt = 1
        End If
        If System.Globalization.CultureInfo.CurrentCulture.Name.ToLower.StartsWith("pt") Then
            CurrentLanguageInt = 2
        End If
        Directory.SetCurrentDirectory(GetExecutableDirectory)
        StartNewInstanceButton.Content = AppTranslator.NewInstance(CurrentLanguageInt)
        UpdateProtocolButton()
        FixWindowsTLS()
        If RequestedURI = "" = False Then
            LoadingWindow = New LoadingWindow(AppTranslator.LoadingValue(CurrentLanguageInt))
            LoadingWindow.Show()
            StartNewInstanceButton_Click(Nothing, Nothing)
        End If
    End Sub

    Function GetExecutableDirectory() As String
        Return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
    End Function

    Function GetClientVersion() As String
        Try
            Dim ClientXMLPath As String = GetClientPath() & "\META-INF\AIR\application.xml"
            Dim OriginalClientXML = New XmlDocument()
            OriginalClientXML.Load(ClientXMLPath)
            Return OriginalClientXML("application")("versionLabel").InnerText
        Catch
            Return "null"
        End Try
    End Function

    Function GetClientPath() As String
        Try
            Dim AppDataPath As String = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) & "\Habbo Launcher\downloads\air"
            AppDataPath += "\" & Directory.GetDirectories(AppDataPath).Max(Function(d) New DirectoryInfo(d).Name)
            If Directory.Exists("META-INF\AIR") Then
                Return Directory.GetCurrentDirectory
            End If
            If Directory.Exists(AppDataPath & "\META-INF\AIR") Then
                Return AppDataPath
            End If
            Throw New Exception("Client not found")
        Catch
            MsgBox(AppTranslator.ClientNotFound(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
            Environment.Exit(0)
        End Try
    End Function

    Private Sub StartNewInstanceButton_Click(sender As Object, e As RoutedEventArgs) Handles StartNewInstanceButton.Click
        Try
            Dim ClientProcess As New Process
            ClientProcess.StartInfo.FileName = GetClientPath() & "\Habbo.exe"
            ClientProcess.StartInfo.WorkingDirectory = GetClientPath()
            If RequestedTicket = "" = False Then
                ClientProcess.StartInfo.Arguments = "-server " & RequestedServer & " -ticket " & RequestedTicket
            End If
            ClientProcess.Start()
            ClientProcesses.Add(ClientProcess)
            ClientMaximizerTimer.Interval = TimeSpan.FromMilliseconds(500)
            ClientMaximizerTimer.Start()
        Catch ex As Exception
            LoadingWindow.Close()
            MsgBox(AppTranslator.ClientOpenError(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
            If RequestedURI = "" = False Then
                Environment.Exit(0)
            End If
        End Try
    End Sub

    Public Function RegisterHabboProtocol() As Boolean
        Try
            Dim UriScheme = "habbo"
            Dim FriendlyName = "Habbo Custom Launcher"
            Dim applicationLocation As String = System.Reflection.Assembly.GetExecutingAssembly().Location
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
        Catch
            MsgBox(AppTranslator.ProtocolRegError(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
            Return False
        End Try
    End Function

    Public Function UnregisterHabboProtocol() As Boolean
        Try
            Dim UriScheme = "habbo"
            Registry.CurrentUser.DeleteSubKeyTree("SOFTWARE\Classes\" & UriScheme)
            Return True
        Catch
            MsgBox(AppTranslator.ProtocolUnregError(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
            Return False
        End Try
    End Function

    Public Sub FixWindowsTLS()
        Try
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
                If UserIsAdmin() Then
                    Using key = Registry.LocalMachine.CreateSubKey("SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client")
                        key.SetValue("DisabledByDefault", 0)
                        key.SetValue("Enabled", 1)
                    End Using
                Else
                    MsgBox(AppTranslator.TLSFixAdminRightsError(CurrentLanguageInt), MsgBoxStyle.Critical, "Error")
                    Environment.Exit(0)
                End If
            End If
        Catch
            Console.WriteLine("Could not fix Windows TLS.")
        End Try
    End Sub

    Function UserIsAdmin() As Boolean
        Dim identity As WindowsIdentity = WindowsIdentity.GetCurrent()
        Dim principal As WindowsPrincipal = New WindowsPrincipal(identity)
        Return principal.IsInRole(WindowsBuiltInRole.Administrator)
    End Function

    Function UpdateProtocolButton()
        If CheckHabboProtocol() Then
            RegisterAppProtocolButton.Content = AppTranslator.UnregisterProtocol(CurrentLanguageInt)
        Else
            RegisterAppProtocolButton.Content = AppTranslator.RegisterProtocol(CurrentLanguageInt)
        End If
    End Function

    Public Function CheckHabboProtocol() As Boolean
        Try
            Dim UriScheme = "habbo"
            Dim applicationLocation As String = System.Reflection.Assembly.GetExecutingAssembly().Location
            Using key = Registry.CurrentUser.OpenSubKey("SOFTWARE\Classes\" & UriScheme)
                Using commandKey = key.OpenSubKey("shell\open\command")
                    If commandKey.GetValue("").ToString.Contains(applicationLocation) Then
                        Return True
                    Else
                        Return False
                    End If
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Sub RegisterAppProtocolButton_Click(sender As Object, e As RoutedEventArgs) Handles RegisterAppProtocolButton.Click
        If RegisterAppProtocolButton.Content = AppTranslator.RegisterProtocol(CurrentLanguageInt) Then
            RegisterHabboProtocol()
        Else
            UnregisterHabboProtocol()
        End If
        UpdateProtocolButton()
    End Sub

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As System.IntPtr, nCmdShow As Integer) As Integer
    End Function
    Enum ShowWindowCommands As Integer
        SW_SHOWNORMAL = 1
        SW_SHOWMAXIMIZED = 3
        SW_RESTORE = 9
    End Enum
    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function IsZoomed(hWnd As IntPtr) As Boolean
    End Function

    Private Sub ClientMaximizerTimer_Tick(sender As Object, e As EventArgs) Handles ClientMaximizerTimer.Tick
        If ClientMaximizerBW.IsBusy = False Then
            ClientMaximizerBW.RunWorkerAsync()
        End If
    End Sub

    Private Sub ClientMaximizerBW_DoWork(sender As Object, e As DoWorkEventArgs) Handles ClientMaximizerBW.DoWork
        Try
            For Each ClientProcess In ClientProcesses
                If ClientProcess.HasExited Then
                    ClientProcesses.Remove(ClientProcess)
                    Exit For
                Else
                    If IsZoomed(ClientProcess.MainWindowHandle) Then
                        Try
                            ClientProcess.PriorityClass = ProcessPriorityClass.RealTime
                        Catch
                            Console.WriteLine("Client priority set error")
                        End Try
                        ClientProcesses.Remove(ClientProcess)
                        Exit For
                    Else
                        ShowWindow(ClientProcess.MainWindowHandle, ShowWindowCommands.SW_SHOWMAXIMIZED)
                    End If
                End If
            Next
            If ClientProcesses.Count = 0 Then
                ClientMaximizerTimer.Stop()
                If RequestedURI = "" = False Then
                    Environment.Exit(0)
                End If
            End If
        Catch
            Console.WriteLine("ClientsMaximizer error")
            If RequestedURI = "" = False Then
                Environment.Exit(0)
            End If
        End Try
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        ClientMaximizerBW.CancelImmediately()
    End Sub
End Class
Public Class AppTranslator
    '0=English 1=Spanish 2=Portuguese
    Public Shared ClientNotFound As String() = {
        "Habbo Client not found." & vbNewLine & "You can download it from the Habbo website.",
        "Habbo Client no encontrado." & vbNewLine & "Puedes descargarlo desde la web de Habbo.",
        "Habbo Client não encontrado." & vbNewLine & "Você pode baixá-lo do site do Habbo."
    }
    Public Shared TLSFixAdminRightsError As String() = {
        "You must run the program as an administrator to enable TLS 1.2 on your system.",
        "Debes ejecutar el programa como administrador para habilitar TLS 1.2 en tu sistema.",
        "Você deve executar o programa como administrador para ativar o TLS 1.2 em seu sistema."
    }
    Public Shared ClientOpenError As String() = {
        "Could not open Habbo Client.",
        "No se pudo abrir Habbo Client.",
        "Habbo Client não pôde ser aberto."
    }
    Public Shared ProtocolRegError As String() = {
        "Could not register protocol.",
        "No se pudo registrar el protocolo.",
        "O protocolo não pôde ser registrado."
    }
    Public Shared ProtocolUnregError As String() = {
        "Could not unregister protocol.",
        "No se pudo eliminar el protocolo.",
        "O protocolo não pôde ser removido."
    }
    Public Shared RegisterProtocol As String() = {
        "Register Habbo Protocol",
        "Registrar Habbo Protocol",
        "Registrar Habbo Protocol"
    }
    Public Shared UnregisterProtocol As String() = {
        "Unregister Habbo Protocol",
        "Eliminar Habbo Protocol",
        "Remover Habbo Protocol"
    }
    Public Shared NewInstance As String() = {
        "Start new instance",
        "Iniciar nueva instancia",
        "Iniciar nova instância"
    }
    Public Shared LoadingValue As String() = {
        "Loading Habbo",
        "Cargando Habbo",
        "Carregando Habbo"
    }
End Class