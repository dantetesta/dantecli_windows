using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DanteCLI.Services;

public sealed class UpdateManifest
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("publishedAt")] public string? PublishedAt { get; set; }
    [JsonPropertyName("releaseNotes")] public string? ReleaseNotes { get; set; }
    [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("mac")] public PlatformEntry? Mac { get; set; }
    [JsonPropertyName("windows")] public PlatformEntry? Windows { get; set; }

    public string WindowsDownloadUrl =>
        Windows?.DownloadUrl ?? DownloadUrl ?? "";

    public sealed class PlatformEntry
    {
        [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("minOS")] public string? MinOS { get; set; }
    }
}

public abstract record UpdateState
{
    public sealed record Current(string LatestVersion) : UpdateState;
    public sealed record Available(UpdateManifest Manifest) : UpdateState;
    public sealed record Failure(string Message) : UpdateState;
}

public sealed class UpdateChecker
{
    public static readonly UpdateChecker Shared = new();
    public const string DefaultManifestUrl = "https://dantetesta.com.br/dante-cli/manifest.json";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public DateTime? LastCheckedAt { get; private set; }
    public UpdateState? LastResult { get; private set; }
    public bool IsDownloading { get; private set; }
    public double DownloadProgress { get; private set; }
    public event EventHandler? StateChanged;

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async Task CheckAsync(string? overrideUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(overrideUrl) ? DefaultManifestUrl : overrideUrl;
        try
        {
            var bust = $"{url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var res = await _http.GetAsync(bust);
            if (!res.IsSuccessStatusCode)
            {
                LastResult = new UpdateState.Failure($"HTTP {(int)res.StatusCode}");
                LastCheckedAt = DateTime.Now;
                Notify();
                return;
            }
            var json = await res.Content.ReadAsStringAsync();
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            if (manifest is null || string.IsNullOrEmpty(manifest.Version))
            {
                LastResult = new UpdateState.Failure("Manifest inválido.");
                LastCheckedAt = DateTime.Now;
                Notify();
                return;
            }
            LastResult = Compare(manifest.Version, CurrentVersion) > 0
                ? (UpdateState)new UpdateState.Available(manifest)
                : new UpdateState.Current(manifest.Version);
            LastCheckedAt = DateTime.Now;
            Notify();
        }
        catch (Exception ex)
        {
            LastResult = new UpdateState.Failure(ex.Message);
            LastCheckedAt = DateTime.Now;
            Notify();
        }
    }

    public async Task<(bool ok, string message)> DownloadAndInstallAsync(UpdateManifest manifest)
    {
        var url = manifest.WindowsDownloadUrl;
        if (string.IsNullOrEmpty(url)) return (false, "URL Windows ausente no manifest.");

        IsDownloading = true;
        DownloadProgress = 0;
        Notify();
        try
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            Directory.CreateDirectory(downloads);
            var dest = Path.Combine(downloads, $"dante-cli-{manifest.Version}.exe");
            File.Delete(dest);

            using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? 0L;
            using var inStream = await res.Content.ReadAsStreamAsync();
            using var outStream = File.Create(dest);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await inStream.ReadAsync(buffer)) > 0)
            {
                await outStream.WriteAsync(buffer.AsMemory(0, read));
                written += read;
                if (total > 0)
                {
                    DownloadProgress = (double)written / total;
                    Notify();
                }
            }
            // Reveal in Explorer
            Process.Start("explorer.exe", $"/select,\"{dest}\"");
            return (true, dest);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
            Notify();
        }
    }

    private static int Compare(string a, string b)
    {
        var aP = ParseVersion(a);
        var bP = ParseVersion(b);
        for (int i = 0; i < Math.Max(aP.Length, bP.Length); i++)
        {
            var av = i < aP.Length ? aP[i] : 0;
            var bv = i < bP.Length ? bP[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    private static int[] ParseVersion(string s)
    {
        var parts = s.Split('.');
        var ints = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out ints[i]);
        return ints;
    }
}
