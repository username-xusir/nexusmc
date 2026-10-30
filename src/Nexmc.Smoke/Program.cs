using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NEXMC.Services;
using PCL;

namespace Nexmc.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        bool installFixture = args.Contains("--install-fixture", StringComparer.OrdinalIgnoreCase);
        bool listLaunchers = args.Contains("--launchers", StringComparer.OrdinalIgnoreCase);
        bool searchCategories = args.Contains("--search-categories", StringComparer.OrdinalIgnoreCase);
        bool forgeFixture = args.Contains("--forge-fixture", StringComparer.OrdinalIgnoreCase);
        bool encodingFixture = args.Contains("--encoding-fixture", StringComparer.OrdinalIgnoreCase);
        bool allOfCreateFixture = args.Contains("--allofcreate-fixture", StringComparer.OrdinalIgnoreCase);
        bool vanillaFixture = args.Contains("--vanilla-fixture", StringComparer.OrdinalIgnoreCase);
        bool modrinthFixture = args.Contains("--modrinth-fixture", StringComparer.OrdinalIgnoreCase);
        bool allSources = args.Contains("--all-sources", StringComparer.OrdinalIgnoreCase);
        bool retryFixture = args.Contains("--retry-fixture", StringComparer.OrdinalIgnoreCase);
        bool bbsmcFixture = args.Contains("--bbsmc-fixture", StringComparer.OrdinalIgnoreCase);
        bool netdiskFixture = args.Contains("--netdisk-fixture", StringComparer.OrdinalIgnoreCase);
        bool iconFixture = args.Contains("--icon-fixture", StringComparer.OrdinalIgnoreCase);
        bool assetSourcesFixture = args.Contains("--asset-sources", StringComparer.OrdinalIgnoreCase);
        bool dualFixture = args.Contains("--dual-fixture", StringComparer.OrdinalIgnoreCase);
        bool downloadOnlyFixture = args.Contains("--download-only-fixture", StringComparer.OrdinalIgnoreCase);
        bool concurrentMetadataFixture = args.Contains("--concurrent-metadata", StringComparer.OrdinalIgnoreCase);
        bool fabricFixture = args.Contains("--fabric-fixture", StringComparer.OrdinalIgnoreCase);
        bool neoforgeFixture = args.Contains("--neoforge-fixture", StringComparer.OrdinalIgnoreCase);
        bool mirrorFixture = args.Contains("--mirror-fixture", StringComparer.OrdinalIgnoreCase);
        try
        {
            KernelFacade.Initialize();
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    exitCode = await Task.Run(() => mirrorFixture ? InstallMirrorFixture(args) : concurrentMetadataFixture ? ConcurrentMetadataFixture() : downloadOnlyFixture ? InstallDownloadOnlyFixture() : dualFixture ? InstallDualFixture() : assetSourcesFixture ? ShowAssetSources() : iconFixture ? VerifyIconDecode() : netdiskFixture ? InspectModpackExternal() : fabricFixture ? InstallFabricFixture() : neoforgeFixture ? InstallNeoForgeFixture() : bbsmcFixture ? InstallBbsmcFixture() : retryFixture ? RunRetryFixture() : modrinthFixture ? InstallModrinthFixture() : allOfCreateFixture ? InstallAllOfCreateFixture() : vanillaFixture ? InstallVanillaFixture() : encodingFixture ? VerifyEncodingFixture() : forgeFixture ? InstallForgeFixture() : allSources ? SearchAllSources() : searchCategories ? SearchCategories() : listLaunchers ? ListLaunchers() : installFixture ? InstallFixture() : VerifySources());
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    exitCode = 1;
                }
                finally
                {
                    app.Shutdown();
                }
            }));
            app.Run();
            Environment.Exit(exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int InstallModrinthFixture()
    {
        var pack = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXMC", "ModpackCache", "modrinth-1KVo5zza", "Fabulously.Optimized-v15.0.0-beta.2.mrpack");
        if (!File.Exists(pack)) throw new FileNotFoundException("Cached Fabulously Optimized pack not found.", pack);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "modrinth-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = "Fabulously Optimized";
        var service = new KernelService();
        var request = new InstallRequest(instance, "", null, null, null, null, pack,
            new[] { new LauncherOption("modrinth-target", "Modrinth 本地包", LauncherKind.Generic, minecraft, null, false) });
        var progress = new Progress<TargetProgress>(update =>
            Console.WriteLine($"{update.Progress:P0} {update.Message}"));
        service.InstallAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();
        VerifyInstalledFixture(minecraft, instance);
        Console.WriteLine("MODRINTH_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int InstallAllOfCreateFixture()
    {
        var pack = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXMC", "ModpackCache", "curseforge-885460", "All of Create-v2.6.zip");
        if (!File.Exists(pack)) throw new FileNotFoundException("Cached All of Create pack not found.", pack);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "allofcreate-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = "All of Create";
        Action<double, string> report = (value, message) => Console.WriteLine($"{value:P0} {message}");
        KernelFacade.InstallModpack(instance, "", "", "", pack, minecraft, report, CancellationToken.None);
        VerifyInstalledFixture(minecraft, instance);
        var mods = Directory.Exists(Path.Combine(minecraft, "versions", instance, "mods"))
            ? Directory.EnumerateFiles(Path.Combine(minecraft, "versions", instance, "mods"), "*.jar").Count()
            : 0;
        Console.WriteLine($"ALLOFCREATE_OK; MODS={mods}");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int ConcurrentMetadataFixture()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXMC", "Downloads", "BBSMC");
        var pack = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.mrpack").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (pack is null) throw new FileNotFoundException("No cached BBSMC pack found.", folder);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "concurrent-metadata-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var service = new KernelService();
        using var cancellation = new CancellationTokenSource();
        var target = new LauncherOption("target", "测试目标", LauncherKind.Generic, minecraft, null, false);
        var request = new InstallRequest("ConcurrentMetadata", "", null, null, null, null, pack, new[] { target });
        var install = Task.Run(() => service.InstallAsync(request,
            new Progress<TargetProgress>(_ => { }), cancellation.Token));

        Thread.Sleep(4000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var versions = service.GetModpackVersionsAsync("ft-wt", "BBSMC").GetAwaiter().GetResult();
        watch.Stop();
        Console.WriteLine($"metadata during install: {versions.Count} versions in {watch.ElapsedMilliseconds} ms");
        var settingsWatch = System.Diagnostics.Stopwatch.StartNew();
        service.UpdateSettings(new UiSettings());
        settingsWatch.Stop();
        Console.WriteLine($"settings apply during install: {settingsWatch.ElapsedMilliseconds} ms");
        cancellation.Cancel();
        try { install.GetAwaiter().GetResult(); } catch { }
        if (versions.Count == 0) throw new InvalidOperationException("No versions returned.");
        if (watch.ElapsedMilliseconds > 20000) throw new TimeoutException("Version metadata was blocked by the install lock.");
        if (settingsWatch.ElapsedMilliseconds > 5000) throw new TimeoutException("Saving settings was blocked by the install lock.");
        Console.WriteLine("CONCURRENT_METADATA_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int InstallDownloadOnlyFixture()
    {
        var pack = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXMC", "ModpackCache", "modrinth-1KVo5zza", "Fabulously.Optimized-v15.0.0-beta.2.mrpack");
        if (!File.Exists(pack)) throw new FileNotFoundException("Cached Modrinth pack not found.", pack);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "download-only-" + stamp));
        Directory.CreateDirectory(destination);
        var target = new LauncherOption("local-download", "仅下载到本地", LauncherKind.Generic, destination, null, false)
        {
            IsDownloadTarget = true
        };
        var request = new InstallRequest("DownloadedPack", "", null, null, null, null, pack,
            new[] { target }, DownloadOnly: true, DownloadDirectory: destination);
        var service = new KernelService();
        var progress = new Progress<TargetProgress>(update =>
            Console.WriteLine($"{update.Progress:P0} {update.Message}"));
        service.InstallAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();

        var saved = Directory.EnumerateFiles(destination, "DownloadedPack*").FirstOrDefault();
        if (saved is null) throw new InvalidOperationException("Download-only archive was not saved.");
        if (new FileInfo(saved).Length <= 0) throw new InvalidOperationException("Download-only archive is empty.");
        Console.WriteLine($"DOWNLOAD_ONLY_OK={saved}; SIZE={new FileInfo(saved).Length:N0}");

        var vanillaName = "DownloadedVanilla";
        var vanillaRequest = new InstallRequest(vanillaName, "1.12.2", null, null, null, null, null,
            new[] { target }, DownloadOnly: true, DownloadDirectory: destination);
        service.InstallAsync(vanillaRequest, progress, CancellationToken.None).GetAwaiter().GetResult();
        VerifyInstalledFixture(Path.Combine(destination, vanillaName), vanillaName);
        Console.WriteLine("DOWNLOAD_ONLY_VANILLA_OK");
        Console.WriteLine($"SMOKE_ROOT={destination}");
        return 0;
    }

    private static int InstallDualFixture()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "dual-smoke-" + stamp));
        var pcl = Path.Combine(root, "pcl", ".minecraft");
        var hmcl = Path.Combine(root, "hmcl", ".minecraft");
        Directory.CreateDirectory(pcl);
        Directory.CreateDirectory(hmcl);
        var instance = "nexmc-dual-" + stamp;
        var targets = new[]
        {
            new LauncherOption("pcl", "PCL（测试）", LauncherKind.PCL2, pcl, null, false),
            new LauncherOption("hmcl", "HMCL（测试）", LauncherKind.HMCL, hmcl, null, false)
        };
        var request = new InstallRequest(instance, "1.12.2", null, null, null, null, null, targets);
        var service = new KernelService();
        var progress = new Progress<TargetProgress>(update =>
            Console.WriteLine($"[{update.Target}] {update.Progress:P0} {update.Message}"));
        service.InstallAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();
        VerifyInstalledFixture(pcl, instance);
        VerifyInstalledFixture(hmcl, instance);
        Console.WriteLine("DUAL_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int InstallVanillaFixture()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "vanilla-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = "nexmc-vanilla-smoke-" + stamp;
        Action<double, string> report = (value, message) => Console.WriteLine($"{value:P0} {message}");
        KernelFacade.InstallVanilla(instance, "1.12.2", "", "", minecraft, report, CancellationToken.None);
        VerifyInstalledFixture(minecraft, instance);
        Console.WriteLine("VANILLA_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    /// <summary>用指定的下载线路安装原版；用于复现镜像线路在新版本上的卡住问题。</summary>
    private static int InstallMirrorFixture(string[] args)
    {
        var index = Array.FindIndex(args, arg => arg.Equals("--mirror-fixture", StringComparison.OrdinalIgnoreCase));
        var version = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : "26.3";
        var source = args.Contains("--official", StringComparer.OrdinalIgnoreCase) ? "official" : "mirror";
        var useService = args.Contains("--service", StringComparer.OrdinalIgnoreCase);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", $"mirror-{source}-{version}-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = $"nexmc-{source}-{version}".Replace(' ', '-');
        Action<double, string> report = (value, message) =>
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {value:P1} {message}");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        try
        {
            if (useService)
            {
                var service = new KernelService();
                service.UpdateSettings(new UiSettings
                {
                    GameDownloadSource = source,
                    DownloadThreads = 16,
                    RetryAttempts = 2,
                    RetryDelaySeconds = 2
                });
                var target = new LauncherOption("mirror-target", "镜像测试", LauncherKind.Generic, minecraft, null, false);
                var request = new InstallRequest(instance, version, null, null, null, null, null, new[] { target });
                service.InstallAsync(request, new Progress<TargetProgress>(update =>
                    report(update.Progress, $"[{update.Target}] {update.Message}")), cancellation.Token)
                    .GetAwaiter().GetResult();
            }
            else
            {
                KernelFacade.ApplyDownloadPreferences(source == "official" ? 2 : 0, source == "official" ? 2 : 0, 16, 0);
                KernelFacade.InstallVanilla(instance, version, "", "", minecraft, report, cancellation.Token);
            }
            VerifyInstalledFixture(minecraft, instance);
            Console.WriteLine($"MIRROR_OK source={source} version={version} service={useService}");
            Console.WriteLine($"SMOKE_ROOT={root}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MIRROR_FAIL source={source} version={version} service={useService}: {exception.GetBaseException().Message}");
            return 1;
        }
    }

    private static void VerifyInstalledFixture(string minecraft, string instance)
    {
        var versionDirectory = Path.Combine(minecraft, "versions", instance);
        var json = Path.Combine(versionDirectory, instance + ".json");
        var jar = Path.Combine(versionDirectory, instance + ".jar");
        if (!File.Exists(json)) throw new InvalidOperationException("Version JSON missing.");
        if (!File.Exists(jar) || new FileInfo(jar).Length == 0) throw new InvalidOperationException("Client jar missing.");
        if (!Directory.EnumerateFiles(Path.Combine(minecraft, "libraries"), "*", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("libraries directory is empty.");
        if (!Directory.EnumerateFiles(Path.Combine(minecraft, "assets", "indexes"), "*.json").Any())
            throw new InvalidOperationException("assets indexes missing.");
    }

    private static int VerifyEncodingFixture()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "encoding-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        var folder = Path.Combine(root, ".minecraft") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        var gb18030 = Encoding.GetEncoding("GB18030");
        PCL.ModMinecraft.McFolderLauncherProfilesJsonCreate(folder);
        var profile = Path.Combine(folder, "launcher_profiles.json");
        if (!File.Exists(profile)) throw new InvalidOperationException("launcher_profiles.json was not created.");
        var text = File.ReadAllText(profile, gb18030);
        if (!text.Contains("\"profiles\"", StringComparison.Ordinal)) throw new InvalidOperationException("launcher_profiles.json content is invalid.");
        Console.WriteLine("GB18030=OK");
        Console.WriteLine("LAUNCHER_PROFILES=OK");
        Console.WriteLine($"PROFILE_PATH={profile}");
        return 0;
    }

    private static int InstallForgeFixture()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "forge-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var loaders = KernelFacade.GetLoaders("1.12.2", "Forge");
        var forge = loaders.FirstOrDefault(loader => loader.Version.Contains("14.23.5.2860", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Forge 14.23.5.2860 未出现在版本列表中。");
        Console.WriteLine($"FORGE_VERSION={forge.Version}; TOTAL={loaders.Count}");
        var instance = "nexmc-forge-smoke-" + stamp;
        Action<double, string> report = (value, message) => Console.WriteLine($"{value:P0} {message}");
        KernelFacade.InstallVanilla(instance, "1.12.2", "Forge", forge.Version, minecraft,
            report, CancellationToken.None);
        var versionDirectory = Path.Combine(minecraft, "versions", instance);
        var json = Path.Combine(versionDirectory, instance + ".json");
        var jar = Path.Combine(versionDirectory, instance + ".jar");
        if (!File.Exists(json)) throw new InvalidOperationException("Forge version JSON missing.");
        if (!File.Exists(jar) || new FileInfo(jar).Length == 0) throw new InvalidOperationException("Forge client jar missing.");
        Console.WriteLine($"FORGE_INSTALL_OK={json}");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int InstallFabricFixture()
    {
        const string minecraftVersion = "1.20.1";
        var loaders = KernelFacade.GetLoaders(minecraftVersion, "Fabric");
        var loader = loaders.FirstOrDefault()
            ?? throw new InvalidOperationException("Fabric 版本列表为空。");
        Console.WriteLine($"FABRIC_LOADER={loader.Version}; TOTAL={loaders.Count}");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "fabric-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = "nexmc-fabric-" + stamp;
        Action<double, string> report = (value, message) => Console.WriteLine($"{value:P0} {message}");
        KernelFacade.InstallVanilla(instance, minecraftVersion, "Fabric", loader.Version, minecraft, report, CancellationToken.None);
        VerifyInstalledFixture(minecraft, instance);
        Console.WriteLine("FABRIC_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int InstallNeoForgeFixture()
    {
        const string minecraftVersion = "1.21.1";
        var loaders = KernelFacade.GetLoaders(minecraftVersion, "NeoForge");
        var loader = loaders.FirstOrDefault(item => item.Version.Contains("21.1.256", StringComparison.Ordinal))
            ?? loaders.FirstOrDefault()
            ?? throw new InvalidOperationException("NeoForge 版本列表为空。");
        Console.WriteLine($"NEOFORGE_LOADER={loader.Version}; TOTAL={loaders.Count}");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "neoforge-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = "nexmc-neoforge-" + stamp;
        Action<double, string> report = (value, message) => Console.WriteLine($"{value:P0} {message}");
        KernelFacade.InstallVanilla(instance, minecraftVersion, "NeoForge", loader.Version, minecraft, report, CancellationToken.None);
        VerifyInstalledFixture(minecraft, instance);
        Console.WriteLine("NEOFORGE_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int InstallBbsmcFixture()
    {
        var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXMC", "Downloads", "BBSMC");
        var pack = Directory.Exists(downloadFolder)
            ? Directory.EnumerateFiles(downloadFolder, "*.mrpack")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (pack is null) throw new FileNotFoundException("No BBSMC .mrpack downloaded through the embedded window.", downloadFolder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "bbsmc-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var instance = "BBSMC-FarmingTales";
        Console.WriteLine($"PACK={Path.GetFileName(pack)}; SIZE={new FileInfo(pack).Length:N0}");
        var service = new KernelService();
        var request = new InstallRequest(instance, "", null, null, null, null, pack,
            new[] { new LauncherOption("bbsmc-target", "BBSMC 本地包", LauncherKind.Generic, minecraft, null, false) });
        var progress = new Progress<TargetProgress>(update =>
            Console.WriteLine($"{update.Progress:P0} {update.Message}"));
        service.InstallAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();
        VerifyInstalledFixture(minecraft, instance);
        Console.WriteLine("BBSMC_LOCAL_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private static int RunRetryFixture()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "retry-smoke-" + stamp));
        var minecraft = Path.Combine(root, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var pack = Path.Combine(root, "retry-fixture.mrpack");
        var payload = Encoding.UTF8.GetBytes("nexmc-retry-fixture");
        var sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();
        var sha512 = Convert.ToHexString(SHA512.HashData(payload)).ToLowerInvariant();
        using (var archive = ZipFile.Open(pack, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("modrinth.index.json");
            using var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false));
            writer.Write($$"""
            {
              "formatVersion": 1,
              "game": "minecraft",
              "versionId": "nexmc-retry-fixture",
              "name": "NEXMC Retry Fixture",
              "files": [
                {
                  "path": "mods/nexmc-retry-missing.jar",
                  "hashes": { "sha1": "{{sha1}}", "sha512": "{{sha512}}" },
                  "downloads": [ "http://127.0.0.1:9/nexmc-retry-missing.jar" ],
                  "fileSize": {{payload.Length}}
                }
              ],
              "dependencies": { "minecraft": "1.12.2" }
            }
            """);
        }

        var service = new KernelService();
        var instance = "nexmc-retry-" + stamp;
        var request = new InstallRequest(instance, "", null, null, null, null, pack,
            new[] { new LauncherOption("retry-target", "重试测试目标", LauncherKind.Generic, minecraft, null, false) });
        var progress = new CollectingProgress();
        Exception? failure = null;
        try
        {
            service.InstallAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failure = exception;
            Console.WriteLine("EXPECTED_FAILURE=" + exception.GetBaseException().Message);
        }

        var retryMessages = progress.Messages.Count(message => message.Contains("秒后进行第", StringComparison.Ordinal));
        var attemptMessages = progress.Messages.Count(message => message.Contains("次尝试安装到", StringComparison.Ordinal));
        Console.WriteLine($"RETRY_MESSAGES={retryMessages}; ATTEMPT_MESSAGES={attemptMessages}");
        if (failure is null)
        {
            throw new InvalidOperationException("重试测试本应失败，但安装成功了。");
        }
        if (retryMessages < 2 || attemptMessages < 2)
        {
            throw new InvalidOperationException("没有观察到 3 次尝试和两次 10 秒重试。");
        }
        Console.WriteLine("RETRY_OK");
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }

    private sealed class CollectingProgress : IProgress<TargetProgress>
    {
        private readonly List<string> _messages = new();
        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) return _messages.ToList(); }
        }

        public void Report(TargetProgress value)
        {
            lock (_messages) _messages.Add(value.Message ?? string.Empty);
        }
    }

    private static int SearchAllSources()
    {
        var service = new KernelService();
        var page = service.SearchModpacksAsync("", ModpackSources.All, null, "冒险", 0, 30).GetAwaiter().GetResult();
        Console.WriteLine($"ALL_SOURCES total={page.TotalHits} returned={page.Projects.Count} nextOffset={page.Offset}");
        foreach (var source in ModpackSources.Individual)
        {
            var count = page.Projects.Count(project => string.Equals(project.Source, source, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"  {source}: {count}");
        }
        foreach (var warning in page.Warnings)
        {
            Console.WriteLine($"  WARNING {warning}");
        }
        if (page.Projects.Count == 0)
        {
            throw new InvalidOperationException("全部来源没有返回项目。");
        }
        if (page.Projects.Any(project => string.IsNullOrWhiteSpace(project.ProjectUrl)))
        {
            throw new InvalidOperationException("全部来源返回了没有介绍页的项目。");
        }
        Console.WriteLine("ALL_SOURCES_OK");
        return 0;
    }

    private static int SearchCategories()
    {
        var service = new KernelService();
        var categories = new[] { "冒险", "科技", "魔法", "优化", "战斗", "任务", "多人", "硬核", "轻量" };
        var failures = 0;
        foreach (var source in new[] { "BBSMC", "Modrinth", "CurseForge" })
        {
            foreach (var category in categories)
            {
                try
                {
                    var page = service.SearchModpacksAsync("", source, null, category, 0, 3).GetAwaiter().GetResult();
                    Console.WriteLine($"{source}/{category}: total={page.TotalHits}, returned={page.Projects.Count}");
                    if (page.Projects.Count == 0)
                    {
                        Console.WriteLine($"{source}/{category}: EMPTY");
                        continue;
                    }
                    if (page.Projects.Any(project => !Uri.TryCreate(project.ProjectUrl, UriKind.Absolute, out _)))
                        throw new InvalidOperationException("Project URL is missing.");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"{source}/{category} FAILED: {exception.GetBaseException().Message}");
                    failures++;
                }
            }
        }
        return failures == 0 ? 0 : 2;
    }

    private static int ShowAssetSources()
    {
        var url = "https://resources.download.minecraft.net/10/10f9323f178d439e62c892d4b519b9c9e0dc44bd";
        foreach (var source in KernelFacade.GetAssetSources(url))
        {
            Console.WriteLine(source);
        }
        return 0;
    }

    private static int VerifyIconDecode()
    {
        var local = Path.GetFullPath(Path.Combine("artifacts", "bbsmc-cover.webp"));
        if (!File.Exists(local)) throw new FileNotFoundException("Local WebP fixture missing", local);
        var localImage = NEXMC.Controls.ModpackIcon.DecodeImage(File.ReadAllBytes(local));
        Console.WriteLine($"local webp: {(localImage is null ? "FAILED" : $"{localImage.Width}x{localImage.Height}")}");
        if (localImage is null) throw new InvalidOperationException("Local WebP decode failed.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 NEXMC-Smoke");
        const string url = "https://cdn.modrinth.com/data/1KVo5zza/d8152911f8fd5d7e9a8c499fe89045af81fe816e_96.webp";
        var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        var remoteImage = NEXMC.Controls.ModpackIcon.DecodeImage(bytes);
        Console.WriteLine($"remote webp: {bytes.Length} bytes -> {(remoteImage is null ? "FAILED" : $"{remoteImage.Width}x{remoteImage.Height}")}");
        if (remoteImage is null) throw new InvalidOperationException("Remote WebP decode failed.");
        return 0;
    }

    private static int InspectModpackExternal()
    {
        var service = new KernelService();
        var versions = service.GetModpackVersionsAsync("ft-wt", "BBSMC").GetAwaiter().GetResult();
        Console.WriteLine($"versions={versions.Count}");
        foreach (var version in versions.Take(12))
        {
            Console.WriteLine($"  {version.Id} | direct={version.IsDirectDownload} | ext={version.ExternalUrl} | {version.Display}");
        }

        var external = versions.Count(version => version.HasExternalUrl);
        Console.WriteLine($"external links: {external}/{versions.Count}");
        if (versions.Count == 0) throw new InvalidOperationException("No versions returned.");
        if (external == 0) throw new InvalidOperationException("No external/cloud URL was preserved.");
        return 0;
    }

    private static int ListLaunchers()
    {
        var launchers = new KernelService().DetectLaunchersAsync().GetAwaiter().GetResult();
        foreach (var launcher in launchers)
        {
            Console.WriteLine($"{launcher.Kind} | {launcher.DisplayLabel} | exe={launcher.ExecutablePath} | dir={launcher.Directory}");
        }
        return launchers.Count == 0 ? 2 : 0;
    }

    private static int VerifySources()
    {
        var versions = KernelFacade.GetMinecraftVersions(true);
        Console.WriteLine($"Minecraft versions: {versions.Count}; newest={versions.FirstOrDefault()?.Id}");
        if (versions.Count < 100) throw new InvalidOperationException("Version list is unexpectedly small.");
        var launchers = new KernelService().DetectLaunchersAsync().GetAwaiter().GetResult();
        Console.WriteLine($"Detected launchers: {launchers.Count}");
        foreach (var launcher in launchers) Console.WriteLine($"  {launcher.Kind}: {launcher.Name} -> {launcher.Directory} | exe={launcher.ExecutablePath}");

        var queries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BBSMC"] = "愚者",
            ["Modrinth"] = "Fabulously Optimized",
            ["CurseForge"] = "RLCraft"
        };
        var failures = new List<string>();
        foreach (var source in new[] { "BBSMC", "Modrinth", "CurseForge" })
        {
            try
            {
                var projects = KernelFacade.SearchModpacks(queries[source], source, "");
                Console.WriteLine($"{source} search: {projects.Count} project(s); first={projects.FirstOrDefault()?.Name ?? "<none>"}");
                if (projects.Count == 0) throw new InvalidOperationException("No search results.");
                var directFound = false;
                foreach (var project in projects.Take(5))
                {
                    var files = KernelFacade.GetModpackFiles(project.Id, source);
                    var direct = files.Where(file => file.IsDirectDownload).ToList();
                    Console.WriteLine($"  {project.Name}: {files.Count} version(s), {direct.Count} direct");
                    if (direct.Count == 0) continue;
                    var archive = KernelFacade.DownloadModpackArchive(project.Id, direct[0].Id, source);
                    var length = new FileInfo(archive).Length;
                    if (length <= 0) throw new InvalidOperationException("Downloaded archive is empty.");
                    using var zip = ZipFile.OpenRead(archive);
                    var recognized = zip.Entries.Any(entry =>
                        entry.FullName.EndsWith("modrinth.index.json", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith("modpack.json", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith("mmc-pack.json", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith("mcbbs.packmeta", StringComparison.OrdinalIgnoreCase));
                    if (!recognized) throw new InvalidOperationException("Downloaded archive has no recognized modpack manifest.");
                    Console.WriteLine($"  downloaded: {Path.GetFileName(archive)} ({length:N0} bytes), manifest=OK");
                    directFound = true;
                    break;
                }
                if (!directFound) Console.WriteLine($"  {source}: no directly downloadable modpack in the first five results.");
            }
            catch (Exception exception)
            {
                failures.Add($"{source}: {exception.GetBaseException().Message}");
                Console.Error.WriteLine($"{source} FAILED: {exception}");
            }
        }
        if (failures.Count > 0)
        {
            Console.Error.WriteLine("Source failures: " + string.Join(" | ", failures));
            return 2;
        }
        return 0;
    }

    private static int InstallFixture()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "smoke-" + stamp);
        root = Path.GetFullPath(root);
        var packDirectory = Path.Combine(root, "fixture");
        Directory.CreateDirectory(packDirectory);
        var archivePath = Path.Combine(packDirectory, "nexmc-smoke.mrpack");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("modrinth.index.json");
            using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                writer.Write("""
                {
                  "formatVersion": 1,
                  "game": "minecraft",
                  "versionId": "nexmc-smoke",
                  "name": "NEXMC Smoke Pack",
                  "summary": "NEXMC end-to-end installation fixture",
                  "files": [],
                  "dependencies": { "minecraft": "1.8.9" }
                }
                """);
            }
            var options = archive.CreateEntry("overrides/options.txt");
            using var optionsWriter = new StreamWriter(options.Open(), new UTF8Encoding(false));
            optionsWriter.Write("fancyGraphics:true\n");
        }
        var pclRoot = Path.Combine(root, "pcl2", ".minecraft");
        var hmclRoot = Path.Combine(root, "hmcl", ".minecraft");
        var instance = "nexmc-smoke-" + stamp;
        var service = new KernelService();
        var request = new InstallRequest(instance, "", null, null, null, null, archivePath,
            new[]
            {
                new LauncherOption("smoke-pcl", "PCL2", LauncherKind.PCL2, pclRoot, null, false),
                new LauncherOption("smoke-hmcl", "HMCL", LauncherKind.HMCL, hmclRoot, null, false)
            });
        var progress = new Progress<TargetProgress>(update =>
            Console.WriteLine($"[{update.Target}] {update.Progress:P0} {update.Message} success={update.Success}"));
        service.InstallAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();

        var checks = new[]
        {
            (Name: "PCL2", Root: pclRoot),
            (Name: "HMCL", Root: hmclRoot)
        };
        foreach (var check in checks)
        {
            var json = Path.Combine(check.Root, "versions", instance, instance + ".json");
            if (!File.Exists(json)) throw new InvalidOperationException($"{check.Name} version JSON missing: {json}");
            var versionDirectory = Path.GetDirectoryName(json)!;
            var jar = Path.Combine(versionDirectory, instance + ".jar");
            if (!File.Exists(jar) || new FileInfo(jar).Length == 0)
                throw new InvalidOperationException($"{check.Name} client jar missing or empty: {jar}");
            var options = Path.Combine(versionDirectory, "options.txt");
            if (!File.Exists(options)) throw new InvalidOperationException($"{check.Name} overrides/options.txt missing.");

            using var document = JsonDocument.Parse(File.ReadAllText(json));
            var rootElement = document.RootElement;
            if (rootElement.GetProperty("id").GetString() != instance)
                throw new InvalidOperationException($"{check.Name} version JSON id does not match {instance}.");
            if (!rootElement.TryGetProperty("mainClass", out var mainClass) || string.IsNullOrWhiteSpace(mainClass.GetString()))
                throw new InvalidOperationException($"{check.Name} version JSON has no mainClass.");
            if (!Directory.EnumerateFiles(Path.Combine(check.Root, "libraries"), "*", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException($"{check.Name} libraries directory is empty.");
            if (!Directory.EnumerateFiles(Path.Combine(check.Root, "assets", "indexes"), "*.json").Any())
                throw new InvalidOperationException($"{check.Name} asset index is missing.");

            Console.WriteLine($"{check.Name} install OK: {json} (jar + libraries + assets verified)");
        }
        Console.WriteLine($"SMOKE_ROOT={root}");
        return 0;
    }
}
