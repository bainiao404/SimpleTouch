using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SimpleTouch.Agent.Ui.ViewModels;

namespace SimpleTouch.Agent.Ui;

/// <summary>
/// 主窗口：关闭=隐藏（托盘常驻，见 App.ExitApplication 的 IsExiting 标记）；日志页自动滚动。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.LogLines.CollectionChanged += (_, _) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 托盘常驻：点 X 仅隐藏；托盘「退出」先置 IsExiting 再 Shutdown，走 base 关闭
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
