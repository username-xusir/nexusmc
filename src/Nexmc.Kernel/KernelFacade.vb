Public NotInheritable Class VersionInfo
    Public ReadOnly Id As String
    Public ReadOnly Type As String
    Public ReadOnly ReleaseTime As String
    Public Sub New(id As String, type As String, releaseTime As String)
        Me.Id = id : Me.Type = type : Me.ReleaseTime = releaseTime
    End Sub
End Class

Public NotInheritable Class LoaderInfo
    Public ReadOnly Kind As String
    Public ReadOnly Version As String
    Public Sub New(kind As String, version As String)
        Me.Kind = kind : Me.Version = version
    End Sub
End Class

Public NotInheritable Class ModpackInfo
    Public ReadOnly Id As String
    Public ReadOnly Name As String
    Public ReadOnly Description As String
    Public ReadOnly Source As String
    Public ReadOnly IconUrl As String
    Public ReadOnly Downloads As Long
    Public ReadOnly Website As String
    Public ReadOnly Tags As List(Of String)
    Public Sub New(id As String, name As String, description As String, source As String, iconUrl As String, downloads As Long, website As String, tags As List(Of String))
        Me.Id = id : Me.Name = name : Me.Description = description : Me.Source = source : Me.IconUrl = iconUrl : Me.Downloads = downloads
        Me.Website = If(website, "") : Me.Tags = If(tags, New List(Of String))
    End Sub
End Class

Public NotInheritable Class ModpackSearchPageInfo
    Public ReadOnly Projects As List(Of ModpackInfo)
    Public ReadOnly TotalHits As Integer
    Public Sub New(projects As List(Of ModpackInfo), totalHits As Integer)
        Me.Projects = projects : Me.TotalHits = totalHits
    End Sub
End Class

Public NotInheritable Class ModpackFileInfo
    Public ReadOnly Id As String
    Public ReadOnly Display As String
    Public ReadOnly GameVersion As String
    Public ReadOnly Source As String
    Public ReadOnly IsDirectDownload As Boolean
    ''' <summary>仅提供网盘时，条目自带的外部下载页（夸克、迅雷等）；没有则为空。</summary>
    Public ReadOnly ExternalUrl As String
    ''' <summary>可直接下载的文件地址；命中 CDN 人机验证时可让内嵌浏览器直接打开该地址。</summary>
    Public ReadOnly DirectUrl As String
    Public Sub New(id As String, display As String, gameVersion As String, source As String, isDirectDownload As Boolean, Optional externalUrl As String = "", Optional directUrl As String = "")
        Me.Id = id : Me.Display = display : Me.GameVersion = gameVersion : Me.Source = source : Me.IsDirectDownload = isDirectDownload
        Me.ExternalUrl = If(externalUrl, "")
        Me.DirectUrl = If(directUrl, "")
    End Sub
End Class

''' <summary>NEXMC 对 PCL 下载与安装编排的同步门面。UI 在任务线程调用。</summary>
Public NotInheritable Class KernelFacade
    Private Shared ReadOnly KernelGate As New Object
    ''' <summary>版本列表、搜索、整合包版本等只读元数据使用独立锁，避免被正在进行的安装长时间阻塞。</summary>
    Private Shared ReadOnly MetadataGate As New Object
    Private Shared _initialized As Boolean
    Private Shared ReadOnly PackFiles As New Dictionary(Of String, List(Of ResourceVersion))(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly PackExternalUrls As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly ForgeEntries As New Dictionary(Of String, List(Of DlForgeVersionEntry))(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly NeoEntries As New Dictionary(Of String, List(Of DlNeoForgeListEntry))(StringComparer.OrdinalIgnoreCase)

    Public Shared Sub Initialize()
        '安装期间 KernelGate 可能被长时间占用；已初始化时不再排队等锁。
        If System.Threading.Volatile.Read(_initialized) Then Return
        SyncLock KernelGate
            If System.Threading.Volatile.Read(_initialized) Then Return
            'PCL 仍会读写 GB18030 编码的旧文件；.NET 10 默认不注册代码页，必须先注册。
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)
            Dim data = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC")
            Directory.CreateDirectory(data)
            Directory.CreateDirectory(IO.Path.Combine(data, "Cache"))
            Directory.CreateDirectory(IO.Path.Combine(data, "ModpackCache"))
            FrmMain = New FormMain()
            PathTemp = IO.Path.Combine(data, "Cache") & IO.Path.DirectorySeparatorChar
            PathAppdata = data & IO.Path.DirectorySeparatorChar
            PathPure = IO.Path.Combine(data, "Cache") & IO.Path.DirectorySeparatorChar
            Directory.CreateDirectory(PathExeFolder & "PCL\")
            ServicePointManager.Expect100Continue = False
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            ServicePointManager.DefaultConnectionLimit = 10000
            LogStart()
            Log("[NEXMC] 内核初始化完成，基于 PCL 2.12.7.3 / 1b4d1296")
            System.Threading.Volatile.Write(_initialized, True)
        End SyncLock
    End Sub

    Public Shared Sub SetCurseForgeKey(value As String)
        ModSecret.CurseForgeAPIKey = If(value, "")
    End Sub

    ''' <summary>应用游戏下载线路、版本列表线路、线程数与限速；失败时内核仍会在各线路间切换。</summary>
    Public Shared Sub ApplyDownloadPreferences(downloadSource As Integer, versionSource As Integer, threadCount As Integer, speedSetting As Integer)
        '设置写入不需要等正在进行的安装释放 KernelGate，否则“保存设置”会像没反应一样卡住。
        SyncLock MetadataGate
            Initialize()
            Settings.Set("ToolDownloadSource", Math.Clamp(downloadSource, 0, 2))
            Settings.Set("ToolDownloadVersion", Math.Clamp(versionSource, 0, 2))
            Settings.Set("ToolDownloadThread", Math.Clamp(threadCount - 1, 0, 63))
            Settings.Set("ToolDownloadSpeed", Math.Clamp(speedSetting, 0, 42))
            ModNet.UpdateNetTaskSpeedLimitHigh(0)
        End SyncLock
    End Sub

    ''' <summary>诊断用：返回内核为某个资源地址生成的下载源顺序。</summary>
    Public Shared Function GetAssetSources(url As String) As String()
        SyncLock MetadataGate
            Initialize()
            Return DlSourceAssetsGet(url).ToArray()
        End SyncLock
    End Function

    Public Shared Function GetMinecraftVersions(includeSnapshots As Boolean) As List(Of VersionInfo)
        SyncLock MetadataGate
            Initialize()
            DlClientListLoader.WaitForExit("", IsForceRestart:=True)
            Dim result As New List(Of VersionInfo)
            For Each item As JObject In DlClientListLoader.Output.Value("versions")
                Dim type = item("type").ToString()
                If Not includeSnapshots AndAlso type <> "release" Then Continue For
                result.Add(New VersionInfo(item("id").ToString(), type, item("releaseTime").ToString()))
            Next
            Return result
        End SyncLock
    End Function

    Public Shared Function GetLoaders(minecraftVersion As String, kind As String) As List(Of LoaderInfo)
        SyncLock MetadataGate
            Initialize()
            Dim result As New List(Of LoaderInfo)
            Select Case kind.ToLowerInvariant()
                Case "forge"
                    Dim task As New LoaderTask(Of String, List(Of DlForgeVersionEntry))("NEXMC Forge 版本列表", AddressOf DlForgeVersionMain)
                    task.WaitForExit(minecraftVersion, IsForceRestart:=True)
                    ForgeEntries(minecraftVersion) = task.Output.Where(Function(e) e.Inherit = minecraftVersion).ToList()
                    result.AddRange(ForgeEntries(minecraftVersion).Select(Function(e) New LoaderInfo(kind, e.VersionName)))
                Case "neoforge"
                    DlNeoForgeListLoader.WaitForExit(IsForceRestart:=True)
                    NeoEntries(minecraftVersion) = DlNeoForgeListLoader.Output.Value.Where(Function(e) e.Inherit = minecraftVersion).ToList()
                    result.AddRange(NeoEntries(minecraftVersion).Select(Function(e) New LoaderInfo(kind, e.VersionName)))
                Case "fabric"
                    DlFabricListLoader.WaitForExit(IsForceRestart:=True)
                    For Each entry As JObject In DlFabricListLoader.Output.Value("loader")
                        result.Add(New LoaderInfo(kind, entry("version").ToString()))
                    Next
                Case "", "none", "vanilla"
                    '原版不需要加载器。
                Case Else
                    Throw New ArgumentException("不支持的加载器：" & kind)
            End Select
            Return result
        End SyncLock
    End Function

    Public Shared Function SearchModpacks(query As String, source As String, gameVersion As String) As List(Of ModpackInfo)
        Return SearchModpacksPage(query, source, gameVersion, "", 0, 40).Projects
    End Function

    Public Shared Function SearchModpacksPage(query As String, source As String, gameVersion As String, category As String, offset As Integer, limit As Integer) As ModpackSearchPageInfo
        SyncLock MetadataGate
            Initialize()
            Dim safeOffset = Math.Max(0, offset)
            Dim safeLimit = Math.Clamp(limit, 1, 200)

            If source.Equals("BBSMC", StringComparison.OrdinalIgnoreCase) Then
                Dim projects As New List(Of ModpackInfo)
                Dim totalHits = 0
                Dim requestOffset = safeOffset
                While projects.Count < safeLimit
                    Dim take = Math.Min(100, safeLimit - projects.Count)
                    Dim facets As New JArray()
                    facets.Add(New JArray("project_type:modpack"))
                    Dim bbsmcCategory = GetBbsmcCategory(category)
                    If Not String.IsNullOrWhiteSpace(bbsmcCategory) Then facets.Add(New JArray($"categories:'{bbsmcCategory}'"))
                    If Not String.IsNullOrWhiteSpace(gameVersion) Then facets.Add(New JArray($"versions:'{gameVersion}'"))
                    Dim address = $"https://api.bbsmc.net/v2/search?limit={take}&offset={requestOffset}&index=relevance"
                    If Not String.IsNullOrWhiteSpace(query) Then address &= "&query=" & Uri.EscapeDataString(query)
                    address &= "&facets=" & Uri.EscapeDataString(facets.ToString(Newtonsoft.Json.Formatting.None))
                    Dim response As JObject = DlModRequest(address)
                    totalHits = If(response("total_hits")?.ToObject(Of Integer)(), 0)
                    Dim hits = CType(If(response("hits"), New JArray), JArray)
                    If hits.Count = 0 Then Exit While
                    For Each hit In hits
                        Dim slug = hit("slug")?.ToString()
                        Dim projectId = hit("project_id").ToString()
                        Dim website = If(String.IsNullOrWhiteSpace(slug), $"https://bbsmc.net/project/{projectId}", $"https://bbsmc.net/modpack/{slug}")
                        Dim tagToken = If(hit("display_categories"), hit("categories"))
                        Dim tags = If(tagToken Is Nothing, New List(Of String), tagToken.Select(Function(t) t.ToString()).ToList())
                        projects.Add(New ModpackInfo(projectId, hit("title").ToString(), hit("description").ToString(), "BBSMC",
                            hit("icon_url")?.ToString(), hit("downloads").ToObject(Of Long)(), website, tags))
                    Next
                    requestOffset += hits.Count
                    If requestOffset >= totalHits Then Exit While
                End While
                Return New ModpackSearchPageInfo(projects, totalHits)
            End If

            Dim storage As New ResourceSearcher.SearchResult
            Dim request As New ResourceSearcher.SearchRequest(ResourceTypes.ModPack, storage, Math.Max(safeOffset + safeLimit, 40)) With {
                .SearchText = If(query, ""),
                .GameVersion = If(String.IsNullOrWhiteSpace(gameVersion), Nothing, gameVersion),
                .Tag = GetResourceCategoryTag(category)
            }
            Dim platform = If(source.Equals("CurseForge", StringComparison.OrdinalIgnoreCase), ResourcePlatforms.CurseForge, ResourcePlatforms.Modrinth)
            If platform = ResourcePlatforms.CurseForge AndAlso String.IsNullOrWhiteSpace(ModSecret.CurseForgeAPIKey) Then
                Settings.Set("ToolDownloadMod", 0) '无 Key 时使用 PCL 原有 MCIMirror 镜像。
            End If
            request.Sources = platform
            Dim loader = ResourceSearcher.GetLoader(Function() request)
            Try
                loader.WaitForExit(IsForceRestart:=True)
            Catch ex As Exception When ex.GetBaseException().Message.Contains("没有搜索结果")
                Return New ModpackSearchPageInfo(New List(Of ModpackInfo), 0)
            End Try
            Dim all = storage.Results.Select(Function(p) New ModpackInfo(p.Id, p.RawName, p.Description, source, p.LogoUrl, p.DownloadCount,
                p.Website, If(p.Tags, New List(Of String)))).ToList()
            Dim total = If(platform = ResourcePlatforms.Modrinth, storage.ModrinthTotal, storage.CurseForgeTotal)
            If total < 0 Then total = all.Count
            total = Math.Max(total, all.Count)
            Return New ModpackSearchPageInfo(all.Skip(safeOffset).Take(safeLimit).ToList(), total)
        End SyncLock
    End Function

    Private Shared Function GetResourceCategoryTag(category As String) As String
        Select Case If(category, "").Trim()
            Case "冒险" : Return "4475/adventure"
            Case "科技" : Return "4472/technology"
            Case "魔法" : Return "4473/magic"
            Case "优化" : Return "6814/optimization"
            Case "战斗" : Return "4483/combat"
            Case "任务" : Return "4478/quests"
            Case "多人" : Return "4484/multiplayer"
            Case "硬核" : Return "4479/challenging"
            Case "轻量" : Return "4481/lightweight"
            Case "空岛" : Return "4736/skyblock"
            Case Else : Return ""
        End Select
    End Function

    Private Shared Function GetBbsmcCategory(category As String) As String
        Select Case If(category, "").Trim()
            Case "多人" : Return "多人游戏"
            Case "硬核" : Return "挑战"
            Case Else : Return If(category, "").Trim()
        End Select
    End Function

    Public Shared Function GetModpackFiles(projectId As String, source As String) As List(Of ModpackFileInfo)
        SyncLock MetadataGate
            Initialize()
            Dim key = source.ToLowerInvariant() & ":" & projectId
            If Not PackFiles.ContainsKey(key) Then
                If source.Equals("BBSMC", StringComparison.OrdinalIgnoreCase) Then
                    Dim response As JArray = DlModRequest($"https://api.bbsmc.net/v2/project/{Uri.EscapeDataString(projectId)}/version")
                    PackFiles(key) = response.Select(Function(item) ResourceVersion.FromPlatformJson(item, ResourceTypes.ModPack)).ToList()
                    'BBSMC 的 disk_urls 不在 PCL 的 Modrinth 解析范围内，单独保留，供仅网盘版本跳转。
                    For Each raw In response
                        Dim item = TryCast(raw, JObject)
                        If item Is Nothing Then Continue For
                        Dim idToken = item("id")
                        Dim versionId = If(idToken Is Nothing, "", idToken.ToString())
                        Dim external = GetBbsmcExternalUrl(item)
                        If Not String.IsNullOrWhiteSpace(versionId) AndAlso Not String.IsNullOrWhiteSpace(external) Then
                            PackExternalUrls(key & "|" & versionId) = external
                        End If
                    Next
                Else
                    Dim platform = If(source.Equals("CurseForge", StringComparison.OrdinalIgnoreCase), ResourcePlatforms.CurseForge, ResourcePlatforms.Modrinth)
                    PackFiles(key) = ResourceVersion.FromProjectId(projectId, platform, LoadDependencies:=False)
                End If
            End If
            Dim result As New List(Of ModpackFileInfo)
            For Each file In PackFiles(key)
                Dim external As String = Nothing
                PackExternalUrls.TryGetValue(key & "|" & file.Id, external)
                If String.IsNullOrWhiteSpace(external) Then external = FirstCloudDriveUrl(file.DownloadUrls)
                Dim isDirect = IsRealModpackFile(file)
                If Not isDirect Then
                    file.FileName = Nothing
                    file.DownloadUrls = Nothing
                End If
                result.Add(New ModpackFileInfo(file.Id, file.Display, file.GameVersions.Join("、"), source, isDirect, external))
            Next
            Return result
        End SyncLock
    End Function

    Public Shared Sub InstallVanilla(instanceName As String, minecraftVersion As String, loaderKind As String, loaderVersion As String, targetFolder As String, report As Action(Of Double, String), cancellation As Threading.CancellationToken)
        SyncLock KernelGate
            Initialize()
            Dim name = ValidateInstanceName(instanceName)
            Dim root = ValidateMinecraftFolder(targetFolder)
            McFolderSelected = root
            Dim versionFolder = IO.Path.Combine(root, "versions", name) & IO.Path.DirectorySeparatorChar
            EnsureNewVersion(versionFolder)
            Dim request As New McInstallRequest With {.NewInstanceName = name, .VersionFolder = versionFolder, .MinecraftName = minecraftVersion}
            If Not String.IsNullOrWhiteSpace(loaderKind) AndAlso Not loaderKind.Equals("none", StringComparison.OrdinalIgnoreCase) Then
                If Not ForgeEntries.ContainsKey(minecraftVersion) AndAlso Not NeoEntries.ContainsKey(minecraftVersion) Then GetLoaders(minecraftVersion, loaderKind)
                Select Case loaderKind.ToLowerInvariant()
                    Case "forge"
                        request.ForgeEntry = ForgeEntries(minecraftVersion).FirstOrDefault(Function(e) e.VersionName = loaderVersion)
                        If request.ForgeEntry Is Nothing Then Throw New ArgumentException("未找到所选 Forge 版本，请刷新加载器列表")
                        request.ForgeVersion = request.ForgeEntry.VersionName
                    Case "neoforge"
                        request.NeoForgeEntry = NeoEntries(minecraftVersion).FirstOrDefault(Function(e) e.VersionName = loaderVersion)
                        If request.NeoForgeEntry Is Nothing Then Throw New ArgumentException("未找到所选 NeoForge 版本，请刷新加载器列表")
                        request.NeoForgeVersion = request.NeoForgeEntry.VersionName
                    Case "fabric"
                        request.FabricVersion = loaderVersion
                    Case Else
                        Throw New ArgumentException("不支持的加载器：" & loaderKind)
                End Select
            End If
            Dim combo As New LoaderCombo(Of String)("NEXMC · " & name & " 安装", McInstallLoader(request)) With {.OnStateChanged = AddressOf McInstallState}
            combo.Start(versionFolder)
            RunLoader(combo, report, cancellation)
        End SyncLock
    End Sub

    Public Shared Function DownloadModpackArchive(projectId As String, versionId As String, source As String, Optional report As Action(Of Double, String) = Nothing, Optional cancellation As Threading.CancellationToken = Nothing) As String
        SyncLock KernelGate
            Initialize()
            If report Is Nothing Then report = Sub(value, message) System.Diagnostics.Debug.WriteLine(message)
            If cancellation = Nothing Then cancellation = Threading.CancellationToken.None
            report(0.02, "获取整合包文件")
            Dim key = source.ToLowerInvariant() & ":" & projectId
            Dim file As ResourceVersion = Nothing
            SyncLock MetadataGate
                If Not PackFiles.ContainsKey(key) Then GetModpackFiles(projectId, source)
                file = PackFiles(key).FirstOrDefault(Function(f) f.Id = versionId)
            End SyncLock
            If file Is Nothing OrElse Not IsRealModpackFile(file) Then Throw New InvalidOperationException("该版本没有可直接下载的整合包文件（可能仅提供网盘）")
            If IO.Path.GetFileName(file.FileName) <> file.FileName OrElse file.FileName.Contains("/") OrElse file.FileName.Contains("\") Then Throw New InvalidOperationException("整合包文件名包含非法路径")
            If projectId.Any(Function(c) Not (Char.IsLetterOrDigit(c) OrElse c = "-"c OrElse c = "_"c)) Then Throw New InvalidOperationException("整合包项目 ID 包含非法路径字符")
            Dim sourceKey = If(source.Equals("BBSMC", StringComparison.OrdinalIgnoreCase), "bbsmc", source.ToLowerInvariant())
            Dim cacheFolder = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC", "ModpackCache", sourceKey & "-" & projectId)
            Directory.CreateDirectory(cacheFolder)
            Dim archive = IO.Path.Combine(cacheFolder, file.FileName)
            Dim download As New LoaderDownload("下载整合包 " & file.Display, New List(Of NetFile) From {file.ToNetFile(archive)})
            Dim combo As New LoaderCombo(Of String)("NEXMC · 下载整合包", New List(Of LoaderBase) From {download})
            combo.Start(archive)
            Using registration = cancellation.Register(Sub() RunInThread(AddressOf combo.Interrupt))
                RunLoader(combo, Sub(value, message) report(value * 0.2, message), cancellation)
            End Using
            Return archive
        End SyncLock
    End Function

    Public Shared Sub InstallModpack(instanceName As String, projectId As String, versionId As String, source As String, localPath As String, targetFolder As String, report As Action(Of Double, String), cancellation As Threading.CancellationToken)
        SyncLock KernelGate
            Initialize()
            Dim name = ValidateInstanceName(instanceName)
            Dim root = ValidateMinecraftFolder(targetFolder)
            McFolderSelected = root
            Dim versionFolder = IO.Path.Combine(root, "versions", name) & IO.Path.DirectorySeparatorChar
            EnsureNewVersion(versionFolder)
            Dim archive = localPath
            If String.IsNullOrWhiteSpace(archive) Then
                archive = DownloadModpackArchive(projectId, versionId, source, report, cancellation)
            Else
                archive = IO.Path.GetFullPath(archive)
                If Not File.Exists(archive) Then Throw New FileNotFoundException("整合包文件不存在", archive)
            End If
            report(0.2, "解析整合包并安装")
            Dim loader = ModpackInstall(archive, name)
            RunLoader(loader, Sub(value, message) report(0.2 + value * 0.8, message), cancellation)
        End SyncLock
    End Sub

    ''' <summary>BBSMC 版本 JSON 里的网盘链接；优先 disk_urls，其次 files 中的网盘地址。</summary>
    Private Shared Function GetBbsmcExternalUrl(item As JObject) As String
        Dim candidates As New List(Of String)
        Dim disks = TryCast(item("disk_urls"), JArray)
        If disks IsNot Nothing Then
            For Each raw In disks
                Dim entry = TryCast(raw, JObject)
                If entry Is Nothing Then Continue For
                Dim token = entry("url")
                Dim url = If(token Is Nothing, "", token.ToString()).Trim()
                If IsCloudDriveUrl(url) Then candidates.Add(url)
            Next
        End If
        Dim files = TryCast(item("files"), JArray)
        If files IsNot Nothing Then
            For Each raw In files
                Dim entry = TryCast(raw, JObject)
                If entry Is Nothing Then Continue For
                Dim token = entry("url")
                Dim url = If(token Is Nothing, "", token.ToString()).Trim()
                If IsCloudDriveUrl(url) Then candidates.Add(url)
            Next
        End If
        Dim cloud = candidates.FirstOrDefault(AddressOf IsCloudDriveUrl)
        If Not String.IsNullOrWhiteSpace(cloud) Then Return cloud
        Return If(candidates.FirstOrDefault(), "")
    End Function

    Private Shared Function FirstCloudDriveUrl(urls As IEnumerable(Of String)) As String
        If urls Is Nothing Then Return ""
        Return If(urls.FirstOrDefault(Function(url) IsCloudDriveUrl(url)), "")
    End Function

    Public Shared Function IsCloudDriveUrl(url As String) As Boolean
        If String.IsNullOrWhiteSpace(url) Then Return False
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(url.Trim(), UriKind.Absolute, uri) Then Return False
        Dim host = uri.Host.ToLowerInvariant()
        Return host = "pan.quark.cn" OrElse host.EndsWith(".quark.cn") OrElse
               host = "pan.xunlei.com" OrElse host.EndsWith(".xunlei.com") OrElse
               host = "pan.baidu.com" OrElse host = "yun.baidu.com" OrElse
               host = "www.aliyundrive.com" OrElse host = "www.alipan.com" OrElse
               host.EndsWith("123pan.com") OrElse host.EndsWith("123684.com") OrElse
               host.EndsWith("lanzou.com") OrElse host.EndsWith("lanzoui.com") OrElse
               host.EndsWith("lanzoux.com") OrElse host.EndsWith("lanzouy.com")
    End Function

    Private Shared Function IsRealModpackFile(file As ResourceVersion) As Boolean
        If file Is Nothing OrElse String.IsNullOrWhiteSpace(file.FileName) OrElse file.Size <= 0 OrElse file.DownloadUrls Is Nothing Then Return False
        Dim extension = IO.Path.GetExtension(file.FileName)
        If Not {".zip", ".mrpack"}.Contains(extension, StringComparer.OrdinalIgnoreCase) Then Return False
        For Each candidate In file.DownloadUrls
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(candidate, UriKind.Absolute, uri) Then Continue For
            If uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps Then Continue For
            Dim host = uri.Host.ToLowerInvariant()
            If host = "pan.quark.cn" OrElse host.EndsWith(".quark.cn") OrElse host = "pan.xunlei.com" OrElse host.EndsWith(".xunlei.com") Then Continue For
            Return True
        Next
        Return False
    End Function

    Private Shared Sub EnsureNewVersion(versionFolder As String)
        If File.Exists(IO.Path.Combine(versionFolder, IO.Path.GetFileName(versionFolder.TrimEnd(IO.Path.DirectorySeparatorChar)) & ".json")) Then
            Throw New InvalidOperationException("目标中已存在同名版本，请更换实例名：" & versionFolder)
        End If
        If Directory.Exists(versionFolder) AndAlso Directory.EnumerateFileSystemEntries(versionFolder).Any() Then
            Throw New InvalidOperationException("目标版本目录不为空。为防止覆盖现有文件，请更换实例名：" & versionFolder)
        End If
        Directory.CreateDirectory(IO.Path.GetDirectoryName(versionFolder.TrimEnd(IO.Path.DirectorySeparatorChar)))
    End Sub

    Private Shared Sub RunLoader(loader As LoaderCombo(Of String), report As Action(Of Double, String), cancellation As Threading.CancellationToken)
        Dim lastProgress = -1.0
        Using registration = cancellation.Register(Sub() RunInThread(AddressOf loader.Interrupt))
            Do While loader.State = LoadState.Loading OrElse loader.State = LoadState.Waiting
                cancellation.ThrowIfCancellationRequested()
                Dim current = Math.Max(0, Math.Min(1, loader.Progress))
                If Math.Abs(current - lastProgress) >= 0.005 Then
                    report(current, loader.Name)
                    lastProgress = current
                End If
                Thread.Sleep(100)
            Loop
        End Using
        Try
            cancellation.ThrowIfCancellationRequested()
            If loader.State = LoadState.Finished Then
                report(1, "安装完成")
            ElseIf loader.State = LoadState.Interrupted Then
                Throw New OperationCanceledException("安装已取消")
            Else
                Dim detail = If(loader.Error Is Nothing, "未知错误", loader.Error.GetDetail())
                Throw New InvalidOperationException("安装失败：" & detail, loader.Error)
            End If
        Finally
            'NEXMC 不运行 PCL 的 UI 时钟；必须主动移出全局任务栏，否则第二个目标会被误判为重复任务。
            LoaderTaskbar.Remove(loader)
        End Try
    End Sub
End Class
