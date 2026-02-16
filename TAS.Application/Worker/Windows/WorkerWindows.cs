namespace TAS.Application.Worker.Windows
{
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using TAS.Infrastructure.Worker;
    using TAS.Infrastructure.Worker.Abstractions;

    public sealed class WorkerWindows : IWorker
    {
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;
        private const int InputKeyboard = 1;
        private const uint KeyEventFKeyUp = 0x0002;
        private const ushort VkF15 = 0x7E;
        private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(30);

        private readonly object _sync = new();
        private readonly Timer _heartbeatTimer;
        private readonly Timer _delayTimer;
        private readonly Timer _untilTimer;
        private readonly WorkerCallback _callback;
        private readonly Action<WorkerStatus> _statusChanged;

        private bool _disposed;

        public WorkerWindows(WorkerCallback callback, Action<WorkerStatus> statusChangedEventHandler)
        {
            _callback = callback;
            _statusChanged = statusChangedEventHandler;

            _heartbeatTimer = new Timer(_ => SafeExecute(OnHeartbeat), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _delayTimer = new Timer(_ => SafeExecute(Start), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _untilTimer = new Timer(_ => SafeExecute(Stop), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                StopScheduleTimersUnsafe();
                SendExecutionPulseUnsafe();
                _heartbeatTimer.Change(HeartbeatPeriod, HeartbeatPeriod);
            }

            NotifyStatus(WorkerStatus.Start);
            _callback();
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
            }

            NotifyStatus(WorkerStatus.Stop);
        }

        public void Delay(TimeSpan time)
        {
            var dueTime = NormalizeDueTime(time);

            lock (_sync)
            {
                ThrowIfDisposed();

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
                SendExecutionPulseUnsafe();
                _heartbeatTimer.Change(HeartbeatPeriod, HeartbeatPeriod);
                _untilTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }

            NotifyStatus(WorkerStatus.Until);
            _callback();
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

                _heartbeatTimer.Dispose();
                _delayTimer.Dispose();
                _untilTimer.Dispose();

                _disposed = true;
            }
        }

        private void OnHeartbeat()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                SendExecutionPulseUnsafe();
            }

            _callback();
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

        private static TimeSpan NormalizeDueTime(TimeSpan time)
        {
            return time <= TimeSpan.Zero ? TimeSpan.Zero : time;
        }

        private static void SendExecutionPulseUnsafe()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Keep-awake mode is only supported on Windows.");
            }

            var result = SetThreadExecutionState(EsSystemRequired | EsDisplayRequired);
            if (result == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to update Windows execution state.");
            }

            SendF15KeyPress();
        }

        private static void SendF15KeyPress()
        {
            var inputs = new Input[2];

            inputs[0].Type = InputKeyboard;
            inputs[0].Data.Keyboard.VirtualKey = VkF15;

            inputs[1].Type = InputKeyboard;
            inputs[1].Data.Keyboard.VirtualKey = VkF15;
            inputs[1].Data.Keyboard.Flags = KeyEventFKeyUp;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WorkerWindows));
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public int Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public nint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public nint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint Msg;
            public ushort ParamL;
            public ushort ParamH;
        }
    }
}
