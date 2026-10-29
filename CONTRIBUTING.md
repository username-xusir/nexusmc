# Contributing

感谢你愿意帮助 NEXMC。

## 开发环境

- Windows 10 / 11 x64
- .NET 10 SDK（10.0.x）
- Bun（仅打包安装程序时需要）

```powershell
dotnet restore NEXMC.sln
dotnet build NEXMC.sln -c Release
dotnet test tests/NEXMC.Tests/NEXMC.Tests.csproj -c Release
```

## 提交前

1. 保证 `dotnet build` 和 `dotnet test` 通过。
2. 不要提交 `bin/`、`obj/`、`publish/`、`dist/`、`node_modules/`、`tools/`、`references/`、`artifacts/`。
3. 不要提交任何启动器本体、游戏账号、Cookie、API Key 或个人路径。
4. 修改 PCL 上游组件时，先确认 `licenses/PCL-LICENCE.txt` 的授权范围，并在 PR 中说明改动。
5. 不要绕过 BBSMC、CurseForge 或 Modrinth 的人机验证、登录或访问限制。

## 提交信息

建议使用简短的中文或英文说明，例如：

```text
fix: 保存设置不再等待安装锁
feat: 整合包来源多选
docs: 补充构建说明
```

## 许可证

提交代码即表示你同意按根目录的 MIT License 授权你原创的部分；PCL 上游代码仍遵循其原有许可与署名要求。
