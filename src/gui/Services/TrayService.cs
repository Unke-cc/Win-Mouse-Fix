using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinMouseFix.Gui.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ToolStripMenuItem toggleItem;
    private readonly Forms.ToolStripMenuItem lightweightItem;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Func<bool, Drawing.Icon> iconFactory;
    private bool disposed;
    private bool? lastRunning;

    public TrayService(Func<bool, Drawing.Icon> iconFactory)
    {
        this.iconFactory = iconFactory;
        toggleItem = new Forms.ToolStripMenuItem();
        lightweightItem = new Forms.ToolStripMenuItem("轻量模式", null, (_, _) => Raise(LightweightModeRequested))
        {
            CheckOnClick = true
        };
        menu = new Forms.ContextMenuStrip
        {
            AutoClose = true,
            ShowCheckMargin = true,
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9F, Drawing.FontStyle.Regular)
        };
        menu.Items.Add(new Forms.ToolStripMenuItem("打开设置", null, (_, _) => Raise(OpenSettingsRequested))
        {
            Font = new Drawing.Font("Segoe UI", 9F, Drawing.FontStyle.Bold)
        });
        toggleItem.Click += (_, _) => Raise(ToggleRequested);
        menu.Items.Add(toggleItem);
        menu.Items.Add(lightweightItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("退出", null, (_, _) => Raise(ExitRequested)));

        trayIcon = new Forms.NotifyIcon
        {
            Text = "Win Mouse Fix",
            Icon = iconFactory(false),
            ContextMenuStrip = menu,
            Visible = true
        };
        lastRunning = false;
        trayIcon.DoubleClick += (_, _) => Raise(OpenSettingsRequested);
    }

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? LightweightModeRequested;
    public event EventHandler? ExitRequested;

    public bool IsLightweightModeChecked => lightweightItem.Checked;

    public void SetLightweightModeChecked(bool checkedState) => lightweightItem.Checked = checkedState;

    public void Update(bool running)
    {
        if (disposed)
        {
            return;
        }

        toggleItem.Text = running ? "停止 Win Mouse Fix" : "启动 Win Mouse Fix";
        lightweightItem.Enabled = true;
        lightweightItem.Text = "轻量模式";
        if (lastRunning != running)
        {
            trayIcon.Visible = false;
            var previousIcon = trayIcon.Icon;
            trayIcon.Icon = iconFactory(running);
            previousIcon?.Dispose();
            trayIcon.Visible = true;
            lastRunning = running;
        }
        trayIcon.Text = "Win Mouse Fix";
    }

    public void ShowBalloon(string title, string message)
    {
        if (!disposed)
        {
            trayIcon.ShowBalloonTip(2500, title, message, Forms.ToolTipIcon.Info);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        trayIcon.Visible = false;
        trayIcon.Icon?.Dispose();
        trayIcon.Dispose();
        menu.Dispose();
    }

    private void Raise(EventHandler? handler)
    {
        if (!disposed)
        {
            menu.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
            handler?.Invoke(this, EventArgs.Empty);
        }
    }
}
