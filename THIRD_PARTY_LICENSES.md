# 第三方组件与许可登记（2026-10-29）

| 组件 | 固定版本 / 来源 | 许可 | 随包许可文件 |
|---|---|---|---|
| PCL 下载核心 | 2.12.7.3 / `1b4d1296` | 上游 `LICENCE` 为保留权利指南；维护者声明已获 PCL 作者对本次使用的单独许可，凭证由维护者保存；保留源码与署名 | `licenses/PCL-LICENCE.txt` |
| MeloongCore | `1910e965` | 随 PCL 子模块使用，保留来源与源码 | `licenses/PCL-LICENCE.txt` |
| PCL 随附运行时资源 | `moddata.zip`、`forge-installer.jar`、`transformer.jar`、`java-wrapper.jar`、`lwjgl-unsafe-agent.jar` | 随 PCL 组件按其许可/授权使用，保留来源 | `licenses/PCL-LICENCE.txt` |
| .NET 运行时 | Microsoft .NET 10.0.0 | Microsoft .NET Library License | `licenses/dotnet-LICENSE.txt` |
| WPF-UI | 4.3.0 | MIT | `licenses/WPF-UI-LICENSE.txt` |
| Microsoft.Web.WebView2 | 1.0.4314.35 | BSD-3-Clause（随包 LICENSE/NOTICE） | `licenses/WebView2-LICENSE.txt`、`licenses/WebView2-NOTICE.txt` |
| Newtonsoft.Json | 13.0.4 | MIT | `licenses/Newtonsoft-Json-MIT.txt` |
| CacheCow.Client / Common / FileStore | 2.13.1 | MIT | `licenses/CacheCow-MIT.txt` |
| Imazen.WebP / libwebp | 10.0.1 | MIT（包装层）；libwebp 为 BSD-3-Clause | `licenses/Imazen-WebP-MIT.txt`、`licenses/libwebp-BSD-3-Clause.txt` |
| Ookii.Dialogs.Wpf | PCL 固定二进制 | BSD-3-Clause | `licenses/Ookii-Dialogs-BSD-3-Clause.txt` |
| System.Net.Http.Formatting | 5.2.7 | Microsoft .NET Library License | `licenses/dotnet-LICENSE.txt` |
| System.Drawing.Common / Management / Runtime.Caching / ServiceModel.Primitives | NuGet 锁定版本 | MIT | `licenses/Dotnet-Foundation-MIT.txt` |
| Microsoft.Extensions.ObjectPool | 8.0.10 | MIT | `licenses/Dotnet-Foundation-MIT.txt` |
| BBSMC API | https://api.bbsmc.net/v2 | 仅调用公开接口，不复制网站代码或素材 | 无 |
| 名人堂头像 | 由 huogoooo、龙腾猫跃、username-xusir 提供 | 仅用于软件内鸣谢页，不用于其他用途 | 无 |
| HMCL | v3.16.4（仅分析） | GPL-3.0-or-later；不包含在安装包中 | 无 |

构建期工具（不进入运行时）：Bun、Electron Builder、Sharp、png-to-ico。完整依赖版本在 `bun.lock` 与各 `packages.lock.json` 中。
