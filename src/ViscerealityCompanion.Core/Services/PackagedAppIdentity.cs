namespace ViscerealityCompanion.Core.Services;

public static class PackagedAppIdentity
{
    public const string ReleasePackageName = "MesmerPrism.ViscerealityCompanion";
    public const string ReleaseDisplayName = "Viscereality Companion";
    public const string DevPackageName = "MesmerPrism.ViscerealityCompanionDev";
    public const string DevDisplayName = "Viscereality Companion Dev";
    public const string LegacyPreviewPackageName = "MesmerPrism.ViscerealityCompanionPreview";
    public const string LegacyPreviewDisplayName = "Viscereality Companion Preview";

    public static bool IsReleasePackageName(string? packageName)
        => string.Equals(packageName, ReleasePackageName, StringComparison.OrdinalIgnoreCase);

    public static bool IsDevPackageName(string? packageName)
        => string.Equals(packageName, DevPackageName, StringComparison.OrdinalIgnoreCase);

    public static bool IsLegacyPreviewPackageName(string? packageName)
        => string.Equals(packageName, LegacyPreviewPackageName, StringComparison.OrdinalIgnoreCase);

    public static string GetDisplayName(string? packageName)
    {
        if (IsReleasePackageName(packageName))
        {
            return ReleaseDisplayName;
        }

        if (IsDevPackageName(packageName))
        {
            return DevDisplayName;
        }

        if (IsLegacyPreviewPackageName(packageName))
        {
            return LegacyPreviewDisplayName;
        }

        return string.IsNullOrWhiteSpace(packageName)
            ? "Viscereality Companion"
            : packageName;
    }

    public static IReadOnlyList<string> GetMigrationSourcePackageNames(string? targetPackageName)
    {
        if (IsReleasePackageName(targetPackageName))
        {
            return [LegacyPreviewPackageName];
        }

        if (IsLegacyPreviewPackageName(targetPackageName))
        {
            return [ReleasePackageName];
        }

        return [];
    }
}
