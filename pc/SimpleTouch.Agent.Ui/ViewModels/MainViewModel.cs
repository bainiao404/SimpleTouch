using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxResult = System.Windows.MessageBoxResult;
using SimpleTouch.Core;
using SimpleTouch.Core.Config;
using SimpleTouch.Core.Protocol;
using SimpleTouch.Host;
using SimpleTouch.Platform.Windows;

namespace SimpleTouch.Agent.Ui.ViewModels;

/// <summary>极简 MVVM 基础件（零外部包约定）。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>设置页只读快捷键行。</summary>
public sealed record HotkeyRow(string IdHex, string Name, string Vk, string Mods, string HoldMs);

/// <summary>
/// 主窗口 VM：连接/设置/日志三页数据 + AgentHost 生命周期。
/// AgentHost 事件在 socket/线程池线程触发，统一经 Dispatcher 切回 UI。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly string ConfigDir = Path.Combine(AppContext.BaseDirectory, "config");

    private AgentHost? _host;
    private readonly Dispatcher _dispatcher;
    private AgentState _state = AgentState.Idle;
    private AgentState _lastNotifyState = AgentState.Idle;

    /// <summary>托盘气泡通知请求（标题/正文/是否错误）。</summary>
    public event Action<string, string, bool>? TrayNotify;

    // 连接页
    public ObservableCollection<string> Devices { get; } = new();
    private string _selectedDevice = "";
    public string SelectedDevice { get => _selectedDevice; set { Set(ref _selectedDevice, value); Raise(nameof(StartHint)); } }
    private string _statusText = "未启动";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    private string _peerText = "—";
    public string PeerText { get => _peerText; private set => Set(ref _peerText, value); }
    private string _capsText = "—";
    public string CapsText { get => _capsText; private set => Set(ref _capsText, value); }
    public string StartHint => string.IsNullOrWhiteSpace(SelectedDevice) ? "请先选择设备" : SelectedDevice;
    public bool IsBusy => _state is AgentState.Connecting or AgentState.Connected or AgentState.Reconnecting;

    // 设置页
    private double _moveScale = 1.0;
    public double MoveScale
    {
        get => _moveScale;
        set
        {
            if (!Set(ref _moveScale, Math.Round(value, 2))) return;
            if (_host is not null) _host.SetMoveScale((float)value);
            else { _localSettings.MoveScale = (float)value; _localSettings.Save(_settingsPath); }
        }
    }
    public ObservableCollection<HotkeyRow> Hotkeys { get; } = new();

    // 日志页
    public ObservableCollection<string> LogLines { get; } = new();

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand ReloadHotkeysCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand DiagnoseCommand { get; }
    public RelayCommand RepairCommand { get; }

    private readonly AgentSettings _localSettings;
    private readonly string _settingsPath;

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        _settingsPath = Path.Combine(ConfigDir, "settings.json");
        _localSettings = AgentSettings.LoadOrCreate(_settingsPath);
        _moveScale = _localSettings.MoveScale;

        Log.Emitted += line => _ = _dispatcher.BeginInvoke(() => AppendLog(line));

        StartCommand = new RelayCommand(Start, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedDevice));
        StopCommand = new RelayCommand(Stop, () => IsBusy);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices, () => !IsBusy);
        ReloadHotkeysCommand = new RelayCommand(ReloadHotkeys);
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
        DiagnoseCommand = new RelayCommand(Diagnose, () => !IsBusy);
        RepairCommand = new RelayCommand(Repair, () => !IsBusy);

        _selectedDevice = _localSettings.DeviceName;
        ReloadHotkeys();
        _ = RefreshDevicesAsync();
        AppendLog("SimpleTouch Agent UI 已就绪（托盘常驻，关闭窗口不退出）");
    }

    // ===== 连接管理 =====

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var names = await DeviceScanner.ListPairedDeviceNamesAsync();
            _ = _dispatcher.BeginInvoke(() =>
            {
                Devices.Clear();
                foreach (var n in names) Devices.Add(n);
                if (SelectedDevice is "" || !names.Contains(SelectedDevice, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedDevice = names.Count > 0 ? names[0] : "";
                }
                Raise(nameof(StartHint));
            });
        }
        catch (Exception ex)
        {
            Log.Error($"枚举已配对设备失败: {ex.Message}");
        }
    }

    private void RefreshDevices() => _ = RefreshDevicesAsync();

    private void Start()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SelectedDevice)) return;
        var device = SelectedDevice;
        var host = new AgentHost(device, ConfigDir);
        _host = host;
        host.StateChanged += (state, text) => _ = _dispatcher.BeginInvoke(() => OnStateChanged(state, text));
        host.PeerChanged += peer => _ = _dispatcher.BeginInvoke(() => OnPeerChanged(peer));
        host.RememberDevice(device);
        // 宿主随重连循环结束（Stop/取消）自释放；实例不可复用，下次 Start 新建
        _ = Task.Run(() => host.RunAsync(CancellationToken.None))
              .ContinueWith(_ => host.DisposeAsync(), TaskScheduler.Default);
        Log.Info($"[UI] 启动连接: {device}");
    }

    private void Stop()
    {
        var host = _host;
        if (host is null) return;
        _host = null;
        Log.Info("[UI] 请求停止连接");
        host.Stop();
        _ = DisposeHostAsync(host);
        OnStateChanged(AgentState.Stopped, "已停止");
        OnPeerChanged(null);
    }

    private static async Task DisposeHostAsync(AgentHost host)
    {
        try { await host.DisposeAsync(); } catch { /* 后台循环自行退出 */ }
    }

    private void OnStateChanged(AgentState state, string text)
    {
        _state = state;
        StatusText = text;
        Raise(nameof(IsBusy));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RefreshDevicesCommand.RaiseCanExecuteChanged();
        DiagnoseCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();
        // 托盘气泡仅在状态转移时提示，退避循环内的重复状态不刷屏
        if (state == _lastNotifyState) return;
        switch (state)
        {
            case AgentState.Connected:
                TrayNotify?.Invoke("SimpleTouch", "设备已连接", false);
                break;
            case AgentState.Reconnecting when _lastNotifyState is AgentState.Connected or AgentState.Connecting:
                TrayNotify?.Invoke("SimpleTouch", "连接断开，正在自动重连…", true);
                break;
            case AgentState.Stopped:
                TrayNotify?.Invoke("SimpleTouch", "连接已停止", false);
                break;
        }
        _lastNotifyState = state;
    }

    private void OnPeerChanged(PeerInfo? peer)
    {
        if (peer is null)
        {
            PeerText = "—";
            CapsText = "—";
            return;
        }
        PeerText = $"{peer.Name}（协议 v{peer.Version}）";
        // caps 门控展示（与 Android 侧惯例一致）：按能力点亮
        var c = peer.Caps;
        CapsText = string.Join("  ", new[]
        {
            $"文本直注 {((c & ProtocolConsts.CapsBits.TextInject) != 0 ? "✓" : "✗")}",
            $"剪贴板 {((c & ProtocolConsts.CapsBits.Clipboard) != 0 ? "✓" : "✗")}",
            $"宏 {((c & ProtocolConsts.CapsBits.Macros) != 0 ? "✓" : "✗")}",
            $"媒体键 {((c & ProtocolConsts.CapsBits.Consumer) != 0 ? "✓" : "✗")}",
        });
    }

    // ===== 诊断 / 修复（输出经 Log.Emitted 落入日志页） =====

    private void Diagnose()
    {
        var device = SelectedDevice;
        _ = Task.Run(async () =>
        {
            Log.Info($"[UI] 运行诊断: {device}");
            try { await DeviceDiagnostics.DumpAsync(device, ProtocolConsts.RfcommServiceUuid, CancellationToken.None); }
            catch (Exception ex) { Log.Error($"诊断失败: {ex.Message}"); }
        });
    }

    private void Repair()
    {
        var device = SelectedDevice;
        if (MessageBoxResult.OK != MessageBox.Show(
                "重新配对流程：① 平板端先删除本机配对（如未删）② 保持平板蓝牙设置页打开 ③ App 保持监听中。\n\n确认开始？（期间平板会弹出配对确认）",
                "SimpleTouch 重新配对", MessageBoxButton.OKCancel, MessageBoxImage.Information))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            Log.Info("[UI] 开始重新配对流程");
            try
            {
                var ok = await RepairFlow.RunAsync(device, CancellationToken.None);
                Log.Info(ok ? "重新配对成功，SimpleTouch 服务已进入配对缓存" : "重新配对未能缓存 SimpleTouch 服务");
            }
            catch (Exception ex)
            {
                Log.Error($"重新配对失败: {ex.Message}");
            }
        });
    }

    // ===== 设置页 =====

    private void ReloadHotkeys()
    {
        _host?.ReloadHotkeys();
        try
        {
            var map = HotkeyMap.LoadOrCreate(Path.Combine(ConfigDir, "hotkeys.json"));
            Hotkeys.Clear();
            foreach (var (id, def) in map.OrderBy(kv => kv.Key))
                Hotkeys.Add(new HotkeyRow(
                    $"0x{id:X4}",
                    def.Name,
                    $"0x{def.Vk:X2}",
                    ModsText(def.Mods),
                    $"{def.HoldMs}ms"));
        }
        catch (Exception ex)
        {
            Log.Error($"hotkeys.json 读取失败: {ex.Message}");
        }
    }

    private static string ModsText(byte mods)
    {
        if (mods == 0) return "—";
        var parts = new List<string>();
        if ((mods & ProtocolConsts.KeyMods.Shift) != 0) parts.Add("Shift");
        if ((mods & ProtocolConsts.KeyMods.Ctrl) != 0) parts.Add("Ctrl");
        if ((mods & ProtocolConsts.KeyMods.Alt) != 0) parts.Add("Alt");
        if ((mods & ProtocolConsts.KeyMods.Meta) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }

    private void OpenConfigFolder()
    {
        Directory.CreateDirectory(ConfigDir);
        Process.Start("explorer.exe", ConfigDir);
    }

    // ===== 日志页 =====

    private void AppendLog(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > 1000) LogLines.RemoveAt(0);
    }
}
