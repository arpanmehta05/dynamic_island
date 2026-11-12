using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.Core;

/// <summary>
/// VLC's optional built-in web interface (local only). It is the one way to learn a VLC's exact state (playing/paused, position, length)
/// and to control it precisely (seek, ±10 s, next/previous). It is switched on in VLC's settings; the port and password are stored in
/// %LocalAppData%\DynamicIsland\vlc-web.json by tools\enable-vlc-controls.ps1. Without that file the island simply does without it.
/// </summary>
sealed class VlcWeb
{
    public readonly record struct Status(string State, double Time, double Length, string Title);

    static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "vlc-web.json");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(900) };

    readonly string _base;
    readonly AuthenticationHeaderValue _auth;

    VlcWeb(int port, string password)
    {
        _base = $"http://127.0.0.1:{port}/requests/status.json";
        _auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + password)));
    }

    /// <summary>The configured connection, or null when the web interface hasn't been set up. DI_VLC_WEB=port:password overrides (for testing).</summary>
    public static VlcWeb? FromSettings()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("DI_VLC_WEB") is { Length: > 0 } env && env.Split(':', 2) is [var p, var pw] && int.TryParse(p, out var ep)) return new VlcWeb(ep, pw);
            if (!File.Exists(SettingsFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            var port = doc.RootElement.TryGetProperty("port", out var pe) ? pe.GetInt32() : 8080;
            var password = doc.RootElement.TryGetProperty("password", out var pwe) ? pwe.GetString() ?? "" : "";
            return password.Length > 0 ? new VlcWeb(port, password) : null;
        }
        catch { return null; }
    }

    /// <summary>VLC's current state, or null when it isn't answering (web interface off, wrong password, VLC closed).</summary>
    public async Task<Status?> GetStatusAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _base) { Headers = { Authorization = _auth } };
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var title = "";
            if (root.TryGetProperty("information", out var info) && info.TryGetProperty("category", out var cat) && cat.TryGetProperty("meta", out var meta))
                title = Text(meta, "title") is { Length: > 0 } t ? t : Text(meta, "filename");

            return new Status(Text(root, "state"), Number(root, "time"), Number(root, "length"), title);
        }
        catch { return null; }
    }

    /// <summary>pl_pause (toggles), pl_next, pl_previous, or seek with a value in seconds ("120", "+10", "-10").</summary>
    public async Task SendAsync(string command, string? value = null)
    {
        try
        {
            var url = _base + "?command=" + Uri.EscapeDataString(command) + (value is null ? "" : "&val=" + Uri.EscapeDataString(value));
            using var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Authorization = _auth } };
            using var response = await Http.SendAsync(request);
        }
        catch { /* VLC went away */ }
    }

    static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double Number(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
