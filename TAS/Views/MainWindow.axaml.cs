namespace TAS.Views;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using TAS.ViewModels;

public partial class MainWindow : Window
{
    private const double DialSize = 500;
    private const double DialCenter = DialSize / 2;
    private const double DialRadius = 218;
    private const double HandleSize = 26;
    private const int MinuteSnap = 5;

    private ActiveHandle _activeHandle = ActiveHandle.None;
    private MainWindowViewModel? _boundViewModel;
    private CancellationTokenSource? _runningAnimationCts;

    private static readonly Animation RunningPulseAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(1300),
        IterationCount = IterationCount.Infinite,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters =
                {
                    new Setter(OpacityProperty, 1d)
                }
            },
            new KeyFrame
            {
                Cue = new Cue(0.5d),
                Setters =
                {
                    new Setter(OpacityProperty, 0.78d)
                }
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters =
                {
                    new Setter(OpacityProperty, 1d)
                }
            }
        }
    };

    private enum ActiveHandle
    {
        None,
        Start,
        Stop
    }

    public event Action<bool>? DisguiseModeChanged;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnGlobalKeyDown;
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        UpdateDialHandlesFromViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _boundViewModel = DataContext as MainWindowViewModel;
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateDialHandlesFromViewModel();
        UpdateRunningAnimation();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.StartTimeText)
            or nameof(MainWindowViewModel.StopTimeText)
            or nameof(MainWindowViewModel.IsStartTimerEnabled)
            or nameof(MainWindowViewModel.IsStopTimerEnabled))
        {
            UpdateDialHandlesFromViewModel();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsRunning))
        {
            UpdateRunningAnimation();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsDisguised))
        {
            DisguiseModeChanged?.Invoke(_boundViewModel!.IsDisguised);
        }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Shift+D — toggle disguise mode (swap content to clock face)
        if (e.Key == Key.D
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ToggleDisguise();
            }

            e.Handled = true;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (IsInteractiveControlSource(e.Source))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnStartHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        BeginDialDrag(ActiveHandle.Start, e);
    }

    private void OnStopHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        BeginDialDrag(ActiveHandle.Stop, e);
    }

    private void BeginDialDrag(ActiveHandle handle, PointerPressedEventArgs e)
    {
        if (!TryGetDialElements(out var dialCanvas, out _, out _))
        {
            return;
        }

        _activeHandle = handle;
        e.Pointer.Capture(dialCanvas);

        UpdateTimeFromPointer(e.GetPosition(dialCanvas));
        e.Handled = true;
    }

    private void OnDialPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeHandle == ActiveHandle.None)
        {
            return;
        }

        if (!TryGetDialElements(out var dialCanvas, out _, out _))
        {
            return;
        }

        UpdateTimeFromPointer(e.GetPosition(dialCanvas));
    }

    private void OnDialPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activeHandle == ActiveHandle.None)
        {
            return;
        }

        _activeHandle = ActiveHandle.None;
        e.Pointer.Capture(null);
    }

    private void UpdateTimeFromPointer(Point position)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dx = position.X - DialCenter;
        var dy = position.Y - DialCenter;

        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return;
        }

        var angleDegrees = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 90.0 + 360.0) % 360.0;
        var rawMinutes = angleDegrees / 360.0 * 1440.0;
        var snappedMinutes = ((int)Math.Round(rawMinutes / MinuteSnap) * MinuteSnap) % 1440;
        var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(snappedMinutes));

        switch (_activeHandle)
        {
            case ActiveHandle.Start:
                viewModel.SetStartTimeFromDial(time);
                break;
            case ActiveHandle.Stop:
                viewModel.SetStopTimeFromDial(time);
                break;
        }

        UpdateDialHandlesFromViewModel();
    }

    private void UpdateDialHandlesFromViewModel()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !TryGetDialElements(out _, out var startHandle, out var stopHandle))
        {
            return;
        }

        var startTime = ParseTimeOrDefault(viewModel.StartTimeText, new TimeOnly(8, 0));
        var stopTime = ParseTimeOrDefault(viewModel.StopTimeText, new TimeOnly(18, 0));

        PositionHandle(startHandle, startTime);
        PositionHandle(stopHandle, stopTime);

        startHandle.Opacity = viewModel.IsStartTimerEnabled ? 1.0 : 0.45;
        stopHandle.Opacity = viewModel.IsStopTimerEnabled ? 1.0 : 0.45;
    }

    private static TimeOnly ParseTimeOrDefault(string value, TimeOnly fallback)
    {
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTime))
        {
            return TimeOnly.FromDateTime(dateTime);
        }

        return fallback;
    }

    private static void PositionHandle(Ellipse handle, TimeOnly time)
    {
        var minutes = time.Hour * 60 + time.Minute;
        var angleDegrees = (minutes / 1440.0 * 360.0) - 90.0;
        var angleRadians = angleDegrees * Math.PI / 180.0;

        var x = DialCenter + Math.Cos(angleRadians) * DialRadius - HandleSize / 2.0;
        var y = DialCenter + Math.Sin(angleRadians) * DialRadius - HandleSize / 2.0;

        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private bool TryGetDialElements(
        out Canvas dialCanvas,
        out Ellipse startHandle,
        out Ellipse stopHandle)
    {
        dialCanvas = DialCanvas;
        startHandle = StartHandle;
        stopHandle = StopHandle;
        return dialCanvas != null && startHandle != null && stopHandle != null;
    }

    private static bool IsInteractiveControlSource(object? source)
    {
        var current = source as StyledElement;
        while (current != null)
        {
            if (current is Button or CheckBox or TextBox)
            {
                return true;
            }

            current = current.Parent as StyledElement;
        }

        return false;
    }

    private void OnHideClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ExitApplication();
    }

    private void OnAddTaskClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddTask();
    }

    private void OnRemoveTaskClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskEntry task })
        {
            ViewModel.RemoveTask(task);
        }
    }

    private void OnSaveTaskClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskEntry task })
        {
            ViewModel.UpdateTask(task);
        }
    }

    private void OnToggleTrackerViewClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ToggleTrackerView();
    }

    private void OnClearAllTasksClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ClearAllTasks();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopRunningAnimation();
        base.OnClosed(e);
    }

    private void OnStartNowClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.StartNow();
    }

    private void OnStopNowClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.StopNow();
    }

    private void OnApplyScheduleClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplySchedule();
    }

    private void UpdateRunningAnimation()
    {
        if (_boundViewModel == null || CenterCard == null)
        {
            return;
        }

        if (_boundViewModel.IsRunning)
        {
            StartRunningAnimation();
            return;
        }

        StopRunningAnimation();
    }

    private void StartRunningAnimation()
    {
        if (_runningAnimationCts != null || CenterCard == null)
        {
            return;
        }

        _runningAnimationCts = new CancellationTokenSource();
        _ = RunningPulseAnimation.RunAsync(CenterCard, _runningAnimationCts.Token);
    }

    private void StopRunningAnimation()
    {
        _runningAnimationCts?.Cancel();
        _runningAnimationCts?.Dispose();
        _runningAnimationCts = null;

        if (CenterCard != null)
        {
            CenterCard.Opacity = 1;
        }
    }

    private void OnClearScheduleClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ClearSchedule();
    }

    private static readonly string ThemeFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TAS", "theme.txt");

    private static readonly Dictionary<string, ThemePreset> Themes = new()
    {
        ["green"] = new(
            "#F7FFFC", "#EAF7F1", "#CEEFE3", "#B8E2D4", "#F9FFFC", "#D0E7DE",
            "#169B73", "#11815F", "#EAF4F0", "#DCEBE5", "#169B73",
            "#F0FAF6", "#D5EAE2", "#E8F8F1", "#BCE7D7", "#AACFC2",
            "#FAFFFD", "#D6ECE4", "#678378"),
        ["blue"] = new(
            "#F5F8FF", "#E4ECF9", "#BDD4F0", "#A2C0E5", "#F6F9FF", "#C0D0E8",
            "#3574B8", "#2A5D96", "#E4ECF6", "#D4DFEE", "#3574B8",
            "#EDF2FA", "#C8D6EA", "#E0EAF6", "#AABFD8", "#96B0CC",
            "#F8FAFF", "#C5D4E8", "#5E7590"),
        ["rose"] = new(
            "#FFF6F7", "#F8E6EA", "#EEC4CC", "#E4ABB5", "#FFF9FA", "#E6C8D0",
            "#B85068", "#9A3F55", "#F6E4E8", "#EDD4DA", "#B85068",
            "#FAF0F2", "#E8CDD4", "#F5DFE4", "#D8B0BA", "#CCA0AC",
            "#FFFAFB", "#E4C4CC", "#906878"),
        ["gray"] = new(
            "#F8F8F9", "#EAEBED", "#CCCED2", "#B8BABE", "#F8F8F9", "#CCCED0",
            "#5E6670", "#4A525C", "#E8EAEC", "#DCDEE2", "#5E6670",
            "#F0F0F2", "#D0D2D6", "#E6E8EA", "#B8BABE", "#A0A4AA",
            "#FAFAFB", "#CED0D4", "#6E747C")
    };

    private void OnThemeGreenClicked(object? sender, PointerPressedEventArgs e) => ApplyAndSaveTheme("green");
    private void OnThemeBlueClicked(object? sender, PointerPressedEventArgs e) => ApplyAndSaveTheme("blue");
    private void OnThemeRoseClicked(object? sender, PointerPressedEventArgs e) => ApplyAndSaveTheme("rose");
    private void OnThemeGrayClicked(object? sender, PointerPressedEventArgs e) => ApplyAndSaveTheme("gray");

    private static void ApplyAndSaveTheme(string name)
    {
        if (!Themes.TryGetValue(name, out var preset))
        {
            return;
        }

        ApplyTheme(preset);
        SaveThemeName(name);
    }

    internal static void RestoreSavedTheme()
    {
        var name = LoadThemeName();
        if (Themes.TryGetValue(name, out var preset))
        {
            ApplyTheme(preset);
        }
    }

    private static void ApplyTheme(ThemePreset t)
    {
        var resources = Application.Current!.Resources;
        resources["WatchShellTop"] = Color.Parse(t.ShellTop);
        resources["WatchShellBottom"] = Color.Parse(t.ShellBottom);
        resources["DialOuterStart"] = Color.Parse(t.OuterStart);
        resources["DialOuterEnd"] = Color.Parse(t.OuterEnd);
        resources["DialInnerFill"] = Color.Parse(t.InnerFill);
        resources["DialInnerBorder"] = Color.Parse(t.InnerBorder);
        resources["AccentColor"] = Color.Parse(t.Accent);
        resources["AccentHoverColor"] = Color.Parse(t.AccentHover);
        resources["NeutralColor"] = Color.Parse(t.Neutral);
        resources["NeutralHoverColor"] = Color.Parse(t.NeutralHover);
        resources["StartColor"] = Color.Parse(t.Start);
        resources["TintLight"] = Color.Parse(t.TintLight);
        resources["TintBorder"] = Color.Parse(t.TintBorder);
        resources["TintChipBg"] = Color.Parse(t.ChipBg);
        resources["TintChipBorder"] = Color.Parse(t.ChipBorder);
        resources["TickColor"] = Color.Parse(t.Tick);
        resources["CenterCardBg"] = Color.Parse(t.CardBg);
        resources["CenterCardBorder"] = Color.Parse(t.CardBorder);
        resources["SubtleText"] = Color.Parse(t.Subtle);
    }

    private static void SaveThemeName(string name)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ThemeFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ThemeFilePath, name);
        }
        catch
        {
            // Best-effort persistence; ignore I/O failures.
        }
    }

    private static string LoadThemeName()
    {
        try
        {
            if (File.Exists(ThemeFilePath))
            {
                return File.ReadAllText(ThemeFilePath).Trim().ToLowerInvariant();
            }
        }
        catch
        {
            // Fall through to default.
        }

        return "green";
    }

    private sealed record ThemePreset(
        string ShellTop, string ShellBottom,
        string OuterStart, string OuterEnd,
        string InnerFill, string InnerBorder,
        string Accent, string AccentHover,
        string Neutral, string NeutralHover,
        string Start,
        string TintLight, string TintBorder,
        string ChipBg, string ChipBorder,
        string Tick,
        string CardBg, string CardBorder,
        string Subtle);
}
