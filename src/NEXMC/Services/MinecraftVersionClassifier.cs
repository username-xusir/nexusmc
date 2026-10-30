namespace NEXMC.Services;

public static class MinecraftVersionClassifier
{
    public const string Release = "正式版本";
    public const string Snapshot = "快照版本";
    public const string AprilFools = "愚人节版本";
    public const string Legacy = "古早版本";
    public const string Other = "其他版本";

    private static readonly HashSet<string> AprilFoolsVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "2.0",
        "15w14a",
        "1.RV-Pre1",
        "3D Shareware v1.34",
        "20w14infinite",
        "22w13oneBlockAtATime",
        "23w13a_or_b",
        "24w14potato",
        "25w14craftmine"
    };

    public static string Classify(string? id, string? type)
    {
        var versionId = id?.Trim() ?? string.Empty;
        if (IsAprilFools(versionId))
        {
            return AprilFools;
        }

        return type?.ToLowerInvariant() switch
        {
            "release" => Release,
            "snapshot" => Snapshot,
            "old_beta" or "old_alpha" => Legacy,
            _ => Other
        };
    }

    /// <summary>Minecraft 26.x 起使用年份版本号（26.3、26.4-snapshot-6）。</summary>
    public static bool IsYearBased(string? versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId) || versionId.Length < 3) return false;
        var value = versionId.Trim();
        return char.IsDigit(value[0]) && char.IsDigit(value[1]) && value[2] == '.';
    }

    public static bool IsAprilFools(string versionId)
    {
        if (AprilFoolsVersions.Contains(versionId))
        {
            return true;
        }

        return versionId.Contains("infinite", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("oneblockatatime", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("potato", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("craftmine", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("shareware", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("rv-pre", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("a_or_b", StringComparison.OrdinalIgnoreCase) ||
               versionId.Contains("_or_b", StringComparison.OrdinalIgnoreCase);
    }
}
