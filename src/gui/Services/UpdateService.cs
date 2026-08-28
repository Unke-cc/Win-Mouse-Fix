using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinMouseFix.Gui.Services;

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NoRelease,
    Error
}

public enum UpdateSource
{
    GitHub,
    Gitee
}

public sealed class SourceResolutionResult
{
    private SourceResolutionResult(UpdateSource? source, string? errorMessage)
    {
        Source = source;
        ErrorMessage = errorMessage;
    }

    public UpdateSource? Source { get; }
    public string? ErrorMessage { get; }
    public bool IsAvailable => Source.HasValue;

    internal static SourceResolutionResult Available(UpdateSource source) => new(source, null);
    internal static SourceResolutionResult Unavailable(string message) => new(null, message);
}

public sealed class UpdateCheckResult
{
    private UpdateCheckResult(
        UpdateCheckStatus status,
        UpdateSource? source,
        UpdateRelease? release,
        string? errorMessage)
    {
        Status = status;
        Source = source;
        ReleaseVersion = release?.ReleaseVersion;
        Version = release?.ReleaseVersion.NumericVersion;
        DisplayName = release?.DisplayName;
        PageUrl = release?.PageUrl;
        ErrorMessage = errorMessage;
        Artifact = release?.Artifact;
    }

    public UpdateCheckStatus Status { get; }
    public UpdateSource? Source { get; }
    // Kept for callers that only need the numeric part of a release version.
    public Version? Version { get; }
    public ReleaseVersion? ReleaseVersion { get; }
    public string? DisplayName { get; }
    public string? PageUrl { get; }
    public string? ErrorMessage { get; }
    public UpdateArtifact? Artifact { get; }
    public bool CanDownload => Status == UpdateCheckStatus.UpdateAvailable && Artifact is not null;

    internal static UpdateCheckResult FromRelease(UpdateRelease release, ReleaseVersion currentVersion) =>
        new(
            release.ReleaseVersion.CompareTo(currentVersion) > 0 ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            release.Source,
            release,
            null);

    internal static UpdateCheckResult NoRelease(UpdateSource source) =>
        new(UpdateCheckStatus.NoRelease, source, null, null);

    internal static UpdateCheckResult Error(string message) =>
        new(UpdateCheckStatus.Error, null, null, message);
}

