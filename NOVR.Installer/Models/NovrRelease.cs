using NOVR.Installer.Services;

namespace NOVR.Installer.Models;

public sealed record NovrRelease(
    string TagName,
    string Name,
    ReleaseVersion Version,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt,
    string AssetName,
    string AssetDownloadUrl)
{
    public string DisplayName
    {
        get
        {
            var date = PublishedAt?.ToString("yyyy-MM-dd") ?? "unpublished";
            var prerelease = IsPrerelease ? " prerelease" : string.Empty;
            return $"{TagName} ({date}{prerelease})";
        }
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
