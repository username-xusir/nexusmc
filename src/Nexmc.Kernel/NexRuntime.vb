' Minimal UI/runtime contract required by the unmodified download pipeline.
Public Module NexRuntime
    Public AniControlEnabled As Integer = 1
    Public AniSpeed As Double = 1
    Public FrmMain As FormMain
    Public FrmSpeedLeft As PageSpeedLeft
    Public FrmSetupLaunch As PageSetupLaunch
    Public FrmInstanceSetup As PageInstanceSetup

    Public Enum HintType
        Blue
        Green
        Red
    End Enum

    Public Sub Hint(Text As String, Optional Type As HintType = HintType.Blue, Optional Log As Boolean = True)
        If Log Then ModBase.Log("[NEXMC] " & Text)
    End Sub

    Public Function MyMsgBox(Caption As String, Optional Title As String = "提示", Optional Button1 As String = "确定", Optional Button2 As String = "", Optional Button3 As String = "", Optional IsWarn As Boolean = False, Optional HighLight As Boolean = True, Optional ForceWait As Boolean = False, Optional Button1Action As Action = Nothing, Optional Button2Action As Action = Nothing, Optional Button3Action As Action = Nothing) As Integer
        Return RunInUiWait(Function()
                               Dim buttons = If(Button3 <> "", MessageBoxButton.YesNoCancel, If(Button2 <> "", MessageBoxButton.YesNo, MessageBoxButton.OK))
                               Dim icon = If(IsWarn, MessageBoxImage.Warning, MessageBoxImage.Information)
                               Dim result = MessageBox.Show(Caption, "NEXMC · " & Title, buttons, icon)
                               Select Case result
                                   Case MessageBoxResult.Yes, MessageBoxResult.OK : Return 1
                                   Case MessageBoxResult.No : Return 2
                                   Case Else : Return 3
                               End Select
                           End Function)
    End Function

    Public Function MyMsgBoxInput(Title As String, Optional Text As String = Nothing, Optional DefaultInput As String = Nothing, Optional ValidateRules As ObjectModel.Collection(Of Validate) = Nothing, Optional HintText As String = "", Optional Button1 As String = "确定", Optional Button2 As String = "取消", Optional IsWarn As Boolean = False) As String
        Return RunInUiWait(Function() Microsoft.VisualBasic.Interaction.InputBox(Text, "NEXMC · " & Title, DefaultInput))
    End Function

    Public Function MyMsgBoxSelect(Selections As IEnumerable(Of IMyRadio), Optional Title As String = "提示", Optional Button1 As String = "确定", Optional Button2 As String = "", Optional IsWarn As Boolean = False) As Integer?
        Return MyMsgBox("请确认当前选择。", Title, Button1, Button2, IsWarn:=IsWarn) - 1
    End Function

    Public Sub LoaderTaskbarAddPlaceholder(Of T)(Loader As LoaderCombo(Of T))
        Log("[NEXMC] 任务已加入：" & Loader.Name)
    End Sub
End Module

Public Enum McLoginType
    Legacy = 0
    Nide = 2
    Auth = 3
    Ms = 5
End Enum

Public Interface ILoadingTrigger
    ReadOnly Property IsLoader As Boolean
    Property LoadingState As MyLoading.MyLoadingState
    Event LoadingStateChanged(NewState As MyLoading.MyLoadingState, OldState As MyLoading.MyLoadingState)
    Event ProgressChanged(NewProgress As Double, OldProgress As Double)
End Interface

Public Class MyLoading
    Public Enum MyLoadingState
        Unloaded = -1
        Run = 0
        [Stop] = 1
        [Error] = 2
    End Enum
End Class

Public Interface IMyRadio
    Event Check(sender As Object, e As RouteEventArgs)
    Event Changed(sender As Object, e As RouteEventArgs)
End Interface

Public Class MyVirtualizingElement(Of T As FrameworkElement)
    Inherits FrameworkElement
    Private ReadOnly Initializer As Func(Of T)
    Public Sub New(initializer As Func(Of T))
        Me.Initializer = initializer
    End Sub
    Public Function Init() As T
        Return Initializer()
    End Function
    Public Shared Widening Operator CType(value As MyVirtualizingElement(Of T)) As T
        Return value.Init()
    End Operator
