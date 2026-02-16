namespace TAS.Services;

using System;

public sealed class DailyScheduleService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly Timer _timer;
    private readonly Action _startAction;
    private readonly Action _stopAction;

    private ScheduleConfiguration _configuration;
    private DateTime? _nextStartAt;
    private DateTime? _nextStopAt;
    private bool _disposed;

    public DailyScheduleService(Action startAction, Action stopAction)
    {
        _startAction = startAction;
        _stopAction = stopAction;
        _configuration = ScheduleConfiguration.Disabled;

        _timer = new Timer(OnTick, null, TickInterval, TickInterval);
    }

    public event Action<ScheduleSnapshot>? ScheduleChanged;

    public ScheduleSnapshot Configure(ScheduleConfiguration configuration)
    {
        ScheduleSnapshot snapshot;

        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateConfiguration(configuration);

            _configuration = configuration;
            RecalculateNextRunsUnsafe(DateTime.Now);
            snapshot = BuildSnapshotUnsafe();
        }

        ScheduleChanged?.Invoke(snapshot);
        return snapshot;
    }

    public ScheduleSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return BuildSnapshotUnsafe();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }

    private void OnTick(object? state)
    {
        bool shouldStart = false;
        bool shouldStop = false;
        ScheduleSnapshot snapshot;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTime.Now;

            if (_nextStartAt.HasValue && now >= _nextStartAt.Value)
            {
                shouldStart = true;
                _nextStartAt = _nextStartAt.Value.AddDays(1);
            }

            if (_nextStopAt.HasValue && now >= _nextStopAt.Value)
            {
                shouldStop = true;
                _nextStopAt = _nextStopAt.Value.AddDays(1);
            }

            snapshot = BuildSnapshotUnsafe();
        }

        if (shouldStart)
        {
            _startAction();
        }

        if (shouldStop)
        {
            _stopAction();
        }

        if (shouldStart || shouldStop)
        {
            ScheduleChanged?.Invoke(snapshot);
        }
    }

    private static void ValidateConfiguration(ScheduleConfiguration configuration)
    {
        if (configuration.StartEnabled && !configuration.StartTime.HasValue)
        {
            throw new InvalidOperationException("Start time is required when start timer is enabled.");
        }

        if (configuration.StopEnabled && !configuration.StopTime.HasValue)
        {
            throw new InvalidOperationException("Stop time is required when stop timer is enabled.");
        }
    }

    private void RecalculateNextRunsUnsafe(DateTime now)
    {
        _nextStartAt = _configuration.StartEnabled && _configuration.StartTime.HasValue
            ? NextOccurrence(_configuration.StartTime.Value, now)
            : null;

        _nextStopAt = _configuration.StopEnabled && _configuration.StopTime.HasValue
            ? NextOccurrence(_configuration.StopTime.Value, now)
            : null;
    }

    private ScheduleSnapshot BuildSnapshotUnsafe()
    {
        return new ScheduleSnapshot(
            StartEnabled: _configuration.StartEnabled,
            StartTime: _configuration.StartTime,
            NextStartAt: _nextStartAt,
            StopEnabled: _configuration.StopEnabled,
            StopTime: _configuration.StopTime,
            NextStopAt: _nextStopAt);
    }

    private static DateTime NextOccurrence(TimeOnly time, DateTime now)
    {
        var candidate = now.Date + time.ToTimeSpan();
        return candidate <= now ? candidate.AddDays(1) : candidate;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DailyScheduleService));
        }
    }
}
