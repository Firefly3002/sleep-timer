using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _cancelItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly ToolStripMenuItem _widgetItem;

    public TrayController(Action show, Action cancel, Action restart, Action settings, Action exit, Action toggleWidget)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Sleep Timer", null, (_, _) => show());
        _widgetItem = new ToolStripMenuItem("Show desktop widget", null, (_, _) => toggleWidget());
        menu.Items.Add(_widgetItem);
        _restartItem = new ToolStripMenuItem("Restart timer", null, (_, _) => restart());
        menu.Items.Add(_restartItem);
        _cancelItem = new ToolStripMenuItem("Cancel timer", null, (_, _) => cancel());
        menu.Items.Add(_cancelItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add("Exit", null, (_, _) => exit());

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "sleep-timer.ico");
        Icon icon;
        try { icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application; }
        catch { icon = SystemIcons.Application; }
        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "Sleep Timer",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => show();
        _icon.BalloonTipTitle = "Sleep Timer";
    }

    public void Update(TimerSnapshot snapshot)
    {
        var active = snapshot.Phase != TimerPhase.Idle;
        _cancelItem.Enabled = active;
        _restartItem.Enabled = active;
        _icon.Text = active
            ? $"Sleep Timer · {FormatRemaining(snapshot.Remaining)}"
            : "Sleep Timer · idle";
    }

    public void SetWidgetVisible(bool visible) => _widgetItem.Text = visible ? "Hide desktop widget" : "Show desktop widget";

    private static string FormatRemaining(TimeSpan value)
    {
        var totalHours = (int)value.TotalHours;
        return totalHours > 0 ? $"{totalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        _icon.Dispose();
        menu?.Dispose();
    }
}

