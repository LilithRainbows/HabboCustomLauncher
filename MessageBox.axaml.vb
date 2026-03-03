Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Markup.Xaml
Partial Public Class MessageBox : Inherits Window
    Private WithEvents Window As Window
    Private WithEvents TitleBarLabel As Label
    Public WithEvents MessageLabel As TextBlock
    Private WithEvents CloseButton As CustomButton
    Private WithEvents OkButton As CustomButton
    Public CurrentLanguageInt As Integer = 0

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
    End Sub

    Sub ConfigureContent(Title As String, Message As String)
        TitleBarLabel.Content = "    " & Title
        MessageLabel.Text = Message
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
End Class
