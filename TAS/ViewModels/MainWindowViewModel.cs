namespace TAS.ViewModels;

using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using TAS.Infrastructure.Worker;
using TAS.Services;

using DispatcherTimer = Avalonia.Threading.DispatcherTimer;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TasController _controller;
    private readonly Action _shutdownAction;
    private readonly TaskStore _taskStore;

    private string _statusText;
    private bool _isStartTimerEnabled;
    private bool _isStopTimerEnabled;
    private string _startTimeText;
    private string _stopTimeText;
    private string _scheduleSummary;
    private string _validationMessage;
    private bool _isRunning;
    private bool _isDisguised;
    private bool _isTrackerView;
    private string _clockTimeText = "";
    private string _clockDateText = "";
    private string _newTaskName = "";
    private string _todayTotal = "0.0h";
    private DispatcherTimer? _clockTimer;

    public MainWindowViewModel(TasController controller, Action shutdownAction)
    {
        _controller = controller;
        _shutdownAction = shutdownAction;
        _taskStore = new TaskStore();

        _statusText = ToDisplayStatus(_controller.Status);
        _startTimeText = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        _stopTimeText = DateTime.Now.AddHours(1).ToString("HH:mm", CultureInfo.InvariantCulture);
        _scheduleSummary = "No timers configured.";
        _validationMessage = string.Empty;
        _isRunning = IsRunningStatus(_controller.Status);

        _controller.StatusChanged += OnStatusChanged;
        _controller.ScheduleChanged += OnScheduleChanged;
        _controller.ErrorOccurred += OnControllerError;

        ApplySnapshot(_controller.Schedule);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string OperatingSystemText => $"OS: {_controller.OperatingSystemName}";

    public bool IsStartTimerEnabled
    {
        get => _isStartTimerEnabled;
        set => SetProperty(ref _isStartTimerEnabled, value);
    }

    public bool IsStopTimerEnabled
    {
        get => _isStopTimerEnabled;
        set => SetProperty(ref _isStopTimerEnabled, value);
    }

    public string StartTimeText
    {
        get => _startTimeText;
        set
        {
            if (SetProperty(ref _startTimeText, value))
            {
                OnPropertyChanged(nameof(TrackerDuration));
            }
        }
    }

    public string StopTimeText
    {
        get => _stopTimeText;
        set
        {
            if (SetProperty(ref _stopTimeText, value))
            {
                OnPropertyChanged(nameof(TrackerDuration));
            }
        }
    }

    public string ScheduleSummary
    {
        get => _scheduleSummary;
        private set => SetProperty(ref _scheduleSummary, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsNotRunning));
            }
        }
    }

    public bool IsNotRunning => !IsRunning;

    public bool IsDisguised
    {
        get => _isDisguised;
        private set
        {
            if (SetProperty(ref _isDisguised, value))
            {
                OnPropertyChanged(nameof(IsNotDisguised));
                OnPropertyChanged(nameof(ShowDialView));
                OnPropertyChanged(nameof(ShowClockView));
                OnPropertyChanged(nameof(ShowTrackerView));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public bool IsNotDisguised => !IsDisguised;

    /// <summary>Within disguise: false = watch face, true = tracker dial.</summary>
    public bool IsTrackerView
    {
        get => _isTrackerView;
        private set
        {
            if (SetProperty(ref _isTrackerView, value))
            {
                OnPropertyChanged(nameof(ShowDialView));
                OnPropertyChanged(nameof(ShowClockView));
                OnPropertyChanged(nameof(ShowTrackerView));
            }
        }
    }

    /// <summary>Show dial canvas: real TAS or tracker view.</summary>
    public bool ShowDialView => IsNotDisguised || (_isDisguised && _isTrackerView);

    /// <summary>Show clock watch face with task list.</summary>
    public bool ShowClockView => _isDisguised && !_isTrackerView;

    /// <summary>Show tracker dial for logging tasks.</summary>
    public bool ShowTrackerView => _isDisguised && _isTrackerView;

    public string WindowTitle => _isDisguised ? "Time Tracker" : "TAS";

    public string ClockTimeText
    {
        get => _clockTimeText;
        private set => SetProperty(ref _clockTimeText, value);
    }

    public string ClockDateText
    {
        get => _clockDateText;
        private set => SetProperty(ref _clockDateText, value);
    }

    public ObservableCollection<TaskEntry> Tasks { get; } = new();

    public string NewTaskName
    {
        get => _newTaskName;
        set => SetProperty(ref _newTaskName, value);
    }

    public string TodayTotal
    {
        get => _todayTotal;
        private set => SetProperty(ref _todayTotal, value);
    }

    public string TrackerDuration
    {
        get
        {
            var start = TryParseTimeValue(_startTimeText);
            var stop = TryParseTimeValue(_stopTimeText);
            if (start == null || stop == null) return "0:30";

            var diff = stop.Value.ToTimeSpan() - start.Value.ToTimeSpan();
            if (diff < TimeSpan.Zero) diff += TimeSpan.FromHours(24);
            if (diff == TimeSpan.Zero) diff = TimeSpan.FromMinutes(30);
            var h = (int)diff.TotalHours;
            var m = diff.Minutes;
            return $"{h}:{m:D2}";
        }
    }

    private static TimeOnly? TryParseTimeValue(string value)
    {
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;
        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
            return TimeOnly.FromDateTime(dt);
        return null;
    }

    public void ToggleDisguise()
    {
        IsDisguised = !IsDisguised;

        if (IsDisguised)
        {
            _isTrackerView = false;
            LoadTodayTasks();
            UpdateClockTexts();
            RecalculateTotal();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateClockTexts();
            _clockTimer.Start();
        }
        else
        {
            _clockTimer?.Stop();
            _clockTimer = null;
            Tasks.Clear();
            IsTrackerView = false;
        }
    }

    private string TodayDateKey => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void LoadTodayTasks()
    {
        Tasks.Clear();
        foreach (var task in _taskStore.LoadTasks(TodayDateKey))
        {
            HookTask(task);
            Tasks.Add(task);
        }
    }

    public void ToggleTrackerView()
    {
        IsTrackerView = !IsTrackerView;
    }

    public void EnsureWatchMode()
    {
        if (IsDisguised && IsTrackerView)
        {
            IsTrackerView = false;
        }
    }

    public void ClearAllTasks()
    {
        _taskStore.ClearDate(TodayDateKey);
        Tasks.Clear();
        RecalculateTotal();
    }

    public void AddTask()
    {
        var name = NewTaskName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ValidationMessage = "Task name cannot be empty.";
            return;
        }

        var parsedStart = TryParseTimeValue(StartTimeText);
        var parsedEnd = TryParseTimeValue(StopTimeText);

        if (parsedStart == null)
        {
            ValidationMessage = "Invalid start time. Use HH:mm format.";
            return;
        }

        if (parsedEnd == null)
        {
            ValidationMessage = "Invalid end time. Use HH:mm format.";
            return;
        }

        if (parsedEnd.Value <= parsedStart.Value)
        {
            ValidationMessage = "End time must be after start time.";
            return;
        }

        var now = TimeOnly.FromDateTime(DateTime.Now);
        if (parsedEnd.Value > now)
        {
            ValidationMessage = "End time cannot be in the future.";
            return;
        }

        var startTime = parsedStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        var stopTime = parsedEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        var duration = ComputeDuration(parsedStart.Value, parsedEnd.Value);

        var id = _taskStore.InsertTask(name, duration, startTime, stopTime, TodayDateKey);
        var task = new TaskEntry(name, duration, startTime, stopTime) { Id = id };
        HookTask(task);
        Tasks.Add(task);
        NewTaskName = string.Empty;
        RecalculateTotal();
        ValidationMessage = string.Empty;
    }

    public void RemoveTask(TaskEntry task)
    {
        if (task.Id > 0) _taskStore.DeleteTask(task.Id);
        Tasks.Remove(task);
        RecalculateTotal();
    }

    public void UpdateTask(TaskEntry task)
    {
        var trimmedName = task.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ValidationMessage = "Task name cannot be empty.";
            return;
        }

        var rawStart = task.StartTime?.Trim() ?? string.Empty;
        var rawEnd = task.EndTime?.Trim() ?? string.Empty;
        var hasStart = !string.IsNullOrWhiteSpace(rawStart);
        var hasEnd = !string.IsNullOrWhiteSpace(rawEnd);

        if (hasStart != hasEnd)
        {
            ValidationMessage = "Provide both start and end times.";
            return;
        }

        if (hasStart && hasEnd)
        {
            if (!TryParseTime(rawStart, out var parsedStart))
            {
                ValidationMessage = "Invalid start time. Use HH:mm format (example: 09:30).";
                return;
            }

            if (!TryParseTime(rawEnd, out var parsedEnd))
            {
                ValidationMessage = "Invalid end time. Use HH:mm format (example: 11:15).";
                return;
            }

            if (parsedEnd <= parsedStart)
            {
                ValidationMessage = "End time must be after start time.";
                return;
            }

            var now = TimeOnly.FromDateTime(DateTime.Now);
            if (parsedEnd > now)
            {
                ValidationMessage = "End time cannot be in the future.";
                return;
            }

            task.StartTime = parsedStart.ToString("HH:mm", CultureInfo.InvariantCulture);
            task.EndTime = parsedEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
            task.Duration = ComputeDuration(parsedStart, parsedEnd);
        }
        else
        {
            task.StartTime = string.Empty;
            task.EndTime = string.Empty;
        }

        task.Name = trimmedName;

        if (task.Id > 0)
        {
            _taskStore.UpdateTask(task.Id, task.Name, task.Duration, task.StartTime, task.EndTime);
        }

        RecalculateTotal();
        ValidationMessage = string.Empty;
    }

    private void HookTask(TaskEntry task)
    {
        task.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TaskEntry.IsCompleted))
            {
                if (task.Id > 0) _taskStore.UpdateCompleted(task.Id, task.IsCompleted);
            }
        };
    }

    private void RecalculateTotal()
    {
        var totalMinutes = 0.0;
        foreach (var task in Tasks)
        {
            totalMinutes += ParseDurationMinutes(task.Duration);
        }

        var hours = totalMinutes / 60.0;
        TodayTotal = $"{hours:F1}h";
    }

    private static double ParseDurationMinutes(string duration)
    {
        // Supports "1:30" (h:mm) or "2h" or "45m" or "1.5h"
        if (string.IsNullOrWhiteSpace(duration))
        {
            return 0;
        }

        duration = duration.Trim().ToLowerInvariant();

        if (duration.EndsWith("h") && double.TryParse(duration[..^1], CultureInfo.InvariantCulture, out var hours))
        {
            return hours * 60;
        }

        if (duration.EndsWith("m") && double.TryParse(duration[..^1], CultureInfo.InvariantCulture, out var mins))
        {
            return mins;
        }

        var parts = duration.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m))
        {
            return h * 60 + m;
        }

        return 30;
    }

    private static string ComputeDuration(TimeOnly start, TimeOnly end)
    {
        var diff = end.ToTimeSpan() - start.ToTimeSpan();
        if (diff < TimeSpan.Zero)
        {
            diff += TimeSpan.FromHours(24);
        }

        if (diff == TimeSpan.Zero)
        {
            diff = TimeSpan.FromMinutes(30);
        }

        return $"{(int)diff.TotalHours}:{diff.Minutes:D2}";
    }

    private void UpdateClockTexts()
    {
        var now = DateTime.Now;
        ClockTimeText = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        ClockDateText = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
    }

    public void StartNow()
    {
        _controller.StartNow();
    }

    public void StopNow()
    {
        _controller.StopNow();
    }

    public void ApplySchedule()
    {
        if (!TryBuildSchedule(out var configuration, out var error))
        {
            ValidationMessage = error;
            return;
        }

        try
        {
            var snapshot = _controller.ApplySchedule(configuration);
            ApplySnapshot(snapshot);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Failed to apply timer: {ex.Message}";
        }
    }

    public void ClearSchedule()
    {
        try
        {
            var snapshot = _controller.ClearSchedule();
            ApplySnapshot(snapshot);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Failed to clear timer: {ex.Message}";
        }
    }

    public void ExitApplication()
    {
        _shutdownAction();
    }

    public void SetStartTimeFromDial(TimeOnly time)
    {
        IsStartTimerEnabled = true;
        StartTimeText = time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public void SetStopTimeFromDial(TimeOnly time)
    {
        IsStopTimerEnabled = true;
        StopTimeText = time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public void ShowRuntimeError(string message)
    {
        ValidationMessage = message;
    }

    private bool TryBuildSchedule(out ScheduleConfiguration configuration, out string error)
    {
        error = string.Empty;
        configuration = ScheduleConfiguration.Disabled;

        TimeOnly? startTime = null;
        TimeOnly? stopTime = null;

        if (IsStartTimerEnabled)
        {
            if (!TryParseTime(StartTimeText, out var parsed))
            {
                error = "Invalid start time. Use HH:mm format (example: 08:30).";
                return false;
            }

            startTime = parsed;
        }

        if (IsStopTimerEnabled)
        {
            if (!TryParseTime(StopTimeText, out var parsed))
            {
                error = "Invalid stop time. Use HH:mm format (example: 17:45).";
                return false;
            }

            stopTime = parsed;
        }

        configuration = new ScheduleConfiguration(
            StartEnabled: IsStartTimerEnabled,
            StartTime: startTime,
            StopEnabled: IsStopTimerEnabled,
            StopTime: stopTime);

        return true;
    }

    private static bool TryParseTime(string value, out TimeOnly result)
    {
        if (TimeOnly.TryParseExact(
                value,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result))
        {
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTime))
        {
            result = TimeOnly.FromDateTime(dateTime);
            return true;
        }

        result = default;
        return false;
    }

    private void OnStatusChanged(WorkerStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = ToDisplayStatus(status);
            IsRunning = IsRunningStatus(status);
            ValidationMessage = string.Empty;
        });
    }

    private void OnScheduleChanged(ScheduleSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => { ApplySnapshot(snapshot); });
    }

    private void OnControllerError(string message)
    {
        Dispatcher.UIThread.Post(() => { ValidationMessage = message; });
    }

    private void ApplySnapshot(ScheduleSnapshot snapshot)
    {
        IsStartTimerEnabled = snapshot.StartEnabled;
        IsStopTimerEnabled = snapshot.StopEnabled;

        if (snapshot.StartTime.HasValue)
        {
            StartTimeText = snapshot.StartTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (snapshot.StopTime.HasValue)
        {
            StopTimeText = snapshot.StopTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        ScheduleSummary = BuildScheduleSummary(snapshot);
    }

    private static string BuildScheduleSummary(ScheduleSnapshot snapshot)
    {
        if (!snapshot.StartEnabled && !snapshot.StopEnabled)
        {
            return "No timers configured.";
        }

        var nextStart = snapshot.NextStartAt.HasValue
            ? snapshot.NextStartAt.Value.ToString("ddd HH:mm", CultureInfo.InvariantCulture)
            : "--";

        var nextStop = snapshot.NextStopAt.HasValue
            ? snapshot.NextStopAt.Value.ToString("ddd HH:mm", CultureInfo.InvariantCulture)
            : "--";

        var startText = snapshot.StartEnabled && snapshot.StartTime.HasValue
            ? $"Start: {snapshot.StartTime.Value:HH:mm} (next {nextStart})"
            : "Start: disabled";

        var stopText = snapshot.StopEnabled && snapshot.StopTime.HasValue
            ? $"Stop: {snapshot.StopTime.Value:HH:mm} (next {nextStop})"
            : "Stop: disabled";

        return $"{startText} | {stopText}";
    }

    private static string ToDisplayStatus(WorkerStatus status)
    {
        return status switch
        {
            WorkerStatus.Start => "Running",
            WorkerStatus.Delay => "Waiting to start",
            WorkerStatus.Until => "Running with stop timer",
            _ => "Stopped"
        };
    }

    private static bool IsRunningStatus(WorkerStatus status)
    {
        return status is WorkerStatus.Start or WorkerStatus.Until;
    }
}
