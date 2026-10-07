using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrbitWeave.Updates;

// Dernière version publiée sur GitHub : son numéro et son installateur.
public sealed record Release(Version Version, string SetupUrl, string? Sha256);

public static class ReleaseInfo
{
    // Réponse de api.github.com/repos/…/releases/latest : tag « v1.2.3 », installateur « …-Setup.exe » parmi les fichiers joints.
    public static Release? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || ParseTag(tag.GetString()) is not { } version) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null || !name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!asset.TryGetProperty("browser_download_url", out var url) || url.GetString() is not { Length: > 0 } link) continue;
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
                return new Release(version, link, sha);
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    public static Version? ParseTag(string? tag) =>
        Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out var version) ? Normalize(version) : null;

    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    // 1.2 = 1.2.0 = 1.2.0.0 : seuls les trois premiers numéros comptent.
    public static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static string Display(Version v) => Normalize(v).ToString(3);
}

public enum UpdateState { NotChecked, Checking, UpToDate, Available, Downloading, Failed }

// Regarde au démarrage puis toutes les 6 heures si GitHub a une version plus récente.
// « Mettre à jour » télécharge l'installateur, le lance en silence et quitte : l'installateur relance la roue.
public static class Updater
{
    public const string Repository = "gfloxe/OrbitWeave";
    public static readonly string Page = $"https://github.com/{Repository}/releases/latest";

    public static Version Current { get; } = DiagnosticFlags.PretendVersion is { } pretend && ReleaseInfo.ParseTag(pretend) is { } v
        ? v : ReleaseInfo.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // Seule la copie installée se remplace elle-même ; celle compilée depuis le code se met à jour par le code.
    public static bool CanInstall => LaunchPolicy.IsInstalledCopy(Environment.ProcessPath);

    public static UpdateState State { get; private set; } = UpdateState.NotChecked;
    public static Release? Latest { get; private set; }
    public static DateTime LastCheck { get; private set; }
    public static event Action? Changed;

    private static readonly HttpClient Http = CreateClient();
    private static System.Windows.Threading.DispatcherTimer? _timer;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"OrbitWeave/{ReleaseInfo.Display(Current)}");
        return client;
    }

    public static void Start()
    {
        if (_timer is not null) return;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += async (_, _) => { _timer.Interval = TimeSpan.FromHours(6); await CheckAsync(); };
        _timer.Start();
    }

    public static async Task CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading) return;
        Set(UpdateState.Checking);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await Http.SendAsync(request, timeout.Token);
            LastCheck = DateTime.Now;
            // Aucune version publiée pour l'instant : rien de plus récent.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) { Latest = null; Set(UpdateState.UpToDate); return; }
            response.EnsureSuccessStatusCode();
            Latest = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            Set(Latest is not null && ReleaseInfo.IsNewer(Latest.Version, Current) ? UpdateState.Available : UpdateState.UpToDate);
        }
        catch (Exception ex)
        {
            LastCheck = DateTime.Now;
            CrashLog.Write("Updater.Check", ex.Message);
            Set(UpdateState.Failed);
        }
    }

    // Retourne un message d'erreur, ou null si l'installateur est lancé (l'appli se ferme alors).
    public static async Task<string?> InstallAsync()
    {
        if (Latest is not { } release || !CanInstall) return "Aucune mise à jour à installer.";
        Set(UpdateState.Downloading);
        try
        {
            var file = Path.Combine(Path.GetTempPath(), $"OrbitWeave-{ReleaseInfo.Display(release.Version)}-Setup.exe");
            using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(file);
                await response.Content.CopyToAsync(output);
            }
            if (release.Sha256 is { } expected)
            {
                await using var input = File.OpenRead(file);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    Set(UpdateState.Available);
                    return "Le fichier téléchargé est abîmé. Réessaie plus tard.";
                }
            }
            // L'installateur attend que la roue soit fermée, remplace les fichiers et relance OrbitWeave.
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
            return null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Updater.Install", ex);
            Set(UpdateState.Available);
            return "La mise à jour n'a pas pu être téléchargée. Vérifie la connexion et réessaie.";
        }
    }

    private static void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
