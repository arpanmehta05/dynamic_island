using System.IO;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.Modules.Agents;

/// <summary>
/// Claude (the desktop app's Code tab and the CLI). Claude Code appends every step of a session to a transcript in
/// ~/.claude/projects; a file watcher marks the newest one and the tail of it tells us what Claude is doing right now.
/// </summary>
sealed class ClaudeAgent : AgentInfo
{
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    const int TailBytes = 384 * 1024, LightTailBytes = 64 * 1024; // collapsed: only the last message is needed

    FileSystemWatcher? _watcher;
    string? _file;
    volatile bool _dirty = true;
    bool _detailed;

    // what the last parse found
    string _prompt = "", _title = "", _project = "", _model = "", _tool = "";
    long _context, _output;
    bool _ended;
    DateTimeOffset _last = DateTimeOffset.MinValue, _turnStart = DateTimeOffset.MinValue;

    public override string Name => "Claude";
    public override string Glyph => "✳"; // ✳
    public override string Accent => "#D97757";
    public override bool HasPet => true;
    protected override string[] ProcessNames => ["claude"];

    public ClaudeAgent()
    {
        if (!Directory.Exists(Root)) return;
        _file = Newest();
        _watcher = new FileSystemWatcher(Root, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains("subagents", StringComparison.OrdinalIgnoreCase)) return;
        _file = e.FullPath;
        _dirty = true;
    }

    static string? Newest() => Directory.EnumerateFiles(Root, "*.jsonl", SearchOption.AllDirectories)
        .Where(f => !f.Contains("subagents", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    protected override void Update(bool detailed)
    {
        // Collapsed: only the last message is needed to know if Claude is working. Expanded: also the prompt, tool, model, tokens.
        if (_dirty || (detailed && !_detailed)) { _dirty = false; Parse(detailed); _detailed = detailed; }

        if (_file is null) { IsWorking = false; Headline = "No Claude sessions yet"; Body = ""; Meta = ""; return; }

        var age = _last == DateTimeOffset.MinValue ? TimeSpan.FromDays(1) : DateTimeOffset.UtcNow - _last;
        var active = IsRunning && !_ended && age < TimeSpan.FromSeconds(90);
        var waiting = IsRunning && !_ended && !active && age < TimeSpan.FromMinutes(30);
        IsWorking = active;

        Headline = !IsRunning ? "Claude isn't running"
            : active ? "Working" + (_tool.Length > 0 ? $"  ·  {_tool}" : "  ·  thinking")
            : waiting ? "Waiting for you" + (_tool.Length > 0 ? $"  ·  {_tool}" : "")
            : $"Idle  ·  finished {Ago(age)}";

        Body = Clip(_prompt.Length > 0 ? _prompt : _title, 150);
        var elapsed = active && _turnStart > DateTimeOffset.MinValue ? Duration(DateTimeOffset.UtcNow - _turnStart) : "";
        Meta = Join(_project, _model, _context > 0 ? $"ctx {K(_context)}" : "", _output > 0 ? $"out {K(_output)}" : "", elapsed);
    }

    void Parse(bool detailed)
    {
        if (_file is null) return;
        string text;
        try
        {
            using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var len = fs.Length;
            var n = (int)Math.Min(len, detailed ? TailBytes : LightTailBytes);
            fs.Seek(len - n, SeekOrigin.Begin);
            var buf = new byte[n];
            var read = 0;
            while (read < n) { var r = fs.Read(buf, read, n - read); if (r <= 0) break; read += r; }
            text = Encoding.UTF8.GetString(buf, 0, read);
            if (len > n) { var nl = text.IndexOf('\n'); text = nl >= 0 ? text[(nl + 1)..] : ""; } // drop the cut-off first line
        }
        catch { return; }

        bool lastSeen = false, asstSeen = false, promptSeen = false, lpSeen = false, titleSeen = false;
        var lines = text.Split('\n');
        for (int i = lines.Length - 1, parsed = 0; i >= 0 && parsed < 150; i--)
        {
            var line = lines[i].TrimEnd('\r');
            // "type" sits among the first few keys (after parentUuid, cwd, …), so look only at the head of the line:
            // tool results can be huge, and the word "assistant" can appear anywhere inside them.
            var head = line.AsSpan(0, Math.Min(line.Length, 700));
            var isAsst = head.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
            var isUser = head.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            var isLp = line.StartsWith("{\"type\":\"last-prompt\"", StringComparison.Ordinal);
            var isTitle = line.StartsWith("{\"type\":\"custom-title\"", StringComparison.Ordinal);

            var wanted = (isLp && !lpSeen) || (isTitle && !titleSeen) || (isAsst && (!asstSeen || !lastSeen)) || (isUser && (!lastSeen || (detailed && !promptSeen)));
            if (!wanted) continue;
            parsed++;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (isLp) { lpSeen = true; if (root.TryGetProperty("lastPrompt", out var lp)) _prompt = lp.GetString() ?? ""; continue; }
                if (isTitle) { titleSeen = true; if (root.TryGetProperty("customTitle", out var t)) _title = t.GetString() ?? ""; continue; }

                var ts = root.TryGetProperty("timestamp", out var tse) && DateTimeOffset.TryParse(tse.GetString(), out var dto) ? dto : DateTimeOffset.MinValue;
                if (_project.Length == 0 && root.TryGetProperty("cwd", out var cwd) && cwd.GetString() is { Length: > 0 } c)
                    _project = Path.GetFileName(c.TrimEnd('\\', '/'));
                if (!root.TryGetProperty("message", out var msg)) continue;

                if (isAsst)
                {
                    var stop = msg.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String ? sr.GetString() : null;
                    if (!lastSeen) { lastSeen = true; _last = ts; _ended = stop is "end_turn" or "stop_sequence" or "max_tokens"; }
                    if (!asstSeen)
                    {
                        asstSeen = true;
                        _tool = ToolSummary(msg);
                        if (msg.TryGetProperty("model", out var m)) _model = (m.GetString() ?? "").Replace("claude-", "");
                        if (msg.TryGetProperty("usage", out var u))
                        {
                            _context = Num(u, "input_tokens") + Num(u, "cache_creation_input_tokens") + Num(u, "cache_read_input_tokens");
                            _output = Num(u, "output_tokens");
                        }
                    }
                }
                else // a user entry: either a real prompt or a tool result
                {
                    if (!lastSeen) { lastSeen = true; _last = ts; _ended = false; }
                    if (!promptSeen && IsRealPrompt(root, msg)) { promptSeen = true; _turnStart = ts; }
                }
            }
            catch (JsonException) { /* half-written line */ }

            if (detailed ? lpSeen && titleSeen && asstSeen && promptSeen : lastSeen && asstSeen) break;
        }
        if (!asstSeen) _tool = "";
    }

    static bool IsRealPrompt(JsonElement root, JsonElement msg)
    {
        if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return false;
        if (!msg.TryGetProperty("content", out var content)) return false;
        if (content.ValueKind == JsonValueKind.String) return true;
        if (content.ValueKind != JsonValueKind.Array) return false;
        var hasText = false;
        foreach (var part in content.EnumerateArray())
        {
            var type = part.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "tool_result") return false;
            if (type == "text") hasText = true;
        }
        return hasText;
    }

    /// <summary>"Edit IslandWindow.xaml", "Bash", "Read …": the last tool Claude called, with its main argument.</summary>
    static string ToolSummary(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return "";
        string tool = "", arg = "";
        foreach (var part in content.EnumerateArray())
        {
            if (!part.TryGetProperty("type", out var t) || t.GetString() != "tool_use") continue;
            tool = part.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            arg = "";
            if (part.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "file_path", "path", "command", "pattern", "description", "query", "url" })
                    if (input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        arg = v.GetString() ?? "";
                        if (key == "file_path" || key == "path") arg = Path.GetFileName(arg);
                        break;
                    }
        }
        tool = tool.StartsWith("mcp__", StringComparison.Ordinal) ? tool[(tool.LastIndexOf("__", StringComparison.Ordinal) + 2)..] : tool;
        return Clip(arg.Length > 0 ? $"{tool} {arg.Replace('\n', ' ')}" : tool, 44);
    }

    static long Num(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    static string K(long n) => n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();
    static string Duration(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{(int)t.TotalSeconds}s";
}
