Imports System.Windows.Threading

Public Class LoadingWindow
    Public LoadingTextValue As String
    Public LoadingCurrentDot As Integer
    Public WithEvents LoadingDotsTimer As New DispatcherTimer()

    Public Sub New(LoadingTextValue As String)
        InitializeComponent()
        Me.LoadingTextValue = LoadingTextValue
        LoadingCurrentDot = 1
        LoadingTB.Text = LoadingTextValue & " ."
        HiddenLoadingTB.Text = LoadingTextValue & " ..."
        LoadingDotsTimer.Interval = TimeSpan.FromMilliseconds(500)
        LoadingDotsTimer.Start()
    End Sub

    Private Sub LoadingDotsTimer_Tick(sender As Object, e As EventArgs) Handles LoadingDotsTimer.Tick
        LoadingTB.Text = LoadingTextValue & " " & New String(".", LoadingCurrentDot)
        If LoadingCurrentDot >= 3 Then
            LoadingCurrentDot = 1
        Else
            LoadingCurrentDot += 1
        End If
    End Sub

End Class
