namespace TAS;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using System.Runtime.InteropServices;
using TAS.Infrastructure.Worker;
using TAS.Services;
using TAS.ViewModels;
using TAS.Views;

public partial class App : Avalonia.Application
{
    private const string RunningIconFile = "tray-running.ico";
    private const string StoppedIconFile = "tray-stopped.ico";
    private const string WaitingIconFile = "tray-waiting.ico";

    private TasController? _controller;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private WindowIcon? _runningIcon;
    private WindowIcon? _stoppedIcon;
    private WindowIcon? _waitingIcon;
    private bool _isShuttingDown;
    private bool _isDisguised;

    // Original tray menu items for disguise renaming
    private NativeMenuItem? _openMenuItem;
    private NativeMenuItem? _startMenuItem;
    private NativeMenuItem? _stopMenuItem;
    private NativeMenuItem? _exitMenuItem;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnDesktopExit;

            MainWindow.RestoreSavedTheme();

            _controller = new TasController();
            _controller.StatusChanged += OnControllerStatusChanged;
            _controller.ErrorOccurred += OnControllerError;

            ConfigureTrayIcon();
            EnsureWindowVisible();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TasController Controller =>
        _controller ?? throw new InvalidOperationException("App controller is not initialized.");

    private bool ConfigureTrayIcon()
    {
        _runningIcon = CreateIcon(RunningIconFile);
        _stoppedIcon = CreateIcon(StoppedIconFile);
        _waitingIcon = CreateIcon(WaitingIconFile);

        var icons = TrayIcon.GetIcons(this);
        if (icons == null || icons.Count == 0)
        {
            return false;
        }

        _trayIcon = icons[0];

        // Capture menu item references for disguise renaming
        if (_trayIcon.Menu is NativeMenu menu && menu.Items.Count >= 5)
        {
            _openMenuItem = menu.Items[0] as NativeMenuItem;
            _startMenuItem = menu.Items[2] as NativeMenuItem;
            _stopMenuItem = menu.Items[3] as NativeMenuItem;
            _exitMenuItem = menu.Items[menu.Items.Count - 1] as NativeMenuItem;
        }

        ApplyTrayPresentation(Controller.Status);
        return true;
    }

    private static WindowIcon CreateIcon(string fileName)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return new WindowIcon(iconPath);
    }

    private void OnControllerStatusChanged(WorkerStatus status)
    {
        Dispatcher.UIThread.Post(() => { ApplyTrayPresentation(status); });
    }

    private void OnControllerError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon != null)
            {
                _trayIcon.Icon = _stoppedIcon;
                _trayIcon.ToolTipText = "TAS - Error";
            }

            if (_mainWindow != null && _stoppedIcon != null)
            {
                _mainWindow.Icon = _stoppedIcon;
            }

            EnsureWindowVisible();
            if (_mainWindow?.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ShowRuntimeError(message);
            }
        });
    }

    private void ApplyTrayPresentation(WorkerStatus status)
    {
        var icon = ResolveStatusIcon(status);

        var tooltip = _isDisguised
            ? "TAS"
            : status switch
            {
                WorkerStatus.Start or WorkerStatus.Until => "TAS - Running",
                WorkerStatus.Delay => "TAS - Waiting",
                _ => "TAS - Stopped"
            };

        if (_trayIcon != null)
        {
            _trayIcon.Icon = icon;
            _trayIcon.ToolTipText = tooltip;
        }

        if (_mainWindow != null && icon != null)
        {
            _mainWindow.Icon = icon;
        }
    }

    private WindowIcon? ResolveStatusIcon(WorkerStatus status)
    {
        return status switch
        {
            WorkerStatus.Start or WorkerStatus.Until => _runningIcon,
            WorkerStatus.Delay => _waitingIcon,
            _ => _stoppedIcon
        };
    }

    private void DisposeTrayResources()
    {
        _runningIcon = null;
        _stoppedIcon = null;
        _waitingIcon = null;
        _trayIcon = null;
    }

    private void EnsureWindowVisible()
    {
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(EnsureWindowVisible);
                return;
            }

            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(Controller, RequestShutdown),
                    Icon = ResolveStatusIcon(Controller.Status)
                };
                _mainWindow.Closing += OnMainWindowClosing;
                _mainWindow.DisguiseModeChanged += OnDisguiseModeChanged;

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow = _mainWindow;
                }
            }

            if (!_mainWindow.IsVisible)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _mainWindow.ShowInTaskbar = true;
                }

                _mainWindow.Show();
            }

            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            ApplyTrayPresentation(Controller.Status);
        }
        catch (Exception ex)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Icon = _stoppedIcon;
                _trayIcon.ToolTipText = $"TAS - UI error: {ex.Message}";
            }
        }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        e.Cancel = true;

        // Auto-disguise when hiding so the app looks like a time tracker if restored
        if (_mainWindow?.DataContext is MainWindowViewModel vm)
        {
            if (!vm.IsDisguised)
            {
                vm.ToggleDisguise();
            }
            else
            {
                vm.EnsureWatchMode();
            }
        }

        if (_mainWindow != null)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.WindowState = WindowState.Minimized;
            }
        }
    }

    private void OnOpenControlCenterClicked(object? sender, EventArgs e)
    {
        RunOnUiThread(EnsureWindowVisible);
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        RunOnUiThread(EnsureWindowVisible);
    }

    private void OnStartNowClicked(object? sender, EventArgs e)
    {
        RunOnUiThread(() => Controller.StartNow());
    }

    private void OnStopNowClicked(object? sender, EventArgs e)
    {
        RunOnUiThread(() => Controller.StopNow());
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        RunOnUiThread(RequestShutdown);
    }

    private void RequestShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        if (_mainWindow != null)
        {
            _mainWindow.DisguiseModeChanged -= OnDisguiseModeChanged;
            _mainWindow.Close();
        }

        if (_controller != null)
        {
            _controller.StatusChanged -= OnControllerStatusChanged;
            _controller.ErrorOccurred -= OnControllerError;
        }

        _controller?.Dispose();
        _controller = null;
        DisposeTrayResources();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnDisguiseModeChanged(bool disguised)
    {
        _isDisguised = disguised;
        ApplyDisguiseToTray(disguised);
        ApplyTrayPresentation(Controller.Status);
    }

    private void ApplyDisguiseToTray(bool disguised)
    {
        if (disguised)
        {
            if (_openMenuItem != null) _openMenuItem.Header = "Preferences";
            if (_startMenuItem != null) _startMenuItem.Header = "Enable";
            if (_stopMenuItem != null) _stopMenuItem.Header = "Disable";
            if (_exitMenuItem != null) _exitMenuItem.Header = "Quit";
        }
        else
        {
            if (_openMenuItem != null) _openMenuItem.Header = "Open TAS";
            if (_startMenuItem != null) _startMenuItem.Header = "Start Now";
            if (_stopMenuItem != null) _stopMenuItem.Header = "Stop Now";
            if (_exitMenuItem != null) _exitMenuItem.Header = "Exit";
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        if (_controller != null)
        {
            _controller.StatusChanged -= OnControllerStatusChanged;
            _controller.ErrorOccurred -= OnControllerError;
        }

        _controller?.Dispose();
        _controller = null;
        DisposeTrayResources();
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
