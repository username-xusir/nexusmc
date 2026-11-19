using System.Text.RegularExpressions;

namespace NEXMC.Services;

/// <summary>网盘平台。只用于识别和展示，不代表已实现自动解析。</summary>
public enum NetdiskPlatform
{
    Direct,
    Quark,
    Uc,
    Baidu,
    Xunlei,
    Pan123,
    Pan115,
    Lanzou,
    ILanzou,
    C139,
    GuangYa,
    Unknown
}

public sealed record ParsedNetdiskLink(
    NetdiskPlatform Platform,
    string ShareId,
    string? Password,
    string OriginalUrl,
    string DisplayName,
    bool SupportsAutoResolve,
    string LoginUrl,
    string CookieDomain)
{
    public string PasswordHint => string.IsNullOrWhiteSpace(Password) ? "无提取码" : $"提取码 {Password}";
}

/// <summary>
/// 从 BBSMC 等来源返回的地址里识别网盘类型、分享 ID 和提取码。
/// 只处理链接文本和公开 URL 规则，不依赖任何第三方程序。
/// </summary>
public static partial class NetdiskLinkParser
{
    private const string QuarkLoginUrl = "https://pan.quark.cn/";
    private const string UcLoginUrl = "https://drive.uc.cn/";
    private const string BaiduLoginUrl = "https://pan.baidu.com/";
    private const string XunleiLoginUrl = "https://pan.xunlei.com/";
    private const string Pan123LoginUrl = "https://www.123pan.com/";
    private const string Pan115LoginUrl = "https://115.com/";
    private const string LanzouLoginUrl = "https://www.lanzou.com/";
    private const string C139LoginUrl = "https://yun.139.com/";
    private const string GuangYaLoginUrl = "https://www.guangyapan.com/";

    private static readonly string[] DirectArchiveExtensions = { ".zip", ".mrpack" };

    public static ParsedNetdiskLink? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var source = text.Trim();
        var url = ExtractUrl(source);
        if (string.IsNullOrWhiteSpace(url)) return null;

        var pwd = PasswordFromUrl(url) ?? PasswordFromText(source);

