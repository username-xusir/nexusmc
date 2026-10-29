Public Module NexCompatibility
    Public Function RequestTaskTempFolder(Optional RequireNonSpace As Boolean = False) As String
        Dim folder = $"{PathTemp}TaskTemp\{Guid.NewGuid():N}\"
        If RequireNonSpace AndAlso folder.Contains(" ") Then
            folder = $"{OsDrive}ProgramData\NEXMC\TaskTemp\{Guid.NewGuid():N}\"
        End If
        DirectoryUtils.Create(folder)
        CheckPermissionWithException(folder)
        Return folder
    End Function

    Public Sub McLaunchLog(text As String)
        Log("[Launch] " & text)
    End Sub
End Module

Public Class PageDownloadResourceDetail
    Public Shared CachedFolder As New Dictionary(Of String, String)
End Class
Public Class PageDownloadInstall
    Public Shared VersionWaitingSelect As String
End Class
Public Class PageSelectLeft
    Public Shared Sub AddFolder(folder As String, name As String, refresh As Boolean)
        ModBase.Log("[NEXMC] 解压安装目标：" & folder)
    End Sub
End Class
Public Class SettingService
End Class
Public Class NexLoginOutput
    Public AccessToken As String
    Public Uuid As String
End Class
Public Class NexLoginLoaderStub
    Public Output As New NexLoginOutput
End Class
Public Module NexLoginCompatibility
    Public McLoginLoader As New NexLoginLoaderStub
End Module
