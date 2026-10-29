Imports Newtonsoft.Json
Imports System.Reflection

''' <summary>NEXMC 的 PCL 设置适配层。仅保留下载所需默认值，不读取 PCL 注册表。</summary>
Public Class Settings
    Private Shared ReadOnly SyncRoot As New Object
    Private Shared _defaults As JObject
    Private Shared _values As JObject
    Private Shared ReadOnly DataFolder As String = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC")
    Private Shared ReadOnly SettingsPath As String = IO.Path.Combine(DataFolder, "kernel-settings.json")

    Private Shared Function Defaults() As JObject
        If _defaults IsNot Nothing Then Return _defaults
        Dim asm = Assembly.GetExecutingAssembly()
        Dim resourceName = asm.GetManifestResourceNames().FirstOrDefault(Function(name) name.EndsWith("kernel-defaults.json", StringComparison.OrdinalIgnoreCase))
        If resourceName Is Nothing Then Throw New InvalidOperationException("缺少内核默认设置资源")
        Using Stream = asm.GetManifestResourceStream(resourceName)
            If Stream Is Nothing Then Throw New InvalidOperationException("无法读取内核默认设置资源")
            Using Reader As New StreamReader(Stream, Encoding.UTF8)
                _defaults = JObject.Parse(Reader.ReadToEnd())
            End Using
        End Using
        Return _defaults
    End Function

    Private Shared Function Values() As JObject
        If _values IsNot Nothing Then Return _values
        SyncLock SyncRoot
            If _values IsNot Nothing Then Return _values
            Directory.CreateDirectory(DataFolder)
            If File.Exists(SettingsPath) Then
                Try
                    _values = JObject.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8))
                Catch ex As Exception
                    Log(ex, "NEXMC 内核设置损坏，使用默认值")
                    _values = New JObject()
                End Try
            Else
                _values = New JObject()
            End If
        End SyncLock
        Return _values
    End Function

    Public Shared Function [Get](Key As String, Optional Instance As McInstance = Nothing) As Object
        If Instance IsNot Nothing Then
            Dim fallback = [Get](Key)
            Dim raw = ReadIni(Instance.PathVersion & "PCL\Setup.ini", Key, CStr(fallback))
            If TypeOf fallback Is Boolean Then Return CBool(raw)
            If TypeOf fallback Is Integer Then Return CInt(Val(raw))
            Return raw
        End If
        SyncLock SyncRoot
            Dim token As JToken = Nothing
            If Values().TryGetValue(Key, token) Then Return token.ToObject(Of Object)()
            If Not Defaults().TryGetValue(Key, token) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
            Return token.ToObject(Of Object)()
        End SyncLock
    End Function

    Public Shared Sub [Set](Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        If Instance IsNot Nothing Then
            WriteIni(Instance.PathVersion & "PCL\Setup.ini", Key, CStr(Value))
            Return
        End If
        If Not Defaults().ContainsKey(Key) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        SyncLock SyncRoot
            Values()(Key) = If(Value Is Nothing, JValue.CreateNull(), JToken.FromObject(Value))
            Directory.CreateDirectory(DataFolder)
            Dim temp = SettingsPath & ".tmp"
            File.WriteAllText(temp, Values().ToString(Formatting.Indented), New UTF8Encoding(False))
            File.Copy(temp, SettingsPath, True)
            File.Delete(temp)
        End SyncLock
    End Sub

    Public Shared Function GetSafe(Key As String, Optional Instance As McInstance = Nothing) As Object
        Return [Get](Key, Instance)
    End Function
    Public Shared Sub SetSafe(Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        [Set](Key, Value, Instance)
    End Sub
    Public Shared Function GetDefault(Key As String) As String
        Return CStr([Get](Key))
    End Function
    Public Shared Sub Reset(Key As String, Optional Instance As McInstance = Nothing)
        If Instance IsNot Nothing Then
            DeleteIniKey(Instance.PathVersion & "PCL\Setup.ini", Key)
        Else
            SyncLock SyncRoot
                Values().Remove(Key)
                If File.Exists(SettingsPath) Then File.WriteAllText(SettingsPath, Values().ToString(Formatting.Indented), New UTF8Encoding(False))
            End SyncLock
        End If
    End Sub
    Public Shared Function HasSaved(Key As String, Optional Instance As McInstance = Nothing) As Boolean
        If Instance IsNot Nothing Then Return HasIniKey(Instance.PathVersion & "PCL\Setup.ini", Key)
        Return Values().ContainsKey(Key)
    End Function
End Class
