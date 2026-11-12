using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;
using WpfClipboard = System.Windows.Clipboard;

namespace DynamicIsland.Modules.Clip;

/// <summary>One remembered copy.</summary>
sealed class ClipItem : INotifyPropertyChanged
{
    bool _justPasted;

    public string Text { get; }
    public string Preview { get; }
    public string Meta { get; }
    /// <summary>Briefly true after being pasted, so the row can flash.</summary>
    public bool JustPasted { get => _justPasted; set { if (_justPasted == value) return; _justPasted = value; PropertyChanged?.Invoke(this, new(nameof(JustPasted))); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ClipItem(string text)
    {
        Text = text;
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Preview = flat.Length <= 120 ? flat : flat[..119] + "…";
        var lines = text.Count(c => c == '\n') + 1;
        Meta = lines > 1 ? $"{lines} lines" : $"{text.Length} chars";
    }
}

/// <summary>
/// Clipboard history (text) and one-click paste. Copies are noticed through the shared clipboard listener, so nothing polls.
/// Pasting puts the chosen text on the clipboard and sends Ctrl+V to the window you were typing in: the island never takes focus,
/// so that window is still the active one. History lives in memory only and is gone when the island closes; copies that password
/// managers mark as sensitive are not recorded.
/// </summary>
sealed class ClipboardEntry : TabEntry
{
    const int MaxItems = 20, MaxChars = 50_000;

    DateTime _ignoreUntil = DateTime.MinValue; // our own clipboard writes must not be recorded again

    public override ViewKind Kind => ViewKind.Clipboard;
    public override string Name => "Clipboard";
    public override string Glyph => ""; // Segoe Fluent Icons: paste
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#4DD0A0";

    public ObservableCollection<ClipItem> Entries { get; } = [];
    public bool IsEmpty => Entries.Count == 0;
    public ICommand Paste { get; }
    public ICommand Clear { get; }

    public ClipboardEntry(ClipboardMonitor monitor)
    {
        IsRunning = true;
        Entries.CollectionChanged += (_, _) => Raise(nameof(IsEmpty));
        Paste = new ParamCommand(p => { if (p is ClipItem item) _ = PasteAsync(item); });
        Clear = new RelayCommand(() => Entries.Clear());
        monitor.Started += Capture; // whatever is on the clipboard right now
        monitor.Changed += Capture;
    }

    void Capture()
    {
        if (DateTime.UtcNow < _ignoreUntil) return;
        string? text = null;
        for (var attempt = 0; attempt < 5 && text is null; attempt++)
        {
            try
            {
                if (WpfClipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return; // password managers set this
                if (!WpfClipboard.ContainsText()) return;
                text = WpfClipboard.GetText();
            }
            catch (COMException) { Thread.Sleep(20); } // the app that just copied still has the clipboard open
        }
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaxChars) text = text[..MaxChars];

        var same = Entries.FirstOrDefault(e => e.Text == text);
        if (same is not null) Entries.Remove(same);
        Entries.Insert(0, new ClipItem(text));
        while (Entries.Count > MaxItems) Entries.RemoveAt(Entries.Count - 1);
    }

    async Task PasteAsync(ClipItem item)
    {
        _ignoreUntil = DateTime.UtcNow.AddMilliseconds(700);
        if (!SetText(item.Text)) return;
        item.JustPasted = true;
        await Task.Delay(70); // let the clipboard settle before the target app reads it
        KeySender.CtrlV();
        await Task.Delay(900);
        item.JustPasted = false;
    }

    static bool SetText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { WpfClipboard.SetText(text); return true; }
            catch (COMException) { Thread.Sleep(20); }
        }
        return false;
    }
}
