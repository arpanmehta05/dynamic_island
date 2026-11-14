using System.Windows.Forms;

namespace DynamicIsland.Core;

/// <summary>The icon by the clock: click it to peek at the island, right-click for Settings and Exit. (Windows 11 may keep it under the ^ arrow until you drag it out.)</summary>
sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;

    public TrayIcon(Action show, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show island", null, (_, _) => show());
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) show(); };
    }

    public void Dispose()
    {
        _icon.Visible = false; // otherwise a ghost icon stays until the mouse passes over it
        _icon.Dispose();
    }
}
