Imports System.Runtime.InteropServices
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Markup.Xaml
Imports Path = System.IO.Path

Partial Public Class LoadingWindow : Inherits Window
    Public MainMenuRequested As Boolean
    Private WithEvents Window As Window
    Private WithEvents TitleBarLabel As Label
    Public WithEvents StatusLabel As TextBlock
    Private WithEvents CloseButton As CustomButton
    Private WithEvents MainMenuButton As CustomButton
    Public CurrentLanguageInt As Integer = 0
    Public PreviousLauncherFilename As String = ""

    Sub New()
        InitializeComponent() ' This call is required by the designer
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
        StatusLabel = FindNameScope().Find("StatusLabel")
        CloseButton = FindNameScope().Find("CloseButton")
        MainMenuButton = FindNameScope().Find("MainMenuButton")

        Singleton.GetCurrentInstance().ScaleMainGrid(Window)

        MainMenuRequested = False

        MainMenuButton.Text = LauncherUpdaterTranslator.ReturnToMainMenu(CurrentLanguageInt)

        Return

        For ArgumentIndex = 0 To Environment.GetCommandLineArgs.Length - 1
            If Environment.GetCommandLineArgs(ArgumentIndex).ToLower.StartsWith("-updater") Then
                If ArgumentIndex + 1 <= Environment.GetCommandLineArgs.Length - 1 Then
                    PreviousLauncherFilename = Environment.GetCommandLineArgs(ArgumentIndex + 1)
                End If
            End If
        Next

        StartUpdateProcess()
    End Sub

    Private Async Sub StartUpdateProcess()
        Try
            If String.IsNullOrWhiteSpace(PreviousLauncherFilename) Then
                Throw New Exception("Unknown launcher filename!")
            End If
            Dim ApplicationLocation As String = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
            Dim LauncherUpdatePath As String = Process.GetCurrentProcess().MainModule.FileName 'HCL_Update (full path)
            Dim PreviousLauncherPath As String = Path.Combine(ApplicationLocation, PreviousLauncherFilename) 'HabboCustomLauncher (full path)
            StatusLabel.Text = LauncherUpdaterTranslator.VerifyingUpdate(CurrentLanguageInt)
            'Do While File.Exists(PreviousLauncherPath)
            '    StatusLabel.Text = LauncherUpdaterTranslator.DeletingPreviousVersion(CurrentLanguageInt)
            '    Dim FileDeleteOK As Boolean
            '    Try
            '        ExitBlocked = True
            '        File.Delete(PreviousLauncherPath)
            '        FileDeleteOK = True
            '    Catch
            '        FileDeleteOK = False
            '    End Try
            '    If FileDeleteOK = False Then
            '        ExitBlocked = False
            '        Await Task.Delay(500)
            '    End If
            'Loop
            'StatusLabel.Text = LauncherUpdaterTranslator.ApplyingUpdate(CurrentLanguageInt)
            'ExitBlocked = True
            'File.Copy(LauncherUpdatePath, PreviousLauncherPath, True)
            'StatusLabel.Text = LauncherUpdaterTranslator.UpdateReady(CurrentLanguageInt)
            'Await Task.Delay(2000)


            ReemplazarCuandoSeLibere(LauncherUpdatePath, PreviousLauncherPath)
            Await Task.Delay(5000)
            Environment.Exit(0)



            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) = False Then
                MakeUnixExecutable(PreviousLauncherPath)
            End If
            Process.Start(PreviousLauncherPath)
            Environment.Exit(0)
        Catch
            StatusLabel.Text = LauncherUpdaterTranslator.UpdateError(CurrentLanguageInt)
        End Try
    End Sub

    Function MakeUnixExecutable(ByVal filePath As String) As Boolean
        Dim process As New Process()
        process.StartInfo.FileName = "chmod"
        process.StartInfo.Arguments = $"+x ""{filePath}"""
        process.StartInfo.UseShellExecute = False
        process.StartInfo.CreateNoWindow = True
        process.Start()
        process.WaitForExit()
    End Function

    Async Sub ReemplazarCuandoSeLibere(archivoNuevo As String, archivoViejo As String)

        Dim psi As New ProcessStartInfo()
        psi.CreateNoWindow = True
        psi.WindowStyle = ProcessWindowStyle.Hidden
        psi.UseShellExecute = False

        If OperatingSystem.IsWindows() Then

            psi.FileName = "cmd.exe"




            '            Dim script As String =
            '"for /L %i in (1,0,2) do ( " &
            '"  timeout /t 1 /nobreak >nul & " &
            '$"  move /y ""{archivoNuevo}"" ""{archivoViejo}"" >nul 2>&1 & " &
            '$"  if not errorlevel 1 ( start """" ""{archivoViejo}"" & exit /b 0 ) " &
            '")"



            '            Dim script As String =
            '"for /L %i in (1,1,60) do ( " &
            '"  timeout /t 1 /nobreak >nul & " &
            '$"  move /y ""{archivoNuevo}"" ""{archivoViejo}"" >nul 2>&1 & " &
            '$"  if not errorlevel 1 ( start """" ""{archivoViejo}"" & exit /b 0 ) " &
            '")"


            Dim script As String =
