using System.Drawing;
using System.Windows.Forms;
using SimpleTouch.Host;

namespace SimpleTouch.Agent.Ui.Services;

/// <summary>
/// 托盘图标服务（WinForms NotifyIcon，零外部依赖）。
/// 图标运行时用 GDI+ 生成（紫色圆 + ST 字样），避免引入资源文件工具链。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayIconService(
        Action showWindow,
        Action connect,
        Action disconnect,
        Action exit,
        Func<bool> isConnected)
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("打开 SimpleTouch", null, (_, _) => showWindow());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("连接设备", null, (_, _) => connect());
        _menu.Items.Add("断开连接", null, (_, _) => disconnect());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "SimpleTouch Agent",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => showWindow();
    }

    /// <summary>托盘悬浮提示（≤63 字符）。</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    /// <summary>状态变化气泡通知。</summary>
    public void Notify(string title, string text, bool isError = false) =>
        _icon.ShowBalloonTip(3000, title, text, isError ? ToolTipIcon.Error : ToolTipIcon.Info);

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(103, 80, 164)); // 主题紫
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            var size = g.MeasureString("ST", font);
            g.DrawString("ST", font, Brushes.White, (32 - size.Width) / 2f, (32 - size.Height) / 2f + 1f);
        }
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle); // 进程级常驻，无需释放句柄
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}

/// <summary>托盘菜单的状态辅助：根据 AgentState 决定连接/断开菜单项可用性。</summary>
public static class TrayMenuState
{
    public static bool CanConnect(AgentState state) =>
        state is AgentState.Idle or AgentState.Stopped or AgentState.Reconnecting;

    public static bool CanDisconnect(AgentState state) =>
        state is AgentState.Connecting or AgentState.Connected or AgentState.Reconnecting;
}
