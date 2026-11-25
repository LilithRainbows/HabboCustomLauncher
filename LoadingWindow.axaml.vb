Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml

Partial Public Class LoadingWindow : Inherits Window
    Public IsFullyLoaded As Boolean = False
    Public MainMenuRequested As Boolean
    Private WithEvents Window As Window
    Private WithEvents TitleBarLabel As Label
    Public WithEvents StatusLabel As TextBlock
    Private WithEvents CloseButton As CustomButton
    Private WithEvents MainMenuButton As CustomButton
    Public CurrentLanguageInt As Integer = 0

    Sub New()
        InitializeComponent() ' This call is required by the designer
    End Sub
    Private Async Sub LoadingWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Await Task.Yield() 'Ensure window load
        IsFullyLoaded = True
        If InstalledLauncherVersion > 0 AndAlso IsVisible Then Await CheckLauncherUpdateAsync()
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
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Window.Close()
    End Sub

    Private Function GetMainWindow() As MainWindow
        Return Singleton.GetCurrentInstance.MainWindow
    End Function

    Private Sub MainMenuButton_Click(sender As Object, e As EventArgs) Handles MainMenuButton.Click
        If InstalledLauncherVersion > 0 AndAlso DownloadInProgress Then
            UpdateCancellation?.Cancel()
            Return
        End If
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
        If InstalledLauncherVersion > 0 Then
            UpdateWindowClosing = True
            UpdateCancellation?.Cancel()
            IsFullyLoaded = False
            Return
        End If
        If MainMenuRequested Then
            GetMainWindow.Show()
            GetMainWindow.LoadingWindowChild = Nothing
        Else
            If Design.IsDesignMode = False Then
                Process.GetCurrentProcess.Kill()
            End If
        End If
        IsFullyLoaded = False
    End Sub
    Public Async Function MsgBox(Title As String, Message As String, Optional ClipboardDebugContent As String = "") As Task(Of Boolean)
        Dim ErrorDialog As New MessageBox()
        ErrorDialog.ConfigureContent(Title, Message, ClipboardDebugContent)
        Do While Window.IsVisible = False
            Await Task.Delay(100)
        Loop
        Await ErrorDialog.ShowDialog(Window)
        Return True
    End Function

End Class

Public Class LauncherUpdaterTranslator
    '0=English 1=Spanish
    Public Shared ReturnToMainMenu As String() = {
        "Return to main menu",
        "Volver al menu principal"
    }
End Class