Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports Avalonia.Controls

Partial Public Class LoadingWindow
    Private Shared ReadOnly UpdateService As New Lazy(Of LauncherUpdates)(
        Function() New LauncherUpdates(LauncherUpdates.CreateClient()))
    Private WithEvents UpdateActionButton As CustomButton
    Private InstalledLauncherVersion As Integer
    Private UpdateCancellation As CancellationTokenSource
    Private PendingUpdate As LauncherUpdate
    Private DownloadedUpdatePath As String

    Friend Sub ConfigureLauncherUpdates(installedVersion As Integer)
        InstalledLauncherVersion = installedVersion
        UpdateActionButton = Me.FindControl(Of CustomButton)("UpdateActionButton")
        StatusLabel.FontSize = 22
        StatusLabel.Text = LauncherUpdateText.Checking(CurrentLanguageInt)
        MainMenuButton.Text = LauncherUpdateText.Close(CurrentLanguageInt)
    End Sub

    Private Async Function CheckLauncherUpdateAsync() As Task
        Using operation As New CancellationTokenSource()
            UpdateCancellation = operation
            Try
                PendingUpdate = Await UpdateService.Value.CheckAsync(InstalledLauncherVersion, operation.Token)
                If operation.IsCancellationRequested Then Return
                If PendingUpdate Is Nothing Then
                    StatusLabel.Text = String.Format(LauncherUpdateText.CurrentVersion(CurrentLanguageInt), InstalledLauncherVersion)
                    ShowUpdateAction(LauncherUpdateText.ViewReleases(CurrentLanguageInt))
                Else
                    StatusLabel.Text = String.Format(LauncherUpdateText.Available(CurrentLanguageInt),
                                                     InstalledLauncherVersion, PendingUpdate.Version)
                    ShowUpdateAction(LauncherUpdateText.Download(CurrentLanguageInt))
                End If
            Catch ex As OperationCanceledException When operation.IsCancellationRequested
            Catch ex As Exception
                Debug.WriteLine(ex)
                If operation.IsCancellationRequested Then Return
                StatusLabel.Text = LauncherUpdateText.CheckError(CurrentLanguageInt)
                ShowUpdateAction(LauncherUpdateText.ViewReleases(CurrentLanguageInt))
            Finally
                UpdateCancellation = Nothing
            End Try
        End Using
    End Function

    Private Async Sub UpdateActionButton_Click(sender As Object, e As EventArgs) Handles UpdateActionButton.Click
        If UpdateCancellation IsNot Nothing Then Return
        If DownloadedUpdatePath IsNot Nothing Then
            OpenUpdateLocation(Path.GetDirectoryName(DownloadedUpdatePath))
        ElseIf PendingUpdate Is Nothing Then
            OpenUpdateLocation(LauncherUpdates.ReleasesPage)
        Else
            Await DownloadLauncherUpdateAsync()
        End If
    End Sub

    Private Async Function DownloadLauncherUpdateAsync() As Task
        Using operation As New CancellationTokenSource()
            UpdateCancellation = operation
            UpdateActionButton.IsButtonDisabled = True
            StatusLabel.Text = LauncherUpdateText.Downloading(CurrentLanguageInt)
            Try
                Dim folder = Path.Combine(MainWindow.GetAppDataPath(), "Habbo Launcher", "launcher-updates")
                DownloadedUpdatePath = Await UpdateService.Value.DownloadAsync(PendingUpdate, folder, operation.Token)
                If operation.IsCancellationRequested Then Return
                StatusLabel.Text = LauncherUpdateText.Ready(CurrentLanguageInt)
                ShowUpdateAction(LauncherUpdateText.OpenFolder(CurrentLanguageInt))
            Catch ex As OperationCanceledException When operation.IsCancellationRequested
            Catch ex As Exception
                Debug.WriteLine(ex)
                If operation.IsCancellationRequested Then Return
                StatusLabel.Text = LauncherUpdateText.DownloadError(CurrentLanguageInt)
                ShowUpdateAction(LauncherUpdateText.Download(CurrentLanguageInt))
            Finally
                UpdateCancellation = Nothing
            End Try
        End Using
    End Function

    Private Sub ShowUpdateAction(label As String)
        UpdateActionButton.Text = label
        UpdateActionButton.IsButtonDisabled = False
        UpdateActionButton.IsVisible = True
    End Sub

    Private Sub OpenUpdateLocation(location As String)
        Try
            Using opened As Process = Process.Start(New ProcessStartInfo(location) With {.UseShellExecute = True})
            End Using
        Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
            Debug.WriteLine(ex)
            StatusLabel.Text = LauncherUpdateText.LocationError(CurrentLanguageInt)
        End Try
    End Sub
End Class

Friend Module LauncherUpdateText
    Friend ReadOnly Check As String() = {"Check launcher updates", "Buscar actualizaciones del launcher"}
    Friend ReadOnly Close As String() = {"Close", "Cerrar"}
    Friend ReadOnly Checking As String() = {"Checking for launcher updates…", "Buscando actualizaciones del launcher…"}
    Friend ReadOnly CurrentVersion As String() = {"Version {0} is up to date.", "La versión {0} está al día."}
    Friend ReadOnly Available As String() = {
        "Version {1} is available. You have version {0}.", "Está disponible la versión {1}. Tienes la versión {0}."
    }
    Friend ReadOnly ViewReleases As String() = {"View releases", "Ver versiones"}
    Friend ReadOnly Download As String() = {"Download update", "Descargar actualización"}
    Friend ReadOnly Downloading As String() = {"Downloading and verifying the launcher…", "Descargando y verificando el launcher…"}
    Friend ReadOnly Ready As String() = {
        "Download verified. Close this launcher before opening the new executable.",
        "Descarga verificada. Cierra este launcher antes de abrir el nuevo ejecutable."
    }
    Friend ReadOnly OpenFolder As String() = {"Open download folder", "Abrir carpeta de descarga"}
    Friend ReadOnly CheckError As String() = {
        "Could not check for updates. You can see the releases on GitHub.",
        "No se pudieron buscar actualizaciones. Puedes ver las versiones en GitHub."
    }
    Friend ReadOnly DownloadError As String() = {"Could not download the update. Try again.", "No se pudo descargar la actualización. Vuelve a intentarlo."}
    Friend ReadOnly LocationError As String() = {"Could not open the update location.", "No se pudo abrir la ubicación de la actualización."}
End Module
