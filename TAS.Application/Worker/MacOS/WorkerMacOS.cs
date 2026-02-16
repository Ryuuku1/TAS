namespace TAS.Application.Worker.MacOS
{
    using System.Diagnostics;
    using TAS.Infrastructure.Worker;
    using TAS.Infrastructure.Worker.Abstractions;

    public sealed class WorkerMacOS : IWorker
    {
        private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(60);

        private readonly object _sync = new();
        private readonly Timer _heartbeatTimer;
        private readonly Timer _delayTimer;
        private readonly Timer _untilTimer;

        private readonly WorkerCallback _callback;
        private readonly Action<WorkerStatus> _statusChanged;
        private readonly ProcessStartInfo _processStartInfo;

        private Process? _caffeinateProcess;
        private bool _disposed;

        public WorkerMacOS(WorkerCallback callback, Action<WorkerStatus> statusChangedEventHandler)
        {
            _callback = callback;
            _statusChanged = statusChangedEventHandler;

            _heartbeatTimer = new Timer(_ => _callback(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _delayTimer = new Timer(_ => SafeExecute(Start), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _untilTimer = new Timer(_ => SafeExecute(Stop), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            _processStartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/caffeinate",
                Arguments = "-dims",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        public void Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                StopScheduleTimersUnsafe();
                StartCaffeinateUnsafe();
                _heartbeatTimer.Change(TimeSpan.Zero, HeartbeatPeriod);
            }

            NotifyStatus(WorkerStatus.Start);
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                StopAllTimersUnsafe();
                StopCaffeinateUnsafe();
            }

            NotifyStatus(WorkerStatus.Stop);
        }

        public void Delay(TimeSpan time)
        {
            var dueTime = NormalizeDueTime(time);

            lock (_sync)
            {
                ThrowIfDisposed();

                StopCaffeinateUnsafe();
                _heartbeatTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _untilTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _delayTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }

            NotifyStatus(WorkerStatus.Delay);
        }

        public void Until(TimeSpan time)
        {
            var dueTime = NormalizeDueTime(time);

            lock (_sync)
            {
                ThrowIfDisposed();

                _delayTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                StartCaffeinateUnsafe();
                _heartbeatTimer.Change(TimeSpan.Zero, HeartbeatPeriod);
                _untilTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }

            NotifyStatus(WorkerStatus.Until);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                StopAllTimersUnsafe();
                StopCaffeinateUnsafe();

                _heartbeatTimer.Dispose();
                _delayTimer.Dispose();
                _untilTimer.Dispose();

                _disposed = true;
            }
        }

        private void StopAllTimersUnsafe()
        {
            _heartbeatTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            StopScheduleTimersUnsafe();
        }

        private void StopScheduleTimersUnsafe()
        {
            _delayTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _untilTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void StartCaffeinateUnsafe()
        {
            if (_caffeinateProcess != null && !_caffeinateProcess.HasExited)
            {
                return;
            }

            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("Keep-awake mode is only supported on macOS.");
            }

            if (!File.Exists(_processStartInfo.FileName))
            {
                throw new InvalidOperationException($"Caffeinate executable was not found at '{_processStartInfo.FileName}'.");
            }

            try
            {
                var process = Process.Start(_processStartInfo);
                _caffeinateProcess = process ?? throw new InvalidOperationException("Unable to start caffeinate process.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unable to start keep-awake process (caffeinate).", ex);
            }
        }

        private void StopCaffeinateUnsafe()
        {
            if (_caffeinateProcess == null)
            {
                return;
            }

            if (!_caffeinateProcess.HasExited)
            {
                try
                {
                    _caffeinateProcess.Kill(entireProcessTree: true);
                    _caffeinateProcess.WaitForExit(2000);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited.
                }
            }

            _caffeinateProcess.Dispose();
            _caffeinateProcess = null;
        }

        private static TimeSpan NormalizeDueTime(TimeSpan time)
        {
            return time <= TimeSpan.Zero ? TimeSpan.Zero : time;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WorkerMacOS));
            }
        }

        private void NotifyStatus(WorkerStatus status)
        {
            _statusChanged?.Invoke(status);
        }

        private static void SafeExecute(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // Timers should never crash the process if an operation fails.
            }
        }
    }
}
