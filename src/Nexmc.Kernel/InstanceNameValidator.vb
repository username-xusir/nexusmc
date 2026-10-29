Imports System.Text.RegularExpressions

Public Module InstanceNameValidator
    Private ReadOnly ReservedNames As String() = {"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"}

    Public Function ValidateInstanceName(value As String) As String
        If value Is Nothing Then Throw New ArgumentException("实例名不能为空")
        Dim name = value.Trim()
        If name.Length = 0 OrElse name.Length > 80 Then Throw New ArgumentException("实例名长度必须为 1–80 个字符")
        If name = "." OrElse name = ".." OrElse name.EndsWith(".") OrElse name.EndsWith(" ") Then Throw New ArgumentException("实例名不能为点、双点，也不能以点或空格结尾")
        If name.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 OrElse name.Contains("/") OrElse name.Contains("\") OrElse name.Contains("!") OrElse name.Contains(";") Then Throw New ArgumentException("实例名包含路径或 PCL 不支持的字符")
        If ReservedNames.Contains(name.ToUpperInvariant()) Then Throw New ArgumentException("实例名不能使用 Windows 保留设备名")
        If Regex.IsMatch(name, "\.\.") Then Throw New ArgumentException("实例名不能包含连续两点")
        Return name
    End Function

    Public Function ValidateMinecraftFolder(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Throw New ArgumentException("Minecraft 目录不能为空")
        Dim full = IO.Path.GetFullPath(value.Trim().TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar) & IO.Path.DirectorySeparatorChar)
        If full.Contains("!") OrElse full.Contains(";") Then Throw New ArgumentException("目标路径不能包含感叹号或分号")
        Return full
    End Function
End Module
