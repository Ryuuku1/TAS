namespace TAS.Services;

using System;

public sealed class DailyScheduleService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly Timer _timer;
    private readonly Action _startAction;
    private readonly Action _stopAction;

    private List<SlotState> _slots = [];
    private bool _disposed;

    public DailyScheduleService(Action startAction, Action stopAction)
    {
        _startAction = startAction;
        _stopAction = stopAction;
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

            var now = DateTime.Now;
            _slots = configuration.Slots
                .Select(s => new SlotState
                {
                    Config = s,
                    NextStartAt = s.IsEnabled && s.Start.HasValue ? NextOccurrence(s.Start.Value, now) : null,
                    NextStopAt = s.IsEnabled && s.End.HasValue ? NextOccurrence(s.End.Value, now) : null
                })
                .ToList();

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
        bool anyFired = false;
        ScheduleSnapshot snapshot;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTime.Now;

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Config.IsEnabled)
                {
                    continue;
                }

                if (slot.NextStartAt.HasValue && now >= slot.NextStartAt.Value)
                {
                    shouldStart = true;
                    anyFired = true;
                    slot.NextStartAt = slot.NextStartAt.Value.AddDays(1);
                }

                if (slot.NextStopAt.HasValue && now >= slot.NextStopAt.Value)
                {
                    shouldStop = true;
                    anyFired = true;
                    slot.NextStopAt = slot.NextStopAt.Value.AddDays(1);
                }

                _slots[i] = slot;
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

        if (anyFired)
        {
            ScheduleChanged?.Invoke(snapshot);
        }
    }

    private static void ValidateConfiguration(ScheduleConfiguration configuration)
    {
        var enabled = configuration.Slots.Where(s => s.IsEnabled).ToList();

        foreach (var slot in enabled)
        {
            if (slot.Start.HasValue && slot.End.HasValue && slot.Start.Value >= slot.End.Value)
            {
                throw new InvalidOperationException(
                    $"Timer start ({slot.Start.Value:HH:mm}) must be before end ({slot.End.Value:HH:mm}).");
            }
        }

        for (var i = 0; i < enabled.Count; i++)
        {
            for (var j = i + 1; j < enabled.Count; j++)
            {
                var a = enabled[i];
                var b = enabled[j];
                if (!a.End.HasValue || !b.End.HasValue)
                {
                    continue;
                }

                if (!a.Start.HasValue || !b.Start.HasValue)
                {
                    continue;
                }

                if (a.Start.Value < b.End.Value && b.Start.Value < a.End.Value)
                {
                    throw new InvalidOperationException(
                        $"Timers {a.Start.Value:HH:mm}–{a.End.Value:HH:mm} and {b.Start.Value:HH:mm}–{b.End.Value:HH:mm} overlap.");
                }
            }
        }
    }

    private ScheduleSnapshot BuildSnapshotUnsafe()
    {
        return new ScheduleSnapshot(
            _slots.Select(s => new TimerSlotSnapshot(
                s.Config.Start,
                s.Config.End,
                s.Config.IsEnabled,
                s.NextStartAt,
                s.NextStopAt)).ToList());
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

    private struct SlotState
    {
        public TimerSlotConfiguration Config;
        public DateTime? NextStartAt;
        public DateTime? NextStopAt;
    }
}
