using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NOVR.Installer.Models;

namespace NOVR.Installer.Services;

public sealed class GitHubReleaseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    public GitHubReleaseClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(InstallerConstants.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<IReadOnlyList<NovrRelease>> GetNovrReleasesAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        progress.Report($"Loading releases from {InstallerConstants.GitHubOwner}/{InstallerConstants.GitHubRepo}...");
        var releases = await GetReleasesAsync(
            InstallerConstants.GitHubOwner,
            InstallerConstants.GitHubRepo,
            cancellationToken);

        var installable = releases
            .Where(release => !release.Draft)
            .Select(TryCreateNovrRelease)
            .Where(release => release is not null)
            .Select(release => release!)
            .OrderByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
            .ToArray();

        if (installable.Length == 0)
        {
            throw new InvalidOperationException(
                $"No published {InstallerConstants.NovrReleaseAssetName} assets were found in {InstallerConstants.GitHubOwner}/{InstallerConstants.GitHubRepo} releases.");
        }

        return installable;
    }

    public async Task<string> DownloadNovrReleaseAsync(
        NovrRelease release,
        string tempDir,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var asset = new GitHubAsset(release.AssetName, release.AssetDownloadUrl);
        return await DownloadAssetAsync(asset, tempDir, progress, release.TagName, cancellationToken);
    }

    public async Task<string> DownloadLatestBepInEx5Async(string tempDir, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Checking latest BepInEx 5 release...");
        var release = await GetLatestBepInEx5ReleaseAsync(cancellationToken);
        var asset = (release.Assets ?? Array.Empty<GitHubAsset>()).FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("BepInEx", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("win_x64", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            throw new InvalidOperationException("Latest BepInEx 5 release does not contain a Windows x64 ZIP asset.");
        }

        return await DownloadAssetAsync(asset, tempDir, progress, release.TagName, cancellationToken);
    }

    private async Task<GitHubRelease[]> GetReleasesAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=50";
        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, JsonOptions, cancellationToken);
        return releases ?? throw new InvalidOperationException($"Could not read releases for {owner}/{repo}.");
    }

    private async Task<GitHubRelease> GetLatestBepInEx5ReleaseAsync(CancellationToken cancellationToken)
    {
        var releases = await GetReleasesAsync(InstallerConstants.BepInExOwner, InstallerConstants.BepInExRepo, cancellationToken);
        var release = releases?.FirstOrDefault(release => release.TagName.StartsWith("v5.", StringComparison.OrdinalIgnoreCase));
        return release ?? throw new InvalidOperationException("Could not find a BepInEx 5 release.");
    }

    private async Task<string> DownloadAssetAsync(GitHubAsset asset, string tempDir, IProgress<string> progress, string releaseName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tempDir);
        var destination = Path.Combine(tempDir, asset.Name);
        progress.Report($"Downloading {asset.Name} from release {releaseName}...");

        await using var remote = await _httpClient.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken);
        await using var local = File.Create(destination);
        await remote.CopyToAsync(local, cancellationToken);
        return destination;
    }

    private static NovrRelease? TryCreateNovrRelease(GitHubRelease release)
    {
        if (!ReleaseVersion.TryParse(release.TagName, out var version))
        {
            return null;
        }

        var asset = (release.Assets ?? Array.Empty<GitHubAsset>()).FirstOrDefault(asset =>
            asset.Name.Equals(InstallerConstants.NovrReleaseAssetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            return null;
        }

        return new NovrRelease(
            release.TagName,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            version,
            release.Prerelease,
            release.PublishedAt,
            asset.Name,
            asset.BrowserDownloadUrl);
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")]
        string TagName,
        string? Name,
        bool Draft,
        bool Prerelease,
        [property: JsonPropertyName("published_at")]
        DateTimeOffset? PublishedAt,
        GitHubAsset[]? Assets);

    private sealed record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")]
        string BrowserDownloadUrl);
}
