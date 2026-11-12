using System.Windows;
using System.Windows.Input;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Modules.Approvals;

/// <summary>
/// Claude Code's questions ("run this command?", "edit this file?") with Allow and Deny. The button exists only while something is waiting,
/// and the island opens by itself when a question arrives. Several at once queue up; the oldest is shown.
/// </summary>
sealed class ApprovalEntry : TabEntry
{
    readonly List<ApprovalRequest> _queue = [];
    DateTime _shownAt;

    public override ViewKind Kind => ViewKind.Approval;
    public override string Name => "Claude is asking";
    public override string Glyph => "";
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#FFB454";
    public override bool IsAvailable => _queue.Count > 0;

    public ApprovalRequest? Current => _queue.FirstOrDefault();
    public ICommand Allow { get; }
    public ICommand Deny { get; }
    public ICommand Later { get; }

    /// <summary>Raised with true when a question arrives (open the island) and false when none is left.</summary>
    public event Action<bool>? Attention;

    public ApprovalEntry()
    {
        IsRunning = true;
        Allow = new RelayCommand(() => Answer("allow"));
        Deny = new RelayCommand(() => Answer("deny"));
        Later = new RelayCommand(() => Answer("ask"));
        var server = new ApprovalServer();
        server.Requested += request => Application.Current.Dispatcher.BeginInvoke(() => Add(request));
    }

    void Add(ApprovalRequest request)
    {
        request.Finished += () => Application.Current.Dispatcher.BeginInvoke(() => Remove(request));
        _queue.Add(request);
        Changed();
        Attention?.Invoke(true);
    }

    void Remove(ApprovalRequest request)
    {
        if (!_queue.Remove(request)) return;
        Changed();
        if (_queue.Count == 0) Attention?.Invoke(false);
    }

    void Answer(string answer)
    {
        // the next question slides into the same spot: ignore taps right after it appears so a double-click can't answer it unseen
        if (Current is { } request && (DateTime.UtcNow - _shownAt).TotalMilliseconds > 500) request.Resolve(answer);
    }

    void Changed()
    {
        _shownAt = DateTime.UtcNow;
        IsWorking = _queue.Count > 0;
        Badge = _queue.Count > 1 ? _queue.Count.ToString() : null;
        Raise(nameof(Current));
        Raise(nameof(IsAvailable));
    }
}
