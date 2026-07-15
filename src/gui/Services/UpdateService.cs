using System.Net.Http;
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
    }

    public UpdateCheckStatus Status { get; }
    public UpdateSource? Source { get; }
    // Kept for callers that only need the numeric part of a release version.
    public Version? Version { get; }
    public ReleaseVersion? ReleaseVersion { get; }
    public string? DisplayName { get; }
    public string? PageUrl { get; }
    public string? ErrorMessage { get; }

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
                pageUrl!);
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
    public UpdateRelease(UpdateSource source, ReleaseVersion releaseVersion, string displayName, string pageUrl)
    {
        Source = source;
        ReleaseVersion = releaseVersion;
        DisplayName = displayName;
        PageUrl = pageUrl;
    }

    public UpdateSource Source { get; }
    public ReleaseVersion ReleaseVersion { get; }
    public string DisplayName { get; }
    public string PageUrl { get; }
}