public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private static readonly Regex Format = new(
        @"^v?(?<number>\d+\.\d+\.\d+)(?:-beta(?:\.(?<beta>\d+))?)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private ReleaseVersion(Version numericVersion, int? betaNumber)
    {
        NumericVersion = numericVersion;
        BetaNumber = betaNumber;
    }

    public Version NumericVersion { get; }
    public int? BetaNumber { get; }
    public bool IsBeta => BetaNumber.HasValue;

    public static ReleaseVersion FromVersion(Version version)
    {
        if (version is null || version.Build < 0)
        {
            throw new ArgumentException("版本号必须包含主版本、次版本和修订号。", nameof(version));
        }

        return new ReleaseVersion(new Version(version.Major, version.Minor, version.Build), null);
    }

    public static bool TryParse(string? tag, out ReleaseVersion version)
    {
        var match = Format.Match(tag?.Trim() ?? string.Empty);
        if (match.Success && Version.TryParse(match.Groups["number"].Value, out var numericVersion))
        {
            var betaNumber = match.Groups["beta"].Success
                ? int.Parse(match.Groups["beta"].Value, System.Globalization.CultureInfo.InvariantCulture)
                : match.Value.IndexOf("-beta", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : (int?)null;
            version = new ReleaseVersion(numericVersion, betaNumber);
            return true;
        }

        version = null!;
        return false;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var numericComparison = NumericVersion.CompareTo(other.NumericVersion);
        if (numericComparison != 0)
        {
            return numericComparison;
        }

        if (IsBeta != other.IsBeta)
        {
            return IsBeta ? -1 : 1;
        }

        return IsBeta ? BetaNumber!.Value.CompareTo(other.BetaNumber!.Value) : 0;
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);
    public override int GetHashCode() => unchecked((NumericVersion.GetHashCode() * 397) ^ (BetaNumber ?? -1));
    public override string ToString() => IsBeta
        ? $"{NumericVersion}-beta" + (BetaNumber > 0 ? $".{BetaNumber}" : string.Empty)
        : NumericVersion.ToString();
}

public sealed class UpdateService
{
    private const string GitHubReleasesUrl =
        "https://api.github.com/repos/Unke-cc/Win-Mouse-Fix/releases?per_page=100";
    private const string GiteeReleasesUrl =
        "https://gitee.com/api/v5/repos/unkecc/Win-Mouse-Fix/releases?page=1&per_page=100";
    private const string GitHubHomepageUrl = "https://github.com/Unke-cc/Win-Mouse-Fix";
    private const string GiteeHomepageUrl = "https://gitee.com/unkecc/Win-Mouse-Fix";
    private const string GiteeReleasePageBaseUrl =
        "https://gitee.com/unkecc/Win-Mouse-Fix/releases/tag/";
    private const int PreferredSourceTimeoutSeconds = 3;

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? SharedHttpClient;
    }

    public static string GetProjectHomepageUrl(UpdateSource source) =>
        source == UpdateSource.Gitee ? GiteeHomepageUrl : GitHubHomepageUrl;

    public static string GetLicenseUrl(UpdateSource source) =>
        GetProjectHomepageUrl(source) + "/blob/main/LICENSE.md";

    public async Task<SourceResolutionResult> ResolvePreferredSourceAsync()
    {
        var githubFailure = await ProbeSourceAsync(UpdateSource.GitHub);
        if (githubFailure is null)
        {
            return SourceResolutionResult.Available(UpdateSource.GitHub);
        }

        var giteeFailure = await ProbeSourceAsync(UpdateSource.Gitee);
        if (giteeFailure is null)
        {
            return SourceResolutionResult.Available(UpdateSource.Gitee);
        }

        return SourceResolutionResult.Unavailable(
            $"GitHub {githubFailure}；Gitee {giteeFailure}。项目链接暂不可用，请检查网络后重试。");
    }

    public Task<UpdateCheckResult> CheckAsync(Version currentVersion, bool includeBeta) =>
        CheckAsync(ReleaseVersion.FromVersion(currentVersion), includeBeta);

    public async Task<UpdateCheckResult> CheckAsync(ReleaseVersion currentVersion, bool includeBeta)
    {
        if (currentVersion is null)
        {
            throw new ArgumentNullException(nameof(currentVersion));
        }

        try
        {
            var release = await GetLatestReleaseAsync(
                GitHubReleasesUrl,
                UpdateSource.GitHub,
                includeBeta,
                currentVersion);
            return release is null
                ? UpdateCheckResult.NoRelease(UpdateSource.GitHub)
                : UpdateCheckResult.FromRelease(release, currentVersion);
        }
        catch (Exception ex) when (IsSourceFailure(ex))
        {
            return await CheckGiteeAsync(currentVersion, includeBeta, DescribeSourceFailure(ex, "GitHub"));
        }
    }

    private async Task<UpdateCheckResult> CheckGiteeAsync(
        ReleaseVersion currentVersion,
        bool includeBeta,
        string githubFailure)
    {
        try
        {
            var release = await GetLatestReleaseAsync(
                GiteeReleasesUrl,
                UpdateSource.Gitee,
                includeBeta,
                currentVersion);
            return release is null
                ? UpdateCheckResult.NoRelease(UpdateSource.Gitee)
                : UpdateCheckResult.FromRelease(release, currentVersion);
        }
        catch (Exception ex) when (IsSourceFailure(ex))
        {
            return UpdateCheckResult.Error(
                $"{githubFailure}；{DescribeSourceFailure(ex, "Gitee")}。请检查网络后重试。");
        }
    }

    private async Task<string?> ProbeSourceAsync(UpdateSource source)
    {
        try
        {
            var address = source == UpdateSource.GitHub ? GitHubReleasesUrl : GiteeReleasesUrl;
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd("WinMouseFix/source-check");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(PreferredSourceTimeoutSeconds));
            using var response = await httpClient.SendAsync(request, cancellation.Token);
            response.EnsureSuccessStatusCode();
            return null;
        }
        catch (Exception ex) when (IsSourceFailure(ex))
        {
            return DescribeProbeFailure(ex);
        }
    }

    private async Task<UpdateRelease?> GetLatestReleaseAsync(
        string address,
        UpdateSource source,
        bool includeBeta,
        ReleaseVersion clientVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd($"WinMouseFix/{clientVersion.NumericVersion}");
        using var response = await SendSourceRequestAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Release response must be an array.");
        }

        var eligibleReleaseFound = false;
        UpdateRelease? latest = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Release item must be an object.");
            }

            var tag = ReadString(item, "tag_name");
            if (!ReleaseVersion.TryParse(tag, out var version))
            {
                continue;
            }

            if (ReadBoolean(item, "draft") || (!includeBeta && (ReadBoolean(item, "prerelease") || version.IsBeta)))
            {
                continue;
            }

            eligibleReleaseFound = true;
            var pageUrl = source == UpdateSource.Gitee
                ? BuildGiteeReleasePage(tag!)
                : ReadString(item, "html_url");
            if (!IsAllowedReleasePage(pageUrl, source))
            {
                continue;
            }

            var displayName = ReadString(item, "name");
            var release = new UpdateRelease(
                source,
                version,
                string.IsNullOrWhiteSpace(displayName) ? tag! : displayName!,
                pageUrl!,
                ParseArtifact(item, source));
            if (latest is null || release.ReleaseVersion.CompareTo(latest.ReleaseVersion) > 0)
            {
                latest = release;
            }
        }

        if (latest is null && eligibleReleaseFound)
        {
            throw new JsonException("No valid release entry was found.");
        }

        return latest;
    }

    public async Task<DownloadedUpdate> DownloadAndVerifyAsync(
        UpdateCheckResult update,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!update.CanDownload || update.ReleaseVersion is null || update.Artifact is null || !update.Source.HasValue)
        {
            throw new InvalidOperationException("当前没有可下载的更新。");
        }

        try
        {
            return await DownloadArtifactAsync(update.Source.Value, update.ReleaseVersion, update.Artifact, progress, cancellationToken);
        }
        catch (Exception ex) when (update.Source == UpdateSource.GitHub &&
                                   !cancellationToken.IsCancellationRequested &&
                                   (ex is HttpRequestException or InvalidDataException or TaskCanceledException))
        {
            var fallback = await FindReleaseByVersionAsync(UpdateSource.Gitee, update.ReleaseVersion, cancellationToken);
            if (fallback?.Artifact is null)
            {
                throw;
            }

            return await DownloadArtifactAsync(UpdateSource.Gitee, update.ReleaseVersion, fallback.Artifact, progress, cancellationToken);
        }
    }

    private async Task<UpdateRelease?> FindReleaseByVersionAsync(UpdateSource source, ReleaseVersion version, CancellationToken cancellationToken)
    {
        var address = source == UpdateSource.GitHub ? GitHubReleasesUrl : GiteeReleasesUrl;
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd($"WinMouseFix/update/{version}");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!ReleaseVersion.TryParse(ReadString(item, "tag_name"), out var candidate) || !candidate.Equals(version))
            {
                continue;
            }

            var tag = ReadString(item, "tag_name")!;
            var pageUrl = source == UpdateSource.Gitee ? BuildGiteeReleasePage(tag) : ReadString(item, "html_url");
            if (!IsAllowedReleasePage(pageUrl, source))
            {
                continue;
            }

            return new UpdateRelease(source, candidate, ReadString(item, "name") ?? tag, pageUrl!, ParseArtifact(item, source));
        }

        return null;
    }

    private async Task<DownloadedUpdate> DownloadArtifactAsync(
        UpdateSource source,
        ReleaseVersion version,
        UpdateArtifact artifact,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedDownloadUrl(artifact.DownloadUrl, source))
        {
            throw new InvalidDataException("安装包下载地址无效。");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "WinMouseFix", "updates");
        Directory.CreateDirectory(tempDirectory);
        var targetPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(artifact.FileName)}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, artifact.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("WinMouseFix/update-download");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = artifact.Size > 0 ? artifact.Size : response.Content.Headers.ContentLength ?? 0;
            long received = 0;
            using (var input = await response.Content.ReadAsStreamAsync())
            using (var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read, cancellationToken);
                    received += read;
                    progress?.Report(new DownloadProgress(received, total));
                }
            }

            if (artifact.Size > 0 && received != artifact.Size)
            {
                throw new InvalidDataException("安装包大小校验失败。");
            }

            var expectedHash = await ReadExpectedHashAsync(artifact.Sha256Url, source, cancellationToken);
            using var stream = File.OpenRead(targetPath);
            var actualHash = BitConverter.ToString(SHA256.Create().ComputeHash(stream)).Replace("-", string.Empty);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("安装包完整性校验失败。");
            }

            return new DownloadedUpdate(targetPath, version, source, received, actualHash);
        }
        catch
        {
            TryDelete(targetPath);
            throw;
        }
    }

    private async Task<string> ReadExpectedHashAsync(string address, UpdateSource source, CancellationToken cancellationToken)
    {
        if (!IsAllowedDownloadUrl(address, source))
        {
            throw new InvalidDataException("校验文件下载地址无效。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("WinMouseFix/update-hash");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(content, @"(?i)\b[a-f0-9]{64}\b");
        if (!match.Success)
        {
            throw new InvalidDataException("校验文件格式无效。");
        }

        return match.Value;
    }

    private static UpdateArtifact? ParseArtifact(JsonElement release, UpdateSource source)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = assets.EnumerateArray()
            .Where(asset => asset.ValueKind == JsonValueKind.Object)
            .Select(asset => new
            {
                Name = ReadString(asset, "name"),
                Url = ReadString(asset, "browser_download_url") ?? ReadString(asset, "download_url") ?? ReadString(asset, "url"),
                Size = ReadInt64(asset, "size")
            })
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && !string.IsNullOrWhiteSpace(asset.Url))
            .ToArray();
        var installer = candidates
            .Where(asset => asset.Name!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => asset.Name!.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (installer is null || !IsAllowedDownloadUrl(installer.Url, source))
        {
            return null;
        }

        var installerStem = Path.GetFileNameWithoutExtension(installer.Name);
        var hash = candidates.FirstOrDefault(asset => string.Equals(asset.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(asset => asset.Name!.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) &&
                                                  (asset.Name.StartsWith(installer.Name, StringComparison.OrdinalIgnoreCase) ||
                                                   asset.Name.StartsWith(installerStem, StringComparison.OrdinalIgnoreCase)));
        return hash is null || !IsAllowedDownloadUrl(hash.Url, source)
            ? null
            : new UpdateArtifact(installer.Name!, installer.Url!, installer.Size, hash.Url!);
    }

    private static long ReadInt64(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var text) ? text : 0;
    }

    private static bool IsAllowedDownloadUrl(string? address, UpdateSource source) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, source == UpdateSource.GitHub ? "github.com" : "gitee.com", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    internal static bool TryParseVersion(string? tag, out Version version)
    {
        if (ReleaseVersion.TryParse(tag, out var parsed))
        {
            version = parsed.NumericVersion;
            return true;
        }

        version = new Version();
        return false;
    }

    private static bool IsSourceFailure(Exception exception) =>
        exception is TaskCanceledException or HttpRequestException or JsonException;

    private async Task<HttpResponseMessage> SendSourceRequestAsync(HttpRequestMessage request)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(PreferredSourceTimeoutSeconds));
        return await httpClient.SendAsync(request, cancellation.Token);
    }

    private static string DescribeProbeFailure(Exception exception) =>
        exception is TaskCanceledException ? "连接超时" :
        exception is HttpRequestException ? "暂时无法访问" :
        "返回的版本信息无法读取";

    private static string DescribeSourceFailure(Exception exception, string source) =>
        exception is TaskCanceledException
            ? $"{source} 连接超时"
            : exception is HttpRequestException
                ? $"{source} 暂时无法访问".Trim()
                : $"{source} 返回的版本信息无法读取";

    private static string BuildGiteeReleasePage(string tag) =>
        GiteeReleasePageBaseUrl + Uri.EscapeDataString(tag);

    private static bool IsAllowedReleasePage(string? address, UpdateSource source) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(
            uri.Host,
            source == UpdateSource.GitHub ? "github.com" : "gitee.com",
            StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException($"{propertyName} must be a boolean.")
        };
    }
}

internal sealed class UpdateRelease
{
    public UpdateRelease(UpdateSource source, ReleaseVersion releaseVersion, string displayName, string pageUrl, UpdateArtifact? artifact)
    {
        Source = source;
        ReleaseVersion = releaseVersion;
        DisplayName = displayName;
        PageUrl = pageUrl;
        Artifact = artifact;
    }

    public UpdateSource Source { get; }
    public ReleaseVersion ReleaseVersion { get; }
    public string DisplayName { get; }
    public string PageUrl { get; }
    public UpdateArtifact? Artifact { get; }
}

public sealed class UpdateArtifact
{
    public UpdateArtifact(string fileName, string downloadUrl, long size, string sha256Url) { FileName = fileName; DownloadUrl = downloadUrl; Size = size; Sha256Url = sha256Url; }
    public string FileName { get; }
    public string DownloadUrl { get; }
    public long Size { get; }
    public string Sha256Url { get; }
}

public sealed class DownloadProgress
{
    public DownloadProgress(long bytesReceived, long totalBytes) { BytesReceived = bytesReceived; TotalBytes = totalBytes; }
    public long BytesReceived { get; }
    public long TotalBytes { get; }
    public double Percentage => TotalBytes > 0 ? Math.Min(100, BytesReceived * 100d / TotalBytes) : 0;
}

public sealed class DownloadedUpdate
{
    public DownloadedUpdate(string installerPath, ReleaseVersion version, UpdateSource source, long size, string sha256) { InstallerPath = installerPath; Version = version; Source = source; Size = size; Sha256 = sha256; }
    public string InstallerPath { get; }
    public ReleaseVersion Version { get; }
    public UpdateSource Source { get; }
    public long Size { get; }
    public string Sha256 { get; }
}
