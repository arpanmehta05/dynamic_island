using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using DynamicIsland.Core;

namespace DynamicIsland.Modules.Agents;

/// <summary>
/// The ChatGPT desktop app (installed here as the OpenAI Codex package). It compresses its session logs and keeps no live
/// transcript, so what it shows is: whether it is open, its latest thread's title, its own pet (read from its pet folder),
/// and a best-effort "busy" signal. That signal is an estimate: sustained CPU use by the app's processes (or its database
/// logs changing while it uses CPU), seen at least twice within ten seconds. An idle app uses essentially no CPU, so a task
/// that is really running stands out.
/// </summary>
sealed class ChatGptAgent : AgentInfo
{
    static readonly string CodexDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    static readonly string IndexFile = Path.Combine(CodexDir, "session_index.jsonl");
    static readonly string[] ActivityFiles = ["logs_2.sqlite-wal", "state_5.sqlite-wal"];
    static readonly TimeSpan PulseWindow = TimeSpan.FromSeconds(10);
    const double BusyCpuPercent = 5, LogCpuPercent = 1; // of one core, averaged since the previous refresh

    string _thread = "";
    DateTimeOffset _updated = DateTimeOffset.MinValue;
    DateTime _indexStamp;
    SpriteSet? _sprites;

    double _lastCpuMs = -1;
    DateTime _lastSampleAt;
    string _lastLogStamp = "";
    readonly List<DateTime> _pulses = [];

    public override string Name => "ChatGPT";
    public override string Glyph => "◎"; // ◎, shown only when the app's own icon can't be read
    public override string Accent => "#10A37F";
    protected override string[] ProcessNames => ["chatgpt"];
    public override System.Windows.Media.Geometry? Logo => Logos.OpenAI;
    protected override bool OnlyWhileChatting => true;
    public override SpriteSet? Sprites => _sprites;

    public ChatGptAgent()
    {
        // The sprite sheet decodes to ~14 MB: do it in the background and keep only the frames that are used.
        Task.Run(SpriteSet.LoadFromCodex).ContinueWith(t =>
        {
            if (t.Result is not { } set) return;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _sprites = set;
                Raise(nameof(Sprites));
                Raise(nameof(HasSprite));
                Raise(nameof(NoPet));

                // The decoded sheet (~14 MB) and its decoder are garbage now: release them instead of waiting for the next collection.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }, TaskScheduler.Default);
    }

    protected override void Update(bool detailed)
    {
        ReadIndex();
        IsWorking = IsRunning && DetectBusy();

        Headline = !IsRunning ? "ChatGPT isn't running"
            : IsWorking ? "Working  ·  busy on a task"
            : _updated > DateTimeOffset.MinValue ? $"Open  ·  last thread {Ago(DateTimeOffset.UtcNow - _updated)}" : "Open";
        Body = Clip(_thread, 150);
        Meta = IsRunning ? "Activity is estimated: the app shows no live status" : "";
    }

    /// <summary>Counts "activity pulses" (CPU use, or log writes alongside CPU use) and reports busy when there are two within ten seconds.</summary>
    bool DetectBusy()
    {
        var now = DateTime.UtcNow;
        double cpu = 0;
        foreach (var pid in Pids)
        {
            try { using var p = Process.GetProcessById(pid); cpu += p.TotalProcessorTime.TotalMilliseconds; }
            catch { /* ended, or not ours to read */ }
        }

        var logStamp = string.Join('|', ActivityFiles.Select(f => File.GetLastWriteTimeUtc(Path.Combine(CodexDir, f)).Ticks));
        if (_lastCpuMs >= 0)
        {
            var elapsedMs = Math.Max(1, (now - _lastSampleAt).TotalMilliseconds);
            var percent = Math.Max(0, cpu - _lastCpuMs) / elapsedMs * 100;
            var logsChanged = logStamp != _lastLogStamp;
            if (percent >= BusyCpuPercent || (logsChanged && percent >= LogCpuPercent)) _pulses.Add(now);
        }
        _lastCpuMs = cpu;
        _lastSampleAt = now;
        _lastLogStamp = logStamp;

        _pulses.RemoveAll(t => now - t > PulseWindow);
        return _pulses.Count >= 2;
    }

    void ReadIndex()
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(IndexFile);
            if (stamp == _indexStamp) return;
            _indexStamp = stamp;

            using var fs = new FileStream(IndexFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var n = (int)Math.Min(fs.Length, 16384);
            fs.Seek(-n, SeekOrigin.End);
            var buf = new byte[n];
            _ = fs.Read(buf, 0, n);
            var last = System.Text.Encoding.UTF8.GetString(buf).Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(l => l.TrimStart().StartsWith('{'));
            if (last is null) return;

            using var doc = JsonDocument.Parse(last);
            if (doc.RootElement.TryGetProperty("thread_name", out var t)) _thread = t.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("updated_at", out var u))
                _updated = u.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(u.GetInt64()) :
                           DateTimeOffset.TryParse(u.GetString(), out var d) ? d : _updated;
        }
        catch { /* file missing or mid-write: keep what we had */ }
    }
}