$"for /L %i in (1,1,60) do ( " &
$"  timeout /t 1 /nobreak >nul & " &
$"  move /y ""{archivoNuevo}"" ""{archivoViejo}"" >nul 2>&1 & " &
$"  if not errorlevel 1 ( " &
$"    start """" ""{archivoViejo}"" & " &
$"    exit /b 0 " &
$"  ) " &
$")"



            psi.Arguments = "/c " & script








        Else
            ' Linux / macOS
            psi.FileName = "sh"

            'psi.Arguments =
            ' $"-c ""while ! mv -f '{archivoNuevo}' '{archivoViejo}' > /dev/null 2>&1; do sleep 1; done"""
            Dim pid = Process.GetCurrentProcess().Id
            '        psi.Arguments =
            '$"-c """ &
            '$"while ! mv -f '{archivoNuevo}' '{archivoViejo}' >/dev/null 2>&1; do sleep 1; done; " &
            '$"kill -9 {pid} >/dev/null 2>&1; " &
            '$"while kill -0 {pid} 2>/dev/null; do sleep 1; done; " &
            '$"exec '{archivoViejo}'" &
            '""""

            psi.Arguments =
            $"-c """ &
            $"i=0; ok=0; " &
            $"while [ $i -lt 60 ]; do " &
            $"  if mv -f '{archivoNuevo}' '{archivoViejo}' >/dev/null 2>&1; then ok=1; break; fi; " &
            $"  i=$((i+1)); " &
            $"  sleep 1; " &
            $"done; " &
            $"[ $ok -ne 1 ] && exit 1; " & ' <-- Esto hace que salga si no se movió
            $"kill -9 {pid} >/dev/null 2>&1; " &
            $"while kill -0 {pid} 2>/dev/null; do sleep 1; done; " &
            $"exec '{archivoViejo}'" &
            """"





        End If

        Process.Start(psi)

        ' Cerrar el proceso actual
        'Environment.Exit(0)

    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Window.Close()
    End Sub

    Private Function GetMainWindow() As MainWindow
        Return Singleton.GetCurrentInstance.MainWindow
    End Function

    Private Sub MainMenuButton_Click(sender As Object, e As EventArgs) Handles MainMenuButton.Click
        MainMenuRequested = True
        Window.Close()
    End Sub

    Private Sub TitleBarLabel_PointerPressed(sender As Object, e As PointerPressedEventArgs) Handles TitleBarLabel.PointerPressed
        ' Solo con botón izquierdo
        If e.GetCurrentPoint(TitleBarLabel).Properties.IsLeftButtonPressed Then
            ' Avalonia se encarga de DPI y límites automáticamente
            Me.BeginMoveDrag(e)
        End If
    End Sub

    Private Sub LoadingWindow_Closing(sender As Object, e As WindowClosingEventArgs) Handles Me.Closing
        If MainMenuRequested Then
            GetMainWindow.Show()
            GetMainWindow.LoadingWindowChild = Nothing
        Else
            Process.GetCurrentProcess.Kill()
        End If
    End Sub
End Class

Public Class LauncherUpdaterTranslator
    '0=English 1=Spanish
    Public Shared ReturnToMainMenu As String() = {
        "Return to main menu",
        "Volver al menu principal"
    }
    Public Shared VerifyingUpdate As String() = {
        "Verifying update ...",
        "Verificando actualizacion ..."
    }
    Public Shared DeletingPreviousVersion As String() = {
        "Deleting previous version ...",
        "Borrando version anterior ..."
    }
    Public Shared ApplyingUpdate As String() = {
        "Applying update ...",
        "Aplicando actualizacion ..."
    }
    Public Shared UpdateReady As String() = {
        "Update ready!",
        "Actualizacion lista!"
    }
    Public Shared UpdateError As String() = {
        "Update error!",
        "Error de actualizacion!"
    }
End Class