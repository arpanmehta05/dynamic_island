using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Modules.Agents;

/// <summary>
/// GitHub Copilot Chat in VS Code. VS Code saves each chat as a .jsonl file under workspaceStorage/*/chatSessions and keeps
/// appending while an answer streams, so "file changed a moment ago" means Copilot is answering. The format is internal to
/// VS Code, so the prompt and model are pulled out leniently.
/// </summary>
sealed partial class CopilotAgent : AgentInfo
{
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "workspaceStorage");

    FileSystemWatcher? _watcher;
    string? _file;
    volatile bool _dirty = true;
    string _prompt = "", _title = "", _model = "";

    public override string Name => "Copilot";
    public override string Glyph => "\uE943"; // Segoe Fluent Icons: code (</>)
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#4C9BFF";
    protected override string[] ProcessNames => ["code"];

    public CopilotAgent()
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
        if (!e.FullPath.Contains("chatSessions", StringComparison.OrdinalIgnoreCase)) return;
        _file = e.FullPath;
        _dirty = true;
    }

    static string? Newest() => Directory.EnumerateDirectories(Root)
        .Select(d => Path.Combine(d, "chatSessions")).Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl"))
        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    protected override void Update(bool detailed)
    {
        if (_file is null || !File.Exists(_file)) { IsWorking = false; Headline = IsRunning ? "No Copilot chats yet" : "VS Code isn't running"; Body = Meta = ""; return; }
        if (detailed && _dirty) { _dirty = false; Parse(); }

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(_file);
        var active = IsRunning && age < TimeSpan.FromSeconds(8);
        IsWorking = active;

        Headline = !IsRunning ? "VS Code isn't running" : active ? "Working  ·  Copilot Chat" : $"Idle  ·  last chat {Ago(age)}";
        Body = Clip(_prompt.Length > 0 ? _prompt : _title, 150);
        Meta = Join(_model);
    }

    void Parse()
    {
        try
        {
            using var fs = new FileStream(_file!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var text = sr.ReadToEnd();
            _prompt = Last(PromptRx(), text) is { Length: > 0 } p ? p : _prompt;
            _title = Last(TitleRx(), text) is { Length: > 0 } t ? t : _title;
            _model = Last(ModelRx(), text) is { Length: > 0 } m ? m : _model;
        }
        catch (IOException) { /* being written */ }
    }

    static string Last(Regex rx, string text)
    {
        var m = rx.Matches(text);
        if (m.Count == 0) return "";
        try { return JsonSerializer.Deserialize<string>($"\"{m[^1].Groups[1].Value}\"") ?? ""; } catch { return ""; }
    }

    [GeneratedRegex("\"message\":\\{\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"")] private static partial Regex PromptRx();
    [GeneratedRegex("\"customTitle\":\"((?:[^\"\\\\]|\\\\.)*)\"")] private static partial Regex TitleRx();
    [GeneratedRegex("\"modelId\":\"((?:[^\"\\\\]|\\\\.)*)\"")] private static partial Regex ModelRx();
}