        var match = QuarkRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Quark, match.Groups[1].Value, pwd, url,
                "夸克网盘", supportsAutoResolve: true, QuarkLoginUrl, "https://pan.quark.cn");
        }

        match = UcRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Uc, match.Groups[1].Value, pwd, url,
                "UC 网盘", supportsAutoResolve: true, UcLoginUrl, "https://drive.uc.cn");
        }

        match = XunleiRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Xunlei, match.Groups[1].Value, pwd, url,
                "迅雷网盘", supportsAutoResolve: false, XunleiLoginUrl, "https://pan.xunlei.com");
        }

        match = BaiduRegex().Match(url);
        if (match.Success)
        {
            var rawId = match.Groups[1].Value;
            var surl = rawId.StartsWith('1') ? rawId[1..] : rawId;
            return Build(NetdiskPlatform.Baidu, surl, pwd, url,
                "百度网盘", supportsAutoResolve: false, BaiduLoginUrl, "https://pan.baidu.com");
        }

        match = C139Regex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.C139, match.Groups[1].Value, pwd, url,
                "中国移动云盘", supportsAutoResolve: false, C139LoginUrl, "https://yun.139.com");
        }

        match = Pan123Regex().Match(url);
        if (!match.Success) match = Pan123SubRegex().Match(url);
        if (!match.Success) match = Pan123SrrRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Pan123, match.Groups[1].Value, pwd, url,
                "123 云盘", supportsAutoResolve: false, Pan123LoginUrl, "https://www.123pan.com");
        }

        match = Pan115CommandRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Pan115, match.Groups[1].Value, match.Groups[2].Value, url,
                "115 网盘", supportsAutoResolve: false, Pan115LoginUrl, "https://115.com");
        }

        match = Pan115Regex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Pan115, match.Groups[1].Value, pwd, url,
                "115 网盘", supportsAutoResolve: false, Pan115LoginUrl, "https://115.com");
        }

        match = GuangYaRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.GuangYa, match.Groups[1].Value, pwd, url,
                "光鸭云盘", supportsAutoResolve: false, GuangYaLoginUrl, "https://www.guangyapan.com");
        }

        match = ILanzouRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.ILanzou, match.Groups[1].Value, pwd, url,
                "蓝奏云优享版", supportsAutoResolve: false, LanzouLoginUrl, "https://www.ilanzou.com");
        }

        match = LanzouRegex().Match(url);
        if (match.Success)
        {
            return Build(NetdiskPlatform.Lanzou, match.Groups[1].Value, pwd, url,
                "蓝奏云", supportsAutoResolve: false, LanzouLoginUrl, "https://www.lanzou.com");
        }

        if (LooksLikeDirectArchive(url))
        {
            return Build(NetdiskPlatform.Direct, string.Empty, null, url,
                "直链文件", supportsAutoResolve: true, string.Empty, string.Empty);
        }

        return null;
    }

    public static bool IsSupported(ParsedNetdiskLink? link) =>
        link is not null && link.SupportsAutoResolve;

    private static ParsedNetdiskLink Build(NetdiskPlatform platform, string shareId, string? pwd,
        string url, string displayName, bool supportsAutoResolve, string loginUrl, string cookieDomain) =>
        new(platform, shareId, pwd, url, displayName, supportsAutoResolve, loginUrl, cookieDomain);

    private static string? ExtractUrl(string text)
    {
        var match = UrlRegex().Match(text);
        return match.Success
            ? match.Value.TrimEnd('。', '，', ',', '；', ';', ')', ']', '}', '"', '\'')
            : null;
    }

    private static bool LooksLikeDirectArchive(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        var path = uri.AbsolutePath;
        return DirectArchiveExtensions.Any(extension =>
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string? PasswordFromUrl(string url)
    {
        var match = PasswordInUrlRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? PasswordFromText(string text)
    {
        var match = PasswordInTextRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"pan\.quark\.cn/s/([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex QuarkRegex();

    [GeneratedRegex(@"drive\.uc\.cn/s/([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UcRegex();

    [GeneratedRegex(@"pan\.xunlei\.com/s/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex XunleiRegex();

    [GeneratedRegex(@"pan\.baidu\.com/s/(1[A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BaiduRegex();

    [GeneratedRegex(@"yun\.139\.com/shareweb/.*?/w/i/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex C139Regex();

    [GeneratedRegex(@"123(?:865|684|pan)\.(?:com|cn)/s/([A-Za-z0-9]+-[A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pan123Regex();

    [GeneratedRegex(@"share\.123pan\.cn/123pan/([A-Za-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pan123SubRegex();

    [GeneratedRegex(@"api/srr\?sk=([A-Za-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pan123SrrRegex();

    [GeneratedRegex(@"115(?:cdn|rc)?\.com/s/(sw[A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pan115Regex();

    [GeneratedRegex(@"115(?:cdn|rc)?\.com/(sw[A-Za-z0-9]{8,})-([A-Za-z0-9]{4,8})", RegexOptions.IgnoreCase)]
    private static partial Regex Pan115CommandRegex();

    [GeneratedRegex(@"guangyapan\.com/s/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GuangYaRegex();

    [GeneratedRegex(@"(?:^|[/.])ilanzou\.(?:com|net|org|cn)/s/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ILanzouRegex();

    [GeneratedRegex(@"(?:^|[/.])(?:lanzou[a-z0-9-]*|lan[zs]o[ux])\.(?:com|net|org|cn)/([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LanzouRegex();

    [GeneratedRegex(@"[?&](?:pwd|pass|passcode|password)=([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordInUrlRegex();

    [GeneratedRegex(@"(?:提取码|访问码|密码)[：:]\s*([A-Za-z0-9]{4,8})")]
    private static partial Regex PasswordInTextRegex();
}
