Class Application

    ' Los eventos de nivel de aplicación, como Startup, Exit y DispatcherUnhandledException
    ' se pueden controlar en este archivo.

    Private Sub Application_Startup(ByVal sender As Object, ByVal e As StartupEventArgs)
        Dim NewMainWindow = New MainWindow()
        Try
            If e.Args.Length = 1 Then
                If e.Args(0).Contains("server=") And e.Args(0).Contains("token=") Then
                    Dim RequestedServer As String = e.Args(0)
                    RequestedServer = RequestedServer.Remove(0, RequestedServer.IndexOf("server=") + 7)
                    If RequestedServer.Contains("&") Then
                        RequestedServer = RequestedServer.Remove(RequestedServer.IndexOf("&"))
                    End If
                    Dim RequestedTicket As String = e.Args(0)
                    RequestedTicket = RequestedTicket.Remove(0, RequestedTicket.IndexOf("token=") + 6)
                    If RequestedTicket.Contains("&") Then
                        RequestedTicket = RequestedTicket.Remove(RequestedTicket.IndexOf("&"))
                    End If
                    NewMainWindow.RequestedTicket = RequestedTicket
                    NewMainWindow.RequestedServer = RequestedServer.Remove(0, 2)
                    NewMainWindow.RequestedURI = e.Args(0)
                    NewMainWindow.WindowStyle = WindowStyle.None
                    NewMainWindow.AllowsTransparency = True
                    NewMainWindow.Opacity = 0
                End If
            End If
        Catch
            MsgBox("Could not parse Ticket.", MsgBoxStyle.Critical, "Error")
            Environment.Exit(0)
            Exit Sub
        End Try
        NewMainWindow.Show()
    End Sub

End Class
