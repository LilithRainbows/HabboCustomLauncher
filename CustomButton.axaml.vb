Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media

Partial Public Class CustomButton : Inherits UserControl

    Private WithEvents InnerButton As Button

    Sub New()
        InitializeComponent()
    End Sub

    ' Auto-wiring does not work for VB, so do it manually
    ' Wires up the controls and optionally loads XAML markup and 
    ' attaches dev tools (if Avalonia.Diagnostics package is referenced)
    Private Sub InitializeComponent(Optional loadXaml As Boolean = True)

        If loadXaml Then
            AvaloniaXamlLoader.Load(Me)
        End If

        'Control = FindNameScope().Find("Control_Name")
        InnerButton = FindNameScope().Find("InnerButton")
        UpdateButtonCorner()
    End Sub

    Public Shared ReadOnly TextProperty As AvaloniaProperty(Of String) =
            AvaloniaProperty.Register(Of CustomButton, String)(NameOf(Text))

    Public Property Text As String
        Get
            Return GetValue(TextProperty)
        End Get
        Set
            SetValue(TextProperty, Value)
        End Set
    End Property

    Public Shared ReadOnly BackColorProperty As AvaloniaProperty(Of Color) =
        AvaloniaProperty.Register(Of CustomButton, Color)(NameOf(BackColor))

    Public Property BackColor As Color
        Get
            Return GetValue(BackColorProperty)
        End Get
        Set
            SetValue(BackColorProperty, Value)
        End Set
    End Property

    Public Shared ReadOnly IsButtonDisabledProperty As AvaloniaProperty(Of Boolean) =
        AvaloniaProperty.Register(Of CustomButton, Boolean)(NameOf(IsButtonDisabled))

    Public Property IsButtonDisabled As Boolean
        Get
            Return GetValue(IsButtonDisabledProperty)
        End Get
        Set
            SetValue(IsButtonDisabledProperty, Value)
        End Set
    End Property

    Public Shared ReadOnly IsButtonCorneredProperty As AvaloniaProperty(Of Boolean) =
        AvaloniaProperty.Register(Of CustomButton, Boolean)(NameOf(IsButtonCornered))

    Public Property IsButtonCornered As Boolean
        Get
            Return GetValue(IsButtonCorneredProperty)
        End Get
        Set
            SetValue(IsButtonCorneredProperty, Value)
            UpdateButtonCorner()
        End Set
    End Property

    Public Sub UpdateButtonCorner()
        Try
            If InnerButton IsNot Nothing Then
                If GetValue(IsButtonCorneredProperty) Then
                    InnerButton.CornerRadius = New CornerRadius(3)
                Else
                    InnerButton.CornerRadius = New CornerRadius(0)
                End If
            End If
        Catch
            'Error while changing corner radius
        End Try
    End Sub

    Public Event Click(sender As Object, e As EventArgs)

    Private Sub InnerButton_Click(sender As Object, e As RoutedEventArgs) Handles InnerButton.Click
        If IsButtonDisabled = False Then
            RaiseEvent Click(Nothing, Nothing)
        End If
    End Sub

End Class