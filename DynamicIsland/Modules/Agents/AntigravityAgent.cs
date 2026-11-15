using System.IO;

namespace DynamicIsland.Modules.Agents;

/// <summary>
/// Google Antigravity. Each conversation is a SQLite file under ~/.gemini/antigravity/conversations that is written while the
/// agent answers (the -wal file too), so "written a moment ago" means it is working. The files are internal, so no prompt is read.
/// </summary>
sealed class AntigravityAgent : AgentInfo
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "conversations");

    public override string Name => "Antigravity";
    public override string Glyph => "\uE945"; // Segoe Fluent Icons: shown only when the app's own icon can't be read
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#4C8DF6";
    protected override string[] ProcessNames => ["antigravity"];
    protected override bool OnlyWhileChatting => true;

    protected override void Update(bool detailed)
    {
        var last = Directory.Exists(Dir)
            ? Directory.EnumerateFiles(Dir).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
            : DateTime.MinValue;
        var age = DateTime.UtcNow - last;
        IsWorking = IsRunning && age < TimeSpan.FromSeconds(8);

        Headline = !IsRunning ? "Antigravity isn't running"
            : IsWorking ? "Working  ·  Antigravity agent"
            : last > DateTime.MinValue ? $"Idle  ·  last chat {Ago(age)}" : "No chats yet";
        Body = Meta = "";
    }
}
