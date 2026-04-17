Imports Avalonia
Imports System.Runtime.InteropServices
Imports Avalonia.Controls
Imports Avalonia.Media

Public Class Singleton

    Private Shared ReadOnly CurrentInstance As New Singleton()
    Public MainWindow As MainWindow

    ' === DPI DETECTION REFERENCES ===
    Private Declare Function GetDC Lib "user32" (ByVal hwnd As IntPtr) As IntPtr
    Private Declare Function ReleaseDC Lib "user32" (ByVal hwnd As IntPtr, ByVal hdc As IntPtr) As Integer
    Private Declare Function GetDeviceCaps Lib "gdi32" (ByVal hdc As IntPtr, ByVal nIndex As Integer) As Integer
    Private Const LOGPIXELSX As Integer = 88
    Public CustomWindowScale As Double = 0 '0=Disabled
    Public Function GetWindowsDpiScale() As Double
        Dim hdc As IntPtr = GetDC(IntPtr.Zero)
        Dim dpiX As Integer = GetDeviceCaps(hdc, LOGPIXELSX)
        ReleaseDC(IntPtr.Zero, hdc)
        Return dpiX / 96.0
    End Function
    Public Function ScaleMainGrid(RequestedWindow As Window)
        Dim osVersion = Environment.OSVersion.Version
        If (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) AndAlso osVersion.Major = 6) Or CustomWindowScale > 0 Then 'Windows 7/8/8.1 or CustomScale
            Dim escala As Double = CustomWindowScale
            If escala = 0 Then
                escala = GetWindowsDpiScale()
            End If
            If CustomWindowScale > 0 OrElse (RequestedWindow.RenderScaling = escala = False) Then
                Dim g = TryCast(RequestedWindow.Content, Grid)
                g.Margin = New Thickness(RequestedWindow.Width * (escala - 1.0) / 2, RequestedWindow.Height * (escala - 1.0) / 2)
                If g IsNot Nothing Then
                    Dim transform As New ScaleTransform(escala, escala)
                    g.RenderTransform = transform
                    g.RenderTransformOrigin = New RelativePoint(0.5, 0.5, RelativeUnit.Relative)
                End If
                RequestedWindow.Width *= escala
                RequestedWindow.Height *= escala
                Dim screen = RequestedWindow.Screens.ScreenFromVisual(RequestedWindow)
                Dim wa = screen.WorkingArea
                RequestedWindow.Position = New PixelPoint(CInt(wa.X + (wa.Width - RequestedWindow.Bounds.Width) / 2), CInt(wa.Y + (wa.Height - RequestedWindow.Bounds.Height) / 2))
                'MsgBox("Hdpi Debug", "Escala aplicada: " & escala.ToString("0.00"))
            End If
        End If
    End Function
    ' ================================


    ' Constructor privado para evitar que se cree desde afuera
    Private Sub New()
    End Sub

    Public Shared ReadOnly Property GetCurrentInstance As Singleton
        Get
            Return CurrentInstance
        End Get
    End Property

End Class