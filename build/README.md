# NEXMC 构建与打包

## 环境
- Windows 10/11 x64
- .NET 10 SDK（10.0.x，`dotnet` 在 PATH 中）
- Bun（仅构建资源与安装包）
- 首次构建：`bun install`

## 构建
```powershell
dotnet build NEXMC.sln -c Release
```

## 测试
```powershell
dotnet test tests\NEXMC.Tests\NEXMC.Tests.csproj -c Release
```

## 冒烟测试
```powershell
dotnet run --project src\Nexmc.Smoke\Nexmc.Smoke.csproj -c Release -- --install-fixture
```
该命令在 `artifacts/` 下创建独立目标目录，安装本地 Modrinth 格式 `.mrpack` 并核对版本 JSON、覆盖文件和游戏文件。其他夹具参数见 `src/Nexmc.Smoke/Program.cs`。

## 自包含发布
```powershell
dotnet publish src\NEXMC\NEXMC.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o publish\NEXMC
```
发布目录应包含 `NEXMC.exe`、.NET 运行时、`LICENSE`、`NOTICE`、`THIRD_PARTY_LICENSES.md`、`licenses/`。

## 离线安装包
```powershell
powershell -ExecutionPolicy Bypass -File build\package.ps1
```
产物：`dist/installer/NEXMC-Setup-1.0.8.exe`。

说明：NEXMC 本体是 .NET WPF 程序；这里只把 electron-builder 的 NSIS 目标当作通用 Windows 安装器打包器，安装内容完全是自包含 .NET 发布目录，不包含 Electron 运行时，也不打包 PCL2/HMCL。

## 验证
1. 在全新空目录运行安装包。
2. 启动 NEXMC，确认无残留子进程、窗口可正常关闭。
3. 打开版本列表与整合包搜索；至少验证一个不需要登录的下载来源。
4. 卸载并确认安装目录与快捷方式被移除。
5. 游戏目录、`%LOCALAPPDATA%\NEXMC` 设置不被卸载器删除。
