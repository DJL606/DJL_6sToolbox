using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>负责系统托盘图标、悬停状态提示和右键菜单。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _showMain;
    private readonly Action _checkUpdate;
    private readonly Action _exitApp;
    private readonly Func<string> _getStatus;
    private readonly Action _startLike;
    private readonly Action _stopLike;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayService(
        Action showMain,
        Action checkUpdate,
        Action exitApp,
        Func<string> getStatus,
        Action startLike,
        Action stopLike)
    {
        _showMain = showMain;
        _checkUpdate = checkUpdate;
        _exitApp = exitApp;
        _getStatus = getStatus;
        _startLike = startLike;
        _stopLike = stopLike;

        _notifyIcon = new NotifyIcon
        {
            Text = "DJL_6's 工具箱 - " + _getStatus(),
            Visible = false
        };

        _notifyIcon.DoubleClick += (_, _) => _showMain();

        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(28, 28, 30),
            ForeColor = Color.White,
            ShowImageMargin = false,
            Padding = new Padding(6),
            Font = new Font("Microsoft YaHei UI", 9f),
            Renderer = new TrayRenderer(new TrayColorTable())
        };

        AddItem(menu, "显示主界面", () => _showMain());
        _startItem = AddItem(menu, "启动自动点赞", () => _startLike());
        _stopItem = AddItem(menu, "停止自动点赞", () => _stopLike());
        AddItem(menu, "检查更新", () => _checkUpdate());
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "退出", () => _exitApp());

        menu.Opening += (_, _) => RefreshState();
        _notifyIcon.ContextMenuStrip = menu;

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => RefreshState();
        _timer.Start();
    }

    private static ToolStripMenuItem AddItem(ContextMenuStrip menu, string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.White,
            AutoSize = false,
            Width = 200,
            Height = 34,
            Padding = new Padding(12, 0, 12, 0)
        };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    public void Show()
    {
        _notifyIcon.Icon = LoadAppIcon();
        _notifyIcon.Visible = true;
        RefreshState();
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void RefreshState()
    {
        var status = _getStatus();
        _notifyIcon.Text = $"DJL_6's 工具箱 - {status}";
        _startItem.Enabled = status != "运行中";
        _stopItem.Enabled = status == "运行中";
    }

    private static Icon LoadAppIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/app.ico");
        var stream = Application.GetResourceStream(uri)?.Stream
                     ?? throw new InvalidOperationException("无法加载托盘图标。");
        return new Icon(stream);
    }

    private sealed class TrayRenderer : ToolStripProfessionalRenderer
    {
        public TrayRenderer(ProfessionalColorTable colorTable) : base(colorTable)
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size);
            using var brush = new SolidBrush(
                e.Item.Selected || e.Item.Pressed
                    ? Color.FromArgb(251, 114, 153)
                    : Color.FromArgb(28, 28, 30));
            e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.DrawRectangle(new Pen(Color.FromArgb(60, 60, 65)), 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }

    private sealed class TrayColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(251, 114, 153);
        public override Color MenuItemBorder => Color.FromArgb(251, 114, 153);
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 28, 30);
        public override Color ImageMarginGradientBegin => Color.FromArgb(28, 28, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 28, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(28, 28, 30);
        public override Color MenuBorder => Color.FromArgb(60, 60, 65);
    }
}
