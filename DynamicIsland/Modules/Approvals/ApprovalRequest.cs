using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using DynamicIsland.Core;

namespace DynamicIsland.Modules.Approvals;

abstract class Notifying : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One choice in a question, with the short explanation shown under it.</summary>
sealed class OptionItem(string label, string description) : Notifying
{
    bool _selected;
    public string Label { get; } = label;
    public string Description { get; } = description;
    public bool HasDescription => Description.Length > 0;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>The "Something else…" row at the end of the options.</summary>
sealed class OtherChip(QuestionItem question) { public QuestionItem Question { get; } = question; }

/// <summary>The text field that opens under it once "Something else…" is chosen.</summary>
sealed class OtherInput(QuestionItem question) { public QuestionItem Question { get; } = question; }

/// <summary>One question with a few options (pick one, or several), plus an "Other" answer you type yourself.</summary>
sealed class QuestionItem : Notifying
{
    bool _otherOn;
    string _otherText = "";

    public required string Text { get; init; }
    public required bool Multi { get; init; }
    public string Hint => Multi ? "Choose any that fit" : "Choose one";
    public required IReadOnlyList<OptionItem> Options { get; init; }
    /// <summary>What the answer list shows, in order: the options, the "Something else…" row, then its text field (only visible when chosen).</summary>
    public IReadOnlyList<object> Items => [.. Options, new OtherChip(this), new OtherInput(this)];

    public bool OtherOn { get => _otherOn; private set { if (Set(ref _otherOn, value)) Raise(nameof(CanAnswer)); } }
    public string OtherText { get => _otherText; set { if (Set(ref _otherText, value)) Raise(nameof(CanAnswer)); } }
    public bool CanAnswer => Options.Any(o => o.IsSelected) || (OtherOn && !string.IsNullOrWhiteSpace(OtherText));

    public void Pick(OptionItem option)
    {
        var on = !option.IsSelected;
        if (!Multi) { foreach (var o in Options) o.IsSelected = false; OtherOn = false; }
        option.IsSelected = on;
        Raise(nameof(CanAnswer));
    }

    public void PickOther()
    {
        if (!Multi) foreach (var o in Options) o.IsSelected = false;
        OtherOn = !OtherOn;
        Raise(nameof(CanAnswer));
    }

    /// <summary>The chosen labels (comma-separated when several) and/or your own text, as Claude Code expects them.</summary>
    public string Answer
    {
        get
        {
            var parts = Options.Where(o => o.IsSelected).Select(o => o.Label).ToList();
            if (OtherOn && !string.IsNullOrWhiteSpace(OtherText)) parts.Add(OtherText.Trim());
            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// One "may I?" or question from Claude Code, as sent by its hooks. A permission is answered "allow", "deny" or "ask" (let Claude's own
/// dialog handle it); a question is answered "answer:{json}" with your choices.
/// </summary>
sealed class ApprovalRequest : Notifying
{
    readonly TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _step;
    DateTime _stepShown = DateTime.UtcNow;

    public required string Heading { get; init; }
    public required string Summary { get; init; }
    public required string Project { get; init; }
    public IReadOnlyList<QuestionItem> Questions { get; init; } = [];

    public bool IsQuestion => Questions.Count > 0;
    public bool IsPermission => !IsQuestion;
    public QuestionItem? Question => IsQuestion ? Questions[_step] : null;
    public string StepText => Questions.Count > 1 ? $"{_step + 1} of {Questions.Count}" : "";
    public string SendLabel => _step < Questions.Count - 1 ? "Next" : "Send";

    public ICommand Choose { get; }
    public ICommand Submit { get; }

    public ApprovalRequest()
    {
        Choose = new ParamCommand(p => { if (p is OptionItem o) Question?.Pick(o); else Question?.PickOther(); });
        Submit = new RelayCommand(SubmitStep);
    }

    public Task<string> Answer => _answer.Task;
    /// <summary>Answered, or the hook went away (you answered in the terminal instead).</summary>
    public event Action? Finished;

    public void Resolve(string answer)
    {
        if (_answer.TrySetResult(answer)) Finished?.Invoke();
    }

    void SubmitStep()
    {
        // the next question slides into the same place: a quick double-click must not answer it unseen
        if (Question is not { CanAnswer: true } || (DateTime.UtcNow - _stepShown).TotalMilliseconds < 400) return;
        if (_step < Questions.Count - 1)
        {
            _step++;
            _stepShown = DateTime.UtcNow;
            foreach (var n in new[] { nameof(Question), nameof(StepText), nameof(SendLabel) }) Raise(n);
            return;
        }
        var answers = Questions.ToDictionary(q => q.Text, q => q.Answer);
        Resolve("answer:" + JsonSerializer.Serialize(answers));
    }

    public static ApprovalRequest? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tool = root.TryGetProperty("tool_name", out var t) ? t.GetString() ?? "a tool" : "a tool";
            var input = root.TryGetProperty("tool_input", out var i) ? i : default;
            var cwd = root.TryGetProperty("cwd", out var c) ? c.GetString() ?? "" : "";
            var project = cwd.Length > 0 ? Path.GetFileName(cwd.TrimEnd('\\', '/')) : "";

            if (tool == "AskUserQuestion" && input.ValueKind == JsonValueKind.Object && input.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
            {
                var questions = qs.EnumerateArray().Select(q => new QuestionItem
                {
                    Text = Str(q, "question"),
                    Multi = q.TryGetProperty("multiSelect", out var m) && m.ValueKind == JsonValueKind.True,
                    Options = q.TryGetProperty("options", out var os) && os.ValueKind == JsonValueKind.Array
                        ? [.. os.EnumerateArray().Select(o => new OptionItem(Str(o, "label"), Str(o, "description")))] : [],
                }).Where(q => q.Text.Length > 0).ToList();
                if (questions.Count > 0) return new ApprovalRequest { Heading = "Claude is asking", Summary = "", Project = project, Questions = questions };
            }

            string Field(string name) => Str(input, name);
            var (heading, summary) = tool switch
            {
                "Bash" or "PowerShell" => ("run a command", Field("command")),
                "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => ("change a file", Field("file_path") is { Length: > 0 } p ? p : Field("notebook_path")),
                "Read" or "Glob" or "Grep" => ("read files", Field("file_path") is { Length: > 0 } p ? p : Field("pattern")),
                "WebFetch" => ("open a web page", Field("url")),
                _ => ($"use {tool}", input.ValueKind == JsonValueKind.Undefined ? "" : input.ToString()),
            };
            return new ApprovalRequest
            {
                Heading = "Claude wants to " + heading,
                Summary = summary.Length > 400 ? summary[..400] + "…" : summary,
                Project = project,
            };
        }
        catch { return null; }
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
