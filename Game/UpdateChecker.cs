using System.Reflection;
using System.Text.Json;

namespace JigsawPuzzle.Game;

/// <summary>Checks the GitHub repository for a released version newer than this build.</summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/anujshroff/JigsawPuzzle/releases/latest";

    /// <summary>Where users go to download the newest build.</summary>
    public const string ReleasesPage = "https://github.com/anujshroff/JigsawPuzzle/releases/latest";

    /// <summary>
    /// The newer released version, or null when up to date or undeterminable
    /// (offline, rate-limited, no releases yet) — callers can stay quiet on null.
    /// </summary>
    public static async Task<Version?> CheckAsync()
    {
        try
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version;
            if (current is null)
                return null;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JigsawPuzzle"); // GitHub API requires a User-Agent
            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseApi));

            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;

            // Compare major.minor.patch only; AssemblyVersion carries a fourth ".0" part.
            var newer = (latest.Major, latest.Minor, Math.Max(latest.Build, 0))
                .CompareTo((current.Major, current.Minor, Math.Max(current.Build, 0))) > 0;
            return newer ? latest : null;
        }
        catch
        {
            return null;
        }
    }
}
