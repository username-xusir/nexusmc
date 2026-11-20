namespace NEXMC.Services;

/// <summary>
/// 夸克 / UC 的直链签名与取链时的 Cookie 绑定。响应里的 Set-Cookie 会刷新会话，
/// 下载直链时必须使用刷新后的 Cookie，否则会出现 412 Precondition Failed。
/// </summary>
public static class NetdiskCookieHeader
{
    public static string? FromSetCookies(IEnumerable<string> values)
    {
        var cookies = new List<string>();
        foreach (var value in values)
        {
            var first = value.Split(';', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(first) && first.Contains('='))
                cookies.Add(first);
        }
        return cookies.Count == 0 ? null : string.Join("; ", cookies);
    }

    public static string Merge(string? first, string? second)
    {
        var parts = new List<string>();
        foreach (var cookie in new[] { first, second })
        {
            if (string.IsNullOrWhiteSpace(cookie)) continue;
            foreach (var part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = part.Split('=', 2)[0].Trim();
                parts.RemoveAll(existing => existing.StartsWith(name + "=", StringComparison.Ordinal));
                parts.Add(part);
            }
        }
        return string.Join("; ", parts);
    }
}