End Class

Public Class MyImage
    Inherits Image
    Public FallbackSource As String
    Private _source As String
    Public Shadows Property Source As String
        Get
            Return _source
        End Get
        Set(value As String)
            _source = value
            If String.IsNullOrEmpty(value) OrElse value.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then Return
            Try
                MyBase.Source = New BitmapImage(New Uri(value, UriKind.RelativeOrAbsolute))
            Catch ex As Exception
                Log(ex, "NEXMC 兼容图片加载失败")
            End Try
        End Set
    End Property
End Class

Public Class MyIconButton
    Inherits Button
    Public Logo As String
    Public LogoScale As Double = 1
    Public Progress As Double
    Public Shadows Event Click(sender As Object, e As EventArgs)
    Public Sub ShowRefresh()
    End Sub
    Public Sub Ribble()
    End Sub
End Class

Public Class MyListItem
    Inherits Grid
    Implements IMyRadio
    Public Enum CheckType
        None
        Clickable
        CheckBox
        RadioBox
    End Enum
    Public Type As CheckType
    Public Title As String
    Public Shadows Property FontSize As Double
    Public Info As String
    Public Logo As String
    Public LogoScale As Double = 1
    Public LogoClickable As Boolean
    Public IsScaleAnimationEnabled As Boolean = True
    Public Buttons As IEnumerable(Of MyIconButton)
    Public ContentHandler As Action(Of MyListItem, EventArgs)
    Public ReadOnly Inlines As New List(Of Documents.Inline)
    Public Event Click(sender As Object, e As MouseButtonEventArgs)
    Public Event Check(sender As Object, e As RouteEventArgs) Implements IMyRadio.Check
    Public Event Changed(sender As Object, e As RouteEventArgs) Implements IMyRadio.Changed
End Class

Public Class MyResourceItem
    Inherits MyListItem
    Public PathLogo As New MyImage
    Public LabTitleRaw As New TextBlock
    Public LabSource As New TextBlock
    Public LabTime As New TextBlock
    Public LabDownload As New TextBlock
    Public LabVersion As New TextBlock
    Public PathVersion As New Grid
    Public ColumnVersion1 As New ColumnDefinition
    Public ColumnVersion2 As New ColumnDefinition
    Public ColumnVersion3 As New ColumnDefinition
    Public SubTitle As String
    Public Tags As List(Of String)
    Public Description As String
    Public Sub New()
        Dim host As New StackPanel
        host.Children.Add(LabTitleRaw)
        Dim title As New TextBlock
        host.Children.Add(title)
        Children.Add(host)
        PathVersion.Children.Add(LabVersion)
        Children.Add(PathVersion)
    End Sub
End Class

Public Class FormMain
    Inherits Window
    Public Enum PageType
        DownloadManager
        Download
        InstanceSelect
    End Enum
    Public Enum PageSubType
        DownloadInstall
    End Enum
    Public BtnExtraDownload As New MyIconButton With {.Progress = 0}
    Public PanTitle As New Border
    Public PanForm As New Border
    Public Sub New()
        TaskbarItemInfo = New Shell.TaskbarItemInfo
    End Sub
    Public Sub PageChange(page As PageType, Optional subPage As PageSubType? = Nothing)
    End Sub
    Public Sub EndProgram(force As Boolean)
    End Sub
    Public Shared Sub EndProgramForce(value As Integer)
        Environment.Exit(value)
    End Sub
End Class

Public Class PageSpeedLeft
    Public Sub TaskRemove(loader As LoaderBase)
    End Sub
    Public Sub TaskRefresh(loader As LoaderBase)
    End Sub
End Class
Public Class PageSetupLaunch
    Public ComboArgumentJava As New ComboBox
    Public Sub UpdateJavaComboBox()
    End Sub
End Class
Public Class PageInstanceSetup
    Public ComboArgumentJava As New ComboBox
    Public Sub RefreshJavaComboBox()
    End Sub
End Class
Public Class PageSetupSystem
    Public Shared ReadOnly Property IsLauncherNewest As Boolean = True
End Class
Public Module NexUpdateStub
End Module
