Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics
Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports Avalonia.Controls

Partial Public Class LoadingWindow
    Private Shared ReadOnly UpdateService As New Lazy(Of LauncherUpdates)(
        Function() New LauncherUpdates(LauncherUpdates.CreateClient()))
    Private WithEvents UpdateActionButton As CustomButton
    Private DownloadProgress As ProgressBar
    Private ReleaseUpdates As LauncherUpdates
    Private UpdatesFolder As String
    Private InstalledLauncherVersion As Integer
    Private UpdateCancellation As CancellationTokenSource
    Private PendingUpdate As LauncherUpdate
    Private DownloadedUpdatePath As String
    Private RetryCheck As Boolean
    Private DownloadInProgress As Boolean
    Private UpdateWindowClosing As Boolean

    Friend Sub ConfigureLauncherUpdates(installedVersion As Integer, Optional service As LauncherUpdates = Nothing,
                                       Optional updateFolder As String = Nothing)
        InstalledLauncherVersion = installedVersion
        ReleaseUpdates = If(service, UpdateService.Value)
        UpdatesFolder = updateFolder
        UpdateActionButton = Me.FindControl(Of CustomButton)("UpdateActionButton")
        DownloadProgress = Me.FindControl(Of ProgressBar)("LauncherDownloadProgress")
        StatusLabel.FontSize = 22
        StatusLabel.Text = LauncherUpdateText.Checking(CurrentLanguageInt)
        MainMenuButton.Text = LauncherUpdateText.Close(CurrentLanguageInt)
    End Sub

    Private Async Function CheckLauncherUpdateAsync() As Task
        Using operation As New CancellationTokenSource()
            UpdateCancellation = operation
            RetryCheck = False
            UpdateActionButton.IsButtonDisabled = True
            StatusLabel.Text = LauncherUpdateText.Checking(CurrentLanguageInt)
            Try
                PendingUpdate = Await ReleaseUpdates.CheckAsync(InstalledLauncherVersion, operation.Token)
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
                RetryCheck = True
                StatusLabel.Text = DescribeUpdateError(ex, LauncherUpdateText.CheckError)
                ShowUpdateAction(LauncherUpdateText.Retry(CurrentLanguageInt))
            Finally
                UpdateCancellation = Nothing
            End Try
        End Using
    End Function

    Private Async Sub UpdateActionButton_Click(sender As Object, e As EventArgs) Handles UpdateActionButton.Click
        If UpdateCancellation IsNot Nothing Then Return
        If DownloadedUpdatePath IsNot Nothing Then
            Await VerifyAndOpenDownloadAsync()
        ElseIf RetryCheck Then
            Await CheckLauncherUpdateAsync()
        ElseIf PendingUpdate Is Nothing Then
            OpenUpdateLocation(LauncherUpdates.ReleasesPage)
        Else
            Await DownloadLauncherUpdateAsync()
        End If
    End Sub

    Private Async Function DownloadLauncherUpdateAsync() As Task
        Using operation As New CancellationTokenSource()
            UpdateCancellation = operation
            DownloadInProgress = True
            UpdateActionButton.IsButtonDisabled = True
            MainMenuButton.Text = LauncherUpdateText.CancelDownload(CurrentLanguageInt)
            DownloadProgress.Value = 0
            DownloadProgress.IsVisible = True
            StatusLabel.Text = LauncherUpdateText.Downloading(CurrentLanguageInt)
            Dim elapsed = Stopwatch.StartNew()
            Dim progress As New Progress(Of Long)(
                Sub(bytes)
                    If UpdateCancellation IsNot operation OrElse operation.IsCancellationRequested Then Return
                    Dim percent As Double = CDbl(bytes) / PendingUpdate.Size * 100
                    Dim speed As Double = bytes / Math.Max(elapsed.Elapsed.TotalSeconds, 0.001) / (1024 * 1024)
                    DownloadProgress.Value = percent
                    StatusLabel.Text = String.Format(LauncherUpdateText.DownloadProgress(CurrentLanguageInt), percent, speed)
                End Sub)
            Try
                Dim folder = If(UpdatesFolder, Path.Combine(MainWindow.GetAppDataPath(), "Habbo Launcher", "launcher-updates"))
                DownloadedUpdatePath = Await ReleaseUpdates.DownloadAsync(PendingUpdate, folder, operation.Token, progress)
                If UpdateWindowClosing Then Return
                StatusLabel.Text = LauncherUpdateText.Ready(CurrentLanguageInt)
                ShowUpdateAction(LauncherUpdateText.OpenFolder(CurrentLanguageInt))
            Catch ex As OperationCanceledException When operation.IsCancellationRequested
                If Not UpdateWindowClosing Then
                    StatusLabel.Text = LauncherUpdateText.DownloadCanceled(CurrentLanguageInt)
                    ShowUpdateAction(LauncherUpdateText.Download(CurrentLanguageInt))
                End If
            Catch ex As Exception
                Debug.WriteLine(ex)
                If operation.IsCancellationRequested Then Return
                StatusLabel.Text = DescribeUpdateError(ex, LauncherUpdateText.DownloadError)
                ShowUpdateAction(LauncherUpdateText.Download(CurrentLanguageInt))
            Finally
                DownloadInProgress = False
                DownloadProgress.IsVisible = False
                MainMenuButton.Text = LauncherUpdateText.Close(CurrentLanguageInt)
                UpdateCancellation = Nothing
            End Try
        End Using
    End Function

    Private Async Function VerifyAndOpenDownloadAsync() As Task
        Using operation As New CancellationTokenSource()
            UpdateCancellation = operation
            UpdateActionButton.IsButtonDisabled = True
            Try
                Dim verified = Await LauncherUpdates.IsVerifiedAsync(DownloadedUpdatePath, PendingUpdate, operation.Token)
                If operation.IsCancellationRequested Then Return
                If verified Then
                    OpenUpdateLocation(Path.GetDirectoryName(DownloadedUpdatePath))
                Else
                    DownloadedUpdatePath = Nothing
                    StatusLabel.Text = LauncherUpdateText.Changed(CurrentLanguageInt)
                    ShowUpdateAction(LauncherUpdateText.Download(CurrentLanguageInt))
                End If
            Catch ex As OperationCanceledException When operation.IsCancellationRequested
            Catch ex As Exception
                Debug.WriteLine(ex)
                If Not operation.IsCancellationRequested Then StatusLabel.Text = DescribeUpdateError(ex, LauncherUpdateText.LocationError)
            Finally
                UpdateActionButton.IsButtonDisabled = False
                UpdateCancellation = Nothing
            End Try
        End Using
    End Function

    Private Function DescribeUpdateError(problem As Exception, fallback As String()) As String
        Dim failure = TryCast(problem, LauncherUpdateException)
        If failure IsNot Nothing Then
            Select Case failure.Failure
                Case LauncherUpdateFailure.RateLimit
                    Return LauncherUpdateText.RateLimit(CurrentLanguageInt)
                Case LauncherUpdateFailure.DiskFull
                    Return LauncherUpdateText.DiskFull(CurrentLanguageInt)
                Case LauncherUpdateFailure.Integrity
                    Return LauncherUpdateText.IntegrityError(CurrentLanguageInt)
                Case LauncherUpdateFailure.Transfer
                    Return LauncherUpdateText.TransferError(CurrentLanguageInt)
            End Select
        End If
        If TypeOf problem Is TimeoutException Then Return LauncherUpdateText.TimedOut(CurrentLanguageInt)
        If TypeOf problem Is HttpRequestException Then Return LauncherUpdateText.ConnectionError(CurrentLanguageInt)
        If TypeOf problem Is UnauthorizedAccessException Then Return LauncherUpdateText.Permissions(CurrentLanguageInt)
        If TypeOf problem Is IOException AndAlso Not TypeOf problem Is InvalidDataException Then Return LauncherUpdateText.StorageError(CurrentLanguageInt)
        Return fallback(CurrentLanguageInt)
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
    Friend ReadOnly Retry As String() = {"Retry check", "Reintentar búsqueda"}
    Friend ReadOnly Download As String() = {"Download update", "Descargar actualización"}
    Friend ReadOnly Downloading As String() = {"Downloading and verifying the launcher…", "Descargando y verificando el launcher…"}
    Friend ReadOnly DownloadProgress As String() = {"Downloading… {0:N0}% · {1:N1} MiB/s", "Descargando… {0:N0}% · {1:N1} MiB/s"}
    Friend ReadOnly CancelDownload As String() = {"Cancel download", "Cancelar descarga"}
    Friend ReadOnly DownloadCanceled As String() = {"Download canceled.", "Descarga cancelada."}
    Friend ReadOnly Ready As String() = {
        "Download verified. Close this launcher before opening the new executable.",
        "Descarga verificada. Cierra este launcher antes de abrir el nuevo ejecutable."
    }
    Friend ReadOnly OpenFolder As String() = {"Open download folder", "Abrir carpeta de descarga"}
    Friend ReadOnly CheckError As String() = {
        "Could not read the release information.", "No se pudo leer la información de la versión."
    }
    Friend ReadOnly DownloadError As String() = {"Could not download the update. Try again.", "No se pudo descargar la actualización. Vuelve a intentarlo."}
    Friend ReadOnly LocationError As String() = {"Could not open the update location.", "No se pudo abrir la ubicación de la actualización."}
    Friend ReadOnly RateLimit As String() = {"GitHub has limited update checks. Try again later.", "GitHub ha limitado las consultas. Reintenta más tarde."}
    Friend ReadOnly DiskFull As String() = {"There is not enough free space for the update.", "No hay suficiente espacio libre para la actualización."}
    Friend ReadOnly IntegrityError As String() = {"The download did not match the published checksum or size.", "La descarga no coincide con el SHA-256 o tamaño publicado."}
    Friend ReadOnly TransferError As String() = {"The connection ended during the download.", "La conexión se interrumpió durante la descarga."}
    Friend ReadOnly TimedOut As String() = {"The request timed out.", "La operación superó el tiempo de espera."}
    Friend ReadOnly ConnectionError As String() = {"Could not get a successful response from GitHub.", "No se obtuvo una respuesta correcta de GitHub."}
    Friend ReadOnly Permissions As String() = {"Access to the update folder was denied.", "No hay permiso para acceder a la carpeta de actualizaciones."}
    Friend ReadOnly StorageError As String() = {"Could not read or write the update file.", "No se pudo leer o escribir el archivo de actualización."}
    Friend ReadOnly Changed As String() = {"The verified file changed. Download the update again.", "El archivo verificado cambió. Descarga de nuevo la actualización."}
End Module
