using System.Drawing;
using System.Windows.Forms;
using ZhccCargoBoard.Core;

namespace ZhccCargoBoard.App.Services;

/// <summary>
/// 系统托盘图标。用 WinForms 的 NotifyIcon（它内部自建消息窗口，
/// 在本机和各类受限账号下都能注册成功；直接调 Shell_NotifyIcon 反而会被拒绝）。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Action _show;
    private readonly Action _refresh;
    private readonly Action _export;
    private readonly Action _exit;

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon? _icon;
    private bool _disposed;
    private string _tooltip = "货机航班动态 · 郑州新郑国际机场";

    public TrayService(Action show, Action refresh, Action export, Action exit)
    {
        _show = show;
        _refresh = refresh;
        _export = export;
        _exit = exit;

        _icon = LoadIcon();

        var menu = BuildMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon ?? SystemIcons.Application,
            Text = _tooltip,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _show();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _show();
            }
        };

        IsRegistered = _notifyIcon.Visible;
        Log.Info($"托盘图标已注册（Visible={_notifyIcon.Visible}，图标句柄={_notifyIcon.Icon?.Handle}）");
    }

    /// <summary>注册是否成功（失败时仍可用主窗口，只是没有托盘入口）。</summary>
    public bool IsRegistered { get; }

    public void UpdateTooltip(string text)
    {
        if (_disposed) return;
        _tooltip = text.Length > 63 ? text[..63] : text;
        _notifyIcon.Text = _tooltip;
    }

    /// <summary>弹出气泡提示（Windows 11 会显示在通知中心/右下角）。</summary>
    public void ShowBalloon(string title, string text)
    {
        if (_disposed) return;

        try
        {
            _notifyIcon.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn("显示托盘气泡失败：" + ex.Message);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
            Padding = new Padding(4)
        };

        menu.Items.Add(CreateItem("打开主窗口", _show, bold: true));
        menu.Items.Add(CreateItem("立即刷新", _refresh));
        menu.Items.Add(CreateItem("导出 Excel…", _export));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem("退出", _exit));

        return menu;
    }

    private static ToolStripMenuItem CreateItem(string text, Action action, bool bold = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            Padding = new Padding(6, 4, 6, 4),
            Font = new Font("Microsoft YaHei UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon? LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("加载托盘图标失败，改用系统图标：" + ex.Message);
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("释放托盘图标失败：" + ex.Message);
        }

        _icon?.Dispose();
    }
}


