using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UsageViewer;

public sealed class TrayIconService : IDisposable
{
#if DEBUG
    private const string TrayIconResourceName = "UsageViewer.Assets.tray-gauge-development.ico";
#else
    private const string TrayIconResourceName = "UsageViewer.Assets.tray-gauge.ico";
#endif

    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _trayIcon;

    public event Action? ShowRequested;
    public event Action? HideRequested;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;
    public TrayIconService()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(ViewerText.TrayShow, null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add(ViewerText.TrayHide, null, (_, _) => HideRequested?.Invoke());
        menu.Items.Add(ViewerText.TrayRefresh, null, (_, _) => RefreshRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(ViewerText.TrayExit, null, (_, _) => ExitRequested?.Invoke());

        using var iconStream = typeof(TrayIconService).Assembly.GetManifestResourceStream(TrayIconResourceName)
            ?? throw new InvalidOperationException("The tray icon resource is missing.");
        using var icon = new Drawing.Icon(iconStream);
        _trayIcon = (Drawing.Icon)icon.Clone();

        _icon = new Forms.NotifyIcon
        {
            Text = ViewerText.TrayIconTitle,
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void Dispose()
    {
        _icon.Dispose();
        _trayIcon.Dispose();
    }
}
