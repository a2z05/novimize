using System.Security.Cryptography;
using System.Text.Json;
using WinOpt.Core;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Release;

/// <summary>
/// Whether a newer Novimize has been published, and where to get it.
///
/// Two rules. The check only ever reads — it never downloads an executable
/// because a page loaded, and the download is a separate, confirmed action.
/// And when the file arrives, its SHA-256 is compared against the digest
/// GitHub published next to it; a mismatch is reported as a failure rather
/// than as a file that is there.
///
/// The version it compares against is <see cref="AppVersion.Value"/>, the one
/// a test holds to tauri.conf.json, so "up to date" cannot disagree with the
/// installer that is running.
/// </summary>
public sealed class ReleaseUpdater
{
    public const string ReleasesUrl = "https://api.github.com/repos/a2z05/novimize/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45),
    };

    static ReleaseUpdater()
    {
        // GitHub requires a user agent, and identifying the tool rather than
        // the default one is what their rate limits are keyed to.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"Novimize/{AppVersion.Value}");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<AppUpdateStatus> CheckAsync(CancellationToken cancel = default)
    {
        var current = AppVersion.Value;
        try
        {
            using var response = await Http.GetAsync(ReleasesUrl, cancel);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(current, $"GitHub answered {(int)response.StatusCode} for the release feed.");
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
            var root = document.RootElement;

            if (root.TryGetProperty("message", out var message))
                return Failed(current, message.GetString() ?? "The release feed did not say why it failed.");

            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var latest = NormalizeVersion(tag);
            var assets = new List<ReleaseAsset>();

            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                    assets.Add(new ReleaseAsset
                    {
                        Name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "",
                        ContentType = asset.TryGetProperty("content_type", out var c) ? c.GetString() ?? "" : "",
                        SizeBytes = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0,
                        Sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                            ? digest["sha256:".Length..]
                            : null,
                        UpdatedAt = asset.TryGetProperty("updated_at", out var ua)
                            && DateTimeOffset.TryParse(ua.GetString(), out var at) ? at : null,
                    });
                }
            }

            var available = IsNewer(current, latest);
            return new AppUpdateStatus
            {
                CurrentVersion = current,
                LatestVersion = latest,
                Tag = tag,
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Url = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "",
                PublishedAt = root.TryGetProperty("published_at", out var pub)
                    && DateTimeOffset.TryParse(pub.GetString(), out var when) ? when : null,
                Assets = assets,
                Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                UpdateAvailable = available,
            };
        }
        catch (OperationCanceledException)
        {
            return Failed(current, "The release feed did not answer in time.");
        }
        catch (Exception ex)
        {
            return Failed(current, ex.Message);
        }
    }

    private static AppUpdateStatus Failed(string current, string error) => new()
    {
        CurrentVersion = current,
        LatestVersion = current,
        CheckFailed = true,
        UpdateAvailable = false,
        Error = error,
    };

    /// <summary>
    /// Fetch one asset and check it against the digest the feed published.
    /// Nothing is executed: the file lands under %LOCALAPPDATA%\WinOpt\updates
    /// and running it is the user's decision, made in a place where they can
    /// see what it is.
    /// </summary>
    public async Task<AppUpdateStatus> DownloadAsync(
        string? assetName, bool confirm, CancellationToken cancel = default)
    {
        var status = await CheckAsync(cancel);
        if (status.CheckFailed) return status;

        var asset = status.Assets.FirstOrDefault(a =>
                        !string.IsNullOrWhiteSpace(assetName)
                            ? a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase)
                            : a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    ?? status.Assets.FirstOrDefault();

        if (asset is null)
            return status with { Error = "That release has no downloadable file." };

        if (!confirm)
        {
            return status with
            {
                UpdateAvailable = status.UpdateAvailable,
                Error = null,
                DownloadedPath = null,
                DigestNote = $"Nothing has been downloaded. {asset.Name} is {Format(asset.SizeBytes)} from {new Uri(asset.Url).Host}.",
            };
        }

        if (string.IsNullOrWhiteSpace(asset.Url))
            return status with { Error = "That release has no download address." };

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "updates");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, Path.GetFileName(asset.Name));
        try
        {
            await using var input = await Http.GetStreamAsync(asset.Url, cancel);
            await using var output = File.Create(path);
            await input.CopyToAsync(output, cancel);

            var bytes = new FileInfo(path).Length;
            string? actual = null;
            await using (var stream = File.OpenRead(path))
            {
                var hash = await SHA256.HashDataAsync(stream, cancel);
                actual = Convert.ToHexString(hash).ToLowerInvariant();
            }

            var verified = asset.Sha256 is null
                ? (bool?)null
                : actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);

            return status with
            {
                DownloadedPath = path,
                DownloadedBytes = bytes,
                DigestVerified = verified,
                DigestNote = asset.Sha256 is null
                    ? "This release published no checksum, so the bytes could not be verified."
                    : verified == true
                        ? $"SHA-256 matches the digest GitHub published for {asset.Name}."
                        : $"SHA-256 does NOT match: expected {asset.Sha256}, got {actual}. Do not run this file.",
            };
        }
        catch (Exception ex)
        {
            return status with { Error = $"The download failed: {ex.Message}" };
        }
    }

    // --- version comparison -------------------------------------------------

    /// <summary>A tag with or without a leading v, and nothing else.</summary>
    public static string NormalizeVersion(string tag) =>
        string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim().TrimStart('v', 'V');

    /// <summary>
    /// Numeric segment by numeric segment, so 0.10.0 is newer than 0.9.0 —
    /// the comparison a string sort gets wrong, and the one that decides
    /// whether somebody is told to update.
    /// </summary>
    public static bool IsNewer(string current, string latest)
    {
        var a = Parse(NormalizeVersion(current));
        var b = Parse(NormalizeVersion(latest));
        if (b.Length == 0) return false;

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var left = i < a.Length ? a[i] : 0;
            var right = i < b.Length ? b[i] : 0;
            if (right != left) return right > left;
        }
        return false;
    }

    private static int[] Parse(string version) =>
        version.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();

    /// <summary>Bytes the way a person would say them; shared with the CLI.</summary>
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} kB",
        _ => $"{bytes} B",
    };
}
