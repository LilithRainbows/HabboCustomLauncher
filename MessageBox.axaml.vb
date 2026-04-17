Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media
Imports Avalonia.Threading
Imports Tmds.DBus.Protocol
Partial Public Class MessageBox : Inherits Window
    Private WithEvents Window As Window
    Private WithEvents TitleBarLabel As Label
    Public WithEvents MessageLabel As TextBlock
    Private WithEvents CloseButton As CustomButton
    Private WithEvents OkButton As CustomButton
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
        MessageLabel = FindNameScope().Find("MessageLabel")
        CloseButton = FindNameScope().Find("CloseButton")
        OkButton = FindNameScope().Find("OkButton")
        Singleton.GetCurrentInstance().ScaleMainGrid(Window)
    End Sub

    Sub ConfigureContent(Title As String, Message As String, Optional ClipboardDebugContent As String = "")
        If ClipboardDebugContent = "" Then
            Me.ClipboardDebugContent = Message
        Else
            Me.ClipboardDebugContent = ClipboardDebugContent
        End If
        If Title.StartsWith("    ") Then
            TitleBarLabel.Content = Title
        Else
            TitleBarLabel.Content = "    " & Title
        End If
        MessageLabel.Text = Message
        AutoAdjustMessageFontSize()
    End Sub

    Sub AutoAdjustMessageFontSize()
        Select Case MessageLabel.Text.Length
            Case > 90
                MessageLabel.FontSize = 15
            Case > 40
                MessageLabel.FontSize = 20
            Case Else
                MessageLabel.FontSize = 30
        End Select
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Window.Close()
    End Sub

    Private Sub OkButton_Click(sender As Object, e As EventArgs) Handles OkButton.Click
        Window.Close()
    End Sub

    Private Sub TitleBarLabel_PointerPressed(sender As Object, e As PointerPressedEventArgs) Handles TitleBarLabel.PointerPressed
        ' Solo con botón izquierdo
        If e.GetCurrentPoint(TitleBarLabel).Properties.IsLeftButtonPressed Then
            ' Avalonia se encarga de DPI y límites automáticamente
            Me.BeginMoveDrag(e)
        End If
    End Sub

    Private Sub MessageBox_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.Key = Avalonia.Input.Key.C AndAlso
(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) OrElse
e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)) Then
            If CopyMessageToClipboardBusy = False Then
                CopyMessageToClipboard()
            End If
        End If
    End Sub

    Async Sub CopyMessageToClipboard()
        CopyMessageToClipboardBusy = True
        Await Clipboard.SetTextAsync(ClipboardDebugContent)
        Dim OriginalMessageLabelText = MessageLabel.Text
        MessageLabel.Text = ClipboardDebugContent
        AutoAdjustMessageFontSize()
        MessageLabel.Background = Brushes.DarkGreen
        Await Task.Delay(500)
        MessageLabel.Text = OriginalMessageLabelText
        AutoAdjustMessageFontSize()
        MessageLabel.Background = Nothing
        CopyMessageToClipboardBusy = False
    End Sub
End Class
