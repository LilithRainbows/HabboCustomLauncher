Imports System.Runtime.InteropServices
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Partial Public Class SettingsWindow : Inherits Window
    Public IsFullyLoaded As Boolean = False
    Private WithEvents Window As Window
    Private WithEvents TitleBarLabel As Label
    Public WithEvents MessageLabel As TextBlock
    Private WithEvents CloseButton As CustomButton
    Private WithEvents OkButton As CustomButton
    Private WithEvents LauncherScalingUpDown As NumericUpDown
    Private WithEvents LauncherScalingResetButton As CustomButton
    Private WithEvents LauncherScalingHelpButton As CustomButton
    Private WithEvents ClientRenderModeButton As CustomButton
    Private WithEvents ClientResolutionButton As CustomButton
    Private WithEvents ClientRenderResetButton As CustomButton
    Private WithEvents ClientRenderHelpButton As CustomButton
    Public CurrentLanguageInt As Integer = 0
    Public CopyMessageToClipboardBusy As Boolean = False
    Public ClipboardDebugContent As String = ""

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
        CloseButton = FindNameScope().Find("CloseButton")
        OkButton = FindNameScope().Find("OkButton")
        LauncherScalingUpDown = FindNameScope().Find("LauncherScalingUpDown")
        LauncherScalingResetButton = FindNameScope().Find("LauncherScalingResetButton")
        LauncherScalingHelpButton = FindNameScope().Find("LauncherScalingHelpButton")
        ClientRenderModeButton = FindNameScope().Find("ClientRenderModeButton")
        ClientResolutionButton = FindNameScope().Find("ClientResolutionButton")
        ClientRenderResetButton = FindNameScope().Find("ClientRenderResetButton")
        ClientRenderHelpButton = FindNameScope().Find("ClientRenderHelpButton")
        Singleton.GetCurrentInstance().ScaleMainGrid(Window)

        ShowSavedSettings()
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Window.Close()
    End Sub

    Private Sub OkButton_Click(sender As Object, e As EventArgs) Handles OkButton.Click
        Singleton.GetCurrentInstance().CustomWindowScale = LauncherScalingUpDown.Value / 100
        With ClientResolutionButton
            Singleton.GetCurrentInstance().ClientResolution = .Text.ToLower.Remove(0, .Text.LastIndexOf(" ") + 1)
        End With
        With ClientRenderModeButton
            Singleton.GetCurrentInstance().ClientRenderMode = .Text.ToLower.Remove(0, .Text.LastIndexOf(" ") + 1)
        End With
        Singleton.GetCurrentInstance().SaveGlobalSettingsXML()
        Singleton.GetCurrentInstance().ScaleMainGrid(Singleton.GetCurrentInstance().MainWindow)
        Window.Close()
    End Sub

    Private Sub TitleBarLabel_PointerPressed(sender As Object, e As PointerPressedEventArgs) Handles TitleBarLabel.PointerPressed
        ' Solo con botón izquierdo
        If e.GetCurrentPoint(TitleBarLabel).Properties.IsLeftButtonPressed Then
            ' Avalonia se encarga de DPI y límites automáticamente
            Me.BeginMoveDrag(e)
        End If
    End Sub

    Private Async Sub SettingsWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Await Task.Yield() 'Ensure window load
        IsFullyLoaded = True
    End Sub

    Private Sub SettingsWindow_Closing(sender As Object, e As WindowClosingEventArgs) Handles Me.Closing
        IsFullyLoaded = False
    End Sub

    Private Sub LauncherScalingUpDown_KeyUp(sender As Object, e As KeyEventArgs) Handles LauncherScalingUpDown.KeyUp
        If e.Key = Key.Enter Then
            FocusManager.ClearFocus()
        End If
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

    Private Sub LauncherScalingHelpButton_Click(sender As Object, e As EventArgs) Handles LauncherScalingHelpButton.Click
        MsgBox("Example", "Launcher scaling help button clicked!")
    End Sub

    Private Sub ClientRenderHelpButton_Click(sender As Object, e As EventArgs) Handles ClientRenderHelpButton.Click
        MsgBox("Example", "Client rendering help button clicked!")
    End Sub

    Private Sub ClientRenderModeButton_Click(sender As Object, e As EventArgs) Handles ClientRenderModeButton.Click
        With ClientRenderModeButton
            Select Case .Text.ToLower.Remove(0, .Text.LastIndexOf(" ") + 1)
                Case "gpu"
                    .Text = "Mode: Direct"
                Case "direct"
                    .Text = "Mode: CPU"
                Case "cpu"
                    .Text = "Mode: GPU"
            End Select
        End With
    End Sub

    Private Sub ShowSavedSettings()
        With Singleton.GetCurrentInstance()
            LauncherScalingUpDown.Value = .CustomWindowScale * 100
            Select Case .ClientRenderMode
                Case "gpu"
                    ClientRenderModeButton.Text = "Mode: GPU"
                Case "direct"
                    ClientRenderModeButton.Text = "Mode: Direct"
                Case "cpu"
                    ClientRenderModeButton.Text = "Mode: CPU"
            End Select
            Select Case .ClientResolution
                Case "standard"
                    ClientResolutionButton.Text = "Resolution: Standard"
                Case "high"
                    ClientResolutionButton.Text = "Resolution: High"
            End Select
        End With
    End Sub

    Private Sub ClientResolutionButton_Click(sender As Object, e As EventArgs) Handles ClientResolutionButton.Click
        With ClientResolutionButton
            Select Case .Text.ToLower.Remove(0, .Text.LastIndexOf(" ") + 1)
                Case "standard"
                    .Text = "Resolution: High"
                Case "high"
                    .Text = "Resolution: Standard"
            End Select
        End With
    End Sub

    Private Sub LauncherScalingResetButton_Click(sender As Object, e As EventArgs) Handles LauncherScalingResetButton.Click
        LauncherScalingUpDown.Value = 100
    End Sub

    Private Sub ClientRenderResetButton_Click(sender As Object, e As EventArgs) Handles ClientRenderResetButton.Click
        If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
            ClientRenderModeButton.Text = "Mode: GPU"
        Else
            ClientRenderModeButton.Text = "Mode: CPU"
        End If
        ClientResolutionButton.Text = "Resolution: Standard"
    End Sub
End Class
