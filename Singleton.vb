Public Class Singleton

    Private Shared ReadOnly CurrentInstance As New Singleton()
    Public MainWindow As MainWindow

    ' Constructor privado para evitar que se cree desde afuera
    Private Sub New()
    End Sub

    Public Shared ReadOnly Property GetCurrentInstance As Singleton
        Get
            Return CurrentInstance
        End Get
    End Property

End Class