Module SingleInstance 'App framework must be disabled from project settings and then select SingleInstance as Startup Object 
    Sub Main(Args As String())
        Dim noPreviousInstance As Boolean
        Using m As New Threading.Mutex(True, "HabboCustomLauncher", noPreviousInstance)
            If Not noPreviousInstance Then
                MessageBox.Show("Program is already running!", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            Else
                Dim NewMainWindow As New MainWindow()
                Dim NewApp As New Application()
                Try
                    If Args.Length = 1 Then
                        If Args(0).Contains("server=") And Args(0).Contains("token=") Then
                            Dim RequestedServer As String = Args(0)
                            RequestedServer = RequestedServer.Remove(0, RequestedServer.IndexOf("server=") + 7)
                            If RequestedServer.Contains("&") Then
                                RequestedServer = RequestedServer.Remove(RequestedServer.IndexOf("&"))
                            End If
                            Dim RequestedTicket As String = Args(0)
                            RequestedTicket = RequestedTicket.Remove(0, RequestedTicket.IndexOf("token=") + 6)
                            If RequestedTicket.Contains("&") Then
                                RequestedTicket = RequestedTicket.Remove(RequestedTicket.IndexOf("&"))
                            End If
                            NewMainWindow.RequestedTicket = RequestedTicket
                            NewMainWindow.RequestedServer = RequestedServer.Remove(0, 2)
                            NewMainWindow.RequestedURI = Args(0)
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
                NewApp.Run(NewMainWindow)
            End If
        End Using
    End Sub
End Module
