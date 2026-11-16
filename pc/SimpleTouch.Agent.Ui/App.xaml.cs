using System.IO;
using System.Threading;
using System.Windows;
using SimpleTouch.Agent.Ui.Services;
using SimpleTouch.Agent.Ui.ViewModels;
using SimpleTouch.Core;
// WinForms（托盘）与 WPF 同存时的歧义消解
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SimpleTouch.Agent.Ui;

/// <summary>
/// 托盘常驻宿主：单实例互斥；窗口关闭≠退出（托盘常驻），托盘「退出」才真正结束。
/// </summary>
public partial class App : Application
{
    private static readonly Mutex SingleInstance = new(false, "Local\\SimpleTouch.Agent.Ui");

    /// <summary>托盘「退出」置位后，窗口关闭不再拦截（否则 OnClosing 一律隐藏）。</summary>
    internal static bool IsExiting;

    private TrayIconService? _tray;
    private MainViewModel? _vm;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstance.WaitOne(0))
        {
            MessageBox.Show("SimpleTouch Agent UI 已在运行（见系统托盘）。", "SimpleTouch",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // 日志双写（GUI 宿主控制台不可见，以文件为准；诊断/修复输出也经 Log 落文件）
        Log.Init(Path.Combine(AppContext.BaseDirectory, "logs", $"agent-ui-{DateTime.Now:yyyyMMdd-HHmmss}.log"));

        _vm = new MainViewModel();
        _vm.TrayNotify += (title, text, error) => _tray?.Notify(title, text, error);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusText))
                _tray?.SetTooltip($"SimpleTouch Agent —— {_vm.StatusText}");
        };

        _tray = new TrayIconService(
            showWindow: ShowMainWindow,
            connect: () => Dispatcher.Invoke(() => { if (_vm.StartCommand.CanExecute(null)) _vm.StartCommand.Execute(null); }),
            disconnect: () => Dispatcher.Invoke(() => { if (_vm.StopCommand.CanExecute(null)) _vm.StopCommand.Execute(null); }),
            exit: ExitApplication,
            isConnected: () => _vm.IsBusy);

        _window = new MainWindow();
        _window.Show();
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            _window?.Show();
            _window?.Activate();
            if (_window is { WindowState: WindowState.Minimized }) _window.WindowState = WindowState.Normal;
        });
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            IsExiting = true;
            _vm?.StopCommand.Execute(null);
            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        SingleInstance.Dispose();
        base.OnExit(e);
    }
}
