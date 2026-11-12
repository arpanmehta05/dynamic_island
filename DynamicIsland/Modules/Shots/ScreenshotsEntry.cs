using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;
using WpfClipboard = System.Windows.Clipboard;

namespace DynamicIsland.Modules.Shots;

/// <summary>One screenshot (or copied image) with a small preview.</summary>
sealed class ShotItem : INotifyPropertyChanged
{
    string _caption = "just now";

    public required string Path { get; init; }
    public required BitmapSource Thumbnail { get; init; }
    public required DateTime Taken { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public bool FromClipboard { get; init; }

    public string Caption { get => _caption; private set { if (_caption == value) return; _caption = value; PropertyChanged?.Invoke(this, new(nameof(Caption))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateCaption()
    {
        var age = DateTime.Now - Taken;
        Caption = age.TotalSeconds < 45 ? "just now" : age.TotalMinutes < 60 ? $"{Math.Max(1, (int)age.TotalMinutes)}m ago" : $"{(int)age.TotalHours}h ago";
    }
}

/// <summary>
/// Recent screenshots, ready to drag into any app. Two sources: the Windows Screenshots folder (PrtScn, Win+PrtScn, Snipping Tool's
/// saved files) through a file watcher, and images that land on the clipboard (Win+Shift+S) which are saved as PNGs under
/// %LocalAppData%\DynamicIsland\shots so they can be dragged as files. The button only appears for 30 minutes after the newest one.
/// </summary>
sealed class ScreenshotsEntry : TabEntry
{
    static readonly TimeSpan RelevantFor = TimeSpan.FromMinutes(30), FreshFor = TimeSpan.FromSeconds(90), SameShotWithin = TimeSpan.FromSeconds(25);
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    const int MaxKept = 8, ThumbWidth = 280;
    static readonly Guid ScreenshotsFolderId = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly string _tempDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "shots");
    readonly List<ShotItem> _all = [];
    readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? _folderWatcher, _parentWatcher;
    (int W, int H, DateTime At) _lastClip;
    bool _seen = true;

    public override ViewKind Kind => ViewKind.Screenshots;
    public override string Name => "Screenshots";
    public override string Glyph => ""; // Segoe Fluent Icons: camera
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#7AA2FF";
    public override bool IsAvailable => _all.Count > 0 && DateTime.Now - _all[0].Taken < RelevantFor;

    /// <summary>The newest three, newest first (what the view shows).</summary>
    public ObservableCollection<ShotItem> Recent { get; } = [];

    /// <summary>A screenshot arrived in the last minute and hasn't been looked at: the island opens straight onto it.</summary>
    public bool IsFresh => !_seen && _all.Count > 0 && DateTime.Now - _all[0].Taken < FreshFor;

    public ScreenshotsEntry(ClipboardMonitor clipboard)
    {
        IsRunning = true;
        try { Directory.CreateDirectory(_tempDir); foreach (var old in Directory.EnumerateFiles(_tempDir).Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-1))) File.Delete(old); }
        catch { /* best effort */ }

        clipboard.Changed += OnClipboard; // (not Started: an image that was already on the clipboard before the island opened isn't a new screenshot)
        StartWatching();
    }

    public void MarkSeen()
    {
        _seen = true;
        IsWorking = false;
    }

    public override void Tick(bool expanded)
    {
        foreach (var s in _all) s.UpdateCaption();
        IsWorking = !_seen && IsFresh; // pulsing dot on the button while it is new
        Raise(nameof(IsAvailable));
    }

    /// <summary>What a drag carries: the file itself (WhatsApp, Claude, Explorer, mail...) and the pixels (Paint, editors, web pages).</summary>
    public DataObject CreateDragData(ShotItem item)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { item.Path });
        try
        {
            var full = new BitmapImage();
            full.BeginInit();
            full.UriSource = new Uri(item.Path);
            full.CacheOption = BitmapCacheOption.OnLoad;
            full.EndInit();
            full.Freeze();
            data.SetImage(full);
        }
        catch { /* the file alone is enough */ }
        return data;
    }

    // ---- screenshots folder -------------------------------------------------------------------------------------------------

    static string ScreenshotsFolder()
    {
        if (Environment.GetEnvironmentVariable("DI_SHOTS_DIR") is { Length: > 0 } dev) return dev; // development only: point it somewhere harmless
        try
        {
            var id = ScreenshotsFolderId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) == 0)
            {
                var path = Marshal.PtrToStringUni(p);
                CoTaskMemFree(p);
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { /* fall through */ }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }

    void StartWatching()
    {
        var folder = ScreenshotsFolder();
        if (Directory.Exists(folder)) { WatchFolder(folder); return; }

        // Windows only creates the folder when the first screenshot is taken: wait for it
        var parent = System.IO.Path.GetDirectoryName(folder);
        if (parent is null || !Directory.Exists(parent)) return;
        _parentWatcher = new FileSystemWatcher(parent, System.IO.Path.GetFileName(folder)) { NotifyFilter = NotifyFilters.DirectoryName };
        _parentWatcher.Created += (_, _) => _ = _ui.InvokeAsync(() => { if (Directory.Exists(folder)) { WatchFolder(folder); _parentWatcher?.Dispose(); _parentWatcher = null; } });
        _parentWatcher.EnableRaisingEvents = true;
    }

    void WatchFolder(string folder)
    {
        _folderWatcher?.Dispose();
        _folderWatcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _folderWatcher.Created += (_, e) => OnFile(e.FullPath);
        _folderWatcher.Renamed += (_, e) => OnFile(e.FullPath);
        _folderWatcher.EnableRaisingEvents = true;

        // shots taken just before the island started
        foreach (var f in Directory.EnumerateFiles(folder).Where(IsImage).Select(p => new FileInfo(p)).Where(i => DateTime.Now - i.LastWriteTime < RelevantFor).OrderBy(i => i.LastWriteTime))
            OnFile(f.FullName, startup: true);
    }

    static bool IsImage(string path) => Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    void OnFile(string path, bool startup = false)
    {
        if (!IsImage(path)) return;
        lock (_known) if (!_known.Add(path)) return;

        // the writer (Snipping Tool, Windows) may still be saving: wait until the file can be read
        Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 25; attempt++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length > 0) break;
                }
                catch (IOException) { }
                await Task.Delay(160);
            }
            var item = Load(path, File.GetLastWriteTime(path), fromClipboard: false);
            if (item is not null) _ = _ui.InvokeAsync(() => Add(item, startup));
        });
    }

    // ---- clipboard ---------------------------------------------------------------------------------------------------------

    void OnClipboard()
    {
        BitmapSource? image = null;
        for (var attempt = 0; attempt < 5 && image is null; attempt++)
        {
            try
            {
                if (!WpfClipboard.ContainsImage()) return;
                var shot = WpfClipboard.GetImage();
                if (shot is null) return;
                image = new WriteableBitmap(shot); // a private frozen copy that can be encoded off the UI thread
                image.Freeze();
            }
            catch (COMException) { Thread.Sleep(20); } // the app that just copied still has the clipboard open
        }
        if (image is null) return;

        // some apps announce one copy several times
        var now = DateTime.Now;
        if (_lastClip.W == image.PixelWidth && _lastClip.H == image.PixelHeight && now - _lastClip.At < TimeSpan.FromSeconds(2)) return;
        _lastClip = (image.PixelWidth, image.PixelHeight, now);

        var file = System.IO.Path.Combine(_tempDir, $"screenshot-{now:yyyyMMdd-HHmmss-fff}.png");
        Task.Run(() =>
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var fs = File.Create(file)) encoder.Save(fs);
                var item = Load(file, now, fromClipboard: true);
                if (item is not null) _ = _ui.InvokeAsync(() => Add(item, startup: false));
            }
            catch (IOException) { }
        });
    }

    // ---- shared ------------------------------------------------------------------------------------------------------------

    static ShotItem? Load(string path, DateTime taken, bool fromClipboard)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var thumb = new BitmapImage();
            thumb.BeginInit();
            thumb.StreamSource = fs;
            thumb.DecodePixelWidth = ThumbWidth;
            thumb.CacheOption = BitmapCacheOption.OnLoad;
            thumb.EndInit();
            thumb.Freeze();

            fs.Position = 0;
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return new ShotItem { Path = path, Thumbnail = thumb, Taken = taken, Width = frame.PixelWidth, Height = frame.PixelHeight, FromClipboard = fromClipboard };
        }
        catch { return null; }
    }

    void Add(ShotItem item, bool startup)
    {
        // The same screenshot often arrives twice: once as clipboard pixels, once as a saved file. Keep the real file.
        bool Same(ShotItem o) => o.Width == item.Width && o.Height == item.Height && (item.Taken - o.Taken).Duration() < SameShotWithin;
        if (!item.FromClipboard) _all.RemoveAll(o => o.FromClipboard && Same(o));
        else if (_all.Any(o => !o.FromClipboard && Same(o))) return;

        _all.Add(item);
        _all.Sort((a, b) => b.Taken.CompareTo(a.Taken));
        if (_all.Count > MaxKept) _all.RemoveRange(MaxKept, _all.Count - MaxKept);

        Recent.Clear();
        foreach (var s in _all.Take(3)) Recent.Add(s);

        if (!startup) { _seen = false; IsWorking = true; }
        Raise(nameof(IsAvailable));
    }
}
