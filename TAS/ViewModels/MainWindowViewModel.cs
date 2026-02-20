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
    private string _startTimeText;
    private string _stopTimeText;
    private string _validationMessage;
    private bool _isRunning;
    private bool _isDisguised;
    private bool _isTrackerView;
    private bool _useStopTime = true;
    private bool _useStartTime = true;
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
        _validationMessage = string.Empty;
        _isRunning = IsRunningStatus(_controller.Status);

        _controller.StatusChanged += OnStatusChanged;
        _controller.ScheduleChanged += OnScheduleChanged;
        _controller.ErrorOccurred += OnControllerError;

        LoadAndApplyTimers();
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string OperatingSystemText => $"OS: {_controller.OperatingSystemName}";

    public ObservableCollection<TimerSlotEntry> Timers { get; } = new();
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

    public bool UseStopTime
    {
        get => _useStopTime;
        set => SetProperty(ref _useStopTime, value);
    }

    public bool UseStartTime
    {
        get => _useStartTime;
        set => SetProperty(ref _useStartTime, value);
    }

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

    public void AddTimer()
    {
        var hasStart = UseStartTime;
        var hasStop = UseStopTime;

        if (!hasStart && !hasStop)
        {
            ValidationMessage = "Enable at least a start or stop time.";
            return;
        }

        TimeOnly? start = null;
        TimeOnly? end = null;

        if (hasStart)
        {
            if (!TryParseTime(StartTimeText, out var parsedStart))
            {
                ValidationMessage = "Invalid start time. Use HH:mm format (example: 08:30).";
                return;
            }

            start = parsedStart;
        }

        if (hasStop)
        {
            if (!TryParseTime(StopTimeText, out var parsedEnd))
            {
                ValidationMessage = "Invalid stop time. Use HH:mm format (example: 17:45).";
                return;
            }

            end = parsedEnd;
        }

        if (start.HasValue && end.HasValue && start.Value >= end.Value)
        {
            ValidationMessage = "Stop time must be after start time.";
            return;
        }

        foreach (var existing in Timers)
        {
            if (!existing.IsEnabled || !existing.HasEndTime || !existing.HasStartTime) continue;
            if (!TryParseTime(existing.StartTime, out var exStart)) continue;
            if (!TryParseTime(existing.EndTime, out var exEnd)) continue;
            if (!start.HasValue || !end.HasValue) continue;

            if (start.Value < exEnd && exStart < end.Value)
            {
                ValidationMessage = $"Overlaps with existing timer {existing.Summary}.";
                return;
            }
        }

        var startText = start?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        var endText = end?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        var entry = new TimerSlotEntry(startText, endText, isEnabled: true);
        HookTimerSlot(entry);
        Timers.Add(entry);
        PersistAndApplyTimers();
        ValidationMessage = string.Empty;
    }

    public void RemoveTimer(TimerSlotEntry slot)
    {
        Timers.Remove(slot);
        PersistAndApplyTimers();
        ValidationMessage = string.Empty;
    }

    public void ClearSchedule()
    {
        Timers.Clear();
        _taskStore.SaveTimerSlots([]);
        try
        {
            _controller.ClearSchedule();
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Failed to clear timers: {ex.Message}";
        }
    }

    public void ExitApplication()
    {
        _shutdownAction();
    }

    public void SetStartTimeFromDial(TimeOnly time)
    {
        StartTimeText = time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public void SetStopTimeFromDial(TimeOnly time)
    {
        StopTimeText = time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public void ShowRuntimeError(string message)
    {
        ValidationMessage = message;
    }

    private void LoadAndApplyTimers()
    {
        Timers.Clear();
        foreach (var (start, end, enabled) in _taskStore.LoadTimerSlots())
        {
            var entry = new TimerSlotEntry(start, end, enabled);
            HookTimerSlot(entry);
            Timers.Add(entry);
        }

        ApplyTimersToController();
    }

    private void PersistAndApplyTimers()
    {
        _taskStore.SaveTimerSlots(
            Timers.Select(t => (t.StartTime, t.EndTime, t.IsEnabled)).ToList());
        ApplyTimersToController();
    }

    private void ApplyTimersToController()
    {
        var slots = Timers
            .Where(t => t.HasStartTime || t.HasEndTime)
            .Select(t =>
            {
                TimeOnly? s = t.HasStartTime && TryParseTime(t.StartTime, out var parsedStart)
                    ? parsedStart
                    : null;
                TimeOnly? e = t.HasEndTime && TryParseTime(t.EndTime, out var parsedEnd)
                    ? parsedEnd
                    : null;
                return new TimerSlotConfiguration(s, e, t.IsEnabled);
            })
            .ToList();

        var config = new ScheduleConfiguration(slots);
        try
        {
            var snapshot = _controller.ApplySchedule(config);
            UpdateNextRunTexts(snapshot);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
    }

    private void HookTimerSlot(TimerSlotEntry slot)
    {
        slot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TimerSlotEntry.IsEnabled))
            {
                PersistAndApplyTimers();
            }
        };
    }

    private void UpdateNextRunTexts(ScheduleSnapshot snapshot)
    {
        var count = Math.Min(Timers.Count, snapshot.Slots.Count);
        for (var i = 0; i < count; i++)
        {
            var s = snapshot.Slots[i];
            Timers[i].NextRunText = s.IsEnabled && s.NextStartAt.HasValue
                ? $"next {s.NextStartAt.Value.ToString("ddd HH:mm", CultureInfo.InvariantCulture)}"
                : string.Empty;
        }
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
        Dispatcher.UIThread.Post(() => { UpdateNextRunTexts(snapshot); });
    }

    private void OnControllerError(string message)
    {
        Dispatcher.UIThread.Post(() => { ValidationMessage = message; });
    }

    private void ApplySnapshot(ScheduleSnapshot snapshot)
    {
        UpdateNextRunTexts(snapshot);
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
