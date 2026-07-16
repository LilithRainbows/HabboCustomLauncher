Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Xml
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Layout
Imports Avalonia.Media
Imports Avalonia.Threading
Imports Avalonia.VisualTree

Public Class Singleton

    Private Shared ReadOnly CurrentInstance As New Singleton()
    Public MainWindow As MainWindow
    Private BaseSizes As New Dictionary(Of Window, (Double, Double))

    ' === DPI DETECTION REFERENCES ===
    Private Declare Function GetDC Lib "user32" (ByVal hwnd As IntPtr) As IntPtr
    Private Declare Function ReleaseDC Lib "user32" (ByVal hwnd As IntPtr, ByVal hdc As IntPtr) As Integer
    Private Declare Function GetDeviceCaps Lib "gdi32" (ByVal hdc As IntPtr, ByVal nIndex As Integer) As Integer
    Private Const LOGPIXELSX As Integer = 88
    ' ================================
    Public UpdateSource As String = "AIR_Plus"
    Public CustomWindowScale As Double = 1 '(1 = 100% or autohdpi for win7/8/8.1)
    Public ClientRenderMode As String = "cpu" '(gpu for osx)
    Public ClientResolution As String = "standard"

    Public Function GetWindowsDpiScale() As Double
        Dim hdc As IntPtr = GetDC(IntPtr.Zero)
        Dim dpiX As Integer = GetDeviceCaps(hdc, LOGPIXELSX)
        ReleaseDC(IntPtr.Zero, hdc)
        Return dpiX / 96.0
    End Function

    Public Function ScaleMainGrid(RequestedWindow As Window)
        Dim osVersion = Environment.OSVersion.Version
        Dim escala As Double = CustomWindowScale
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) AndAlso osVersion.Major = 6 AndAlso escala = 1 Then 'Windows 7/8/8.1
            escala = GetWindowsDpiScale()
        End If

        If Not BaseSizes.ContainsKey(RequestedWindow) Then
            BaseSizes(RequestedWindow) = (RequestedWindow.Width, RequestedWindow.Height)
        End If
        Dim baseW = BaseSizes(RequestedWindow).Item1
        Dim baseH = BaseSizes(RequestedWindow).Item2

        Dim g = TryCast(RequestedWindow.Content, Grid)
        If g IsNot Nothing Then
            ' Tamaño fijo = tamaño de diseño, SIN margin manual
            g.Width = baseW
            g.Height = baseH
            g.HorizontalAlignment = HorizontalAlignment.Center
            g.VerticalAlignment = VerticalAlignment.Center

            Dim transform As New ScaleTransform(escala, escala)
            g.RenderTransform = transform
            g.RenderTransformOrigin = New RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        End If

        RequestedWindow.Width = baseW * escala
        RequestedWindow.Height = baseH * escala

        Dispatcher.UIThread.Post(Sub()
                                     Dim screen = RequestedWindow.Screens.ScreenFromVisual(RequestedWindow)
                                     Dim wa = screen.WorkingArea
                                     Dim scale = RequestedWindow.RenderScaling
                                     Dim widthPx = CInt(RequestedWindow.ClientSize.Width * scale)
                                     Dim heightPx = CInt(RequestedWindow.ClientSize.Height * scale)
                                     RequestedWindow.Position = New PixelPoint(
                CInt(wa.X + (wa.Width - widthPx) / 2),
                CInt(wa.Y + (wa.Height - heightPx) / 2))
                                 End Sub)
    End Function

    Public Function GetLauncherDownloadFolder() As String
        Dim DestinationFolder = Path.Combine(MainWindow.GetAppDataPath, "Habbo Launcher", "downloads")
        Directory.CreateDirectory(DestinationFolder)
        Return DestinationFolder
    End Function

    Public Sub LoadSavedDataFromXML()
        Dim XmlDocument = New XmlDocument()
        If IO.File.Exists(Path.Combine(GetLauncherDownloadFolder, "GlobalSettings.xml")) Then
            XmlDocument.Load(Path.Combine(GetLauncherDownloadFolder, "GlobalSettings.xml"))
            For Each SavedRequestedItem As XmlNode In XmlDocument("GlobalSettings")
                Dim SettingName = SavedRequestedItem.Attributes("Name").Value
                Dim SettingValue = SavedRequestedItem.Attributes("Value").Value
                If SettingName = "UpdateSource" Then
                    UpdateSource = Convert.ToString(SettingValue)
                End If
                If SettingName = "CustomWindowScale" Then
                    CustomWindowScale = Convert.ToDouble(SettingValue)
                End If
                If SettingName = "ClientRenderMode" Then
                    ClientRenderMode = Convert.ToString(SettingValue)
                End If
                If SettingName = "ClientResolution" Then
                    ClientResolution = Convert.ToString(SettingValue)
                End If
            Next
        End If
        XmlDocument = Nothing
    End Sub

    Public Sub SaveGlobalSettingsXML()
        Dim GlobalSettings As New Dictionary(Of String, String)
        GlobalSettings.Add("UpdateSource", UpdateSource)
        GlobalSettings.Add("CustomWindowScale", CustomWindowScale)
        GlobalSettings.Add("ClientRenderMode", ClientRenderMode)
        GlobalSettings.Add("ClientResolution", ClientResolution)
        Using XmlWriter As New XmlTextWriter(Path.Combine(GetLauncherDownloadFolder, "GlobalSettings.xml"), Text.Encoding.UTF8)
            With XmlWriter
                .WriteStartDocument()
                .Formatting = Formatting.Indented
                .WriteStartElement("GlobalSettings")
                For Each GlobalSetting In GlobalSettings
                    .WriteStartElement("Item")
                    .WriteAttributeString("Name", GlobalSetting.Key)
                    .WriteAttributeString("Value", GlobalSetting.Value)
                    .WriteEndElement()
                Next
                .WriteEndElement()
                .WriteEndDocument()
                .Close()
            End With
        End Using
    End Sub

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property GetCurrentInstance As Singleton
        Get
            Return CurrentInstance
        End Get
    End Property

End Class