namespace TAS.Services;

using System;
using TAS.Application.Worker.MacOS;
using TAS.Application.Worker.Windows;
using TAS.Infrastructure.Worker;
using TAS.Infrastructure.Worker.Abstractions;

public sealed class TasController : IDisposable
{
    private readonly IWorker _worker;
    private readonly DailyScheduleService _scheduleService;

    private WorkerStatus _status;
    private bool _disposed;

    public TasController()
    {
        _status = WorkerStatus.Stop;
        OperatingSystemName = DetectOperatingSystem();
        _worker = CreateWorker(OnWorkerStatusChanged);
        _scheduleService = new DailyScheduleService(StartNow, StopNow);
        _scheduleService.ScheduleChanged += OnScheduleChanged;
    }

    public event Action<WorkerStatus>? StatusChanged;
    public event Action<ScheduleSnapshot>? ScheduleChanged;
    public event Action<string>? ErrorOccurred;

    public WorkerStatus Status => _status;
    public string OperatingSystemName { get; }

    public ScheduleSnapshot Schedule => _scheduleService.GetSnapshot();

    public void StartNow()
    {
        ThrowIfDisposed();
        try
        {
            _worker.Start();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to start keep-awake mode: {ex.Message}");
        }
    }

    public void StopNow()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _worker.Stop();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to stop keep-awake mode: {ex.Message}");
        }
    }

    public ScheduleSnapshot ApplySchedule(ScheduleConfiguration configuration)
    {
        ThrowIfDisposed();
        try
        {
            return _scheduleService.Configure(configuration);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to apply timer: {ex.Message}");
            throw;
        }
    }

    public ScheduleSnapshot ClearSchedule()
    {
        ThrowIfDisposed();
        try
        {
            return _scheduleService.Configure(ScheduleConfiguration.Disabled);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to clear timer: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _scheduleService.ScheduleChanged -= OnScheduleChanged;
        _scheduleService.Dispose();

        _worker.Stop();
        _worker.Dispose();
    }

    private void OnScheduleChanged(ScheduleSnapshot snapshot)
    {
        ScheduleChanged?.Invoke(snapshot);
    }

    private void OnWorkerStatusChanged(WorkerStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TasController));
        }
    }

    private static void NoOp()
    {
    }

    private static IWorker CreateWorker(Action<WorkerStatus> statusChanged)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new WorkerMacOS(NoOp, statusChanged);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WorkerWindows(NoOp, statusChanged);
        }

        throw new PlatformNotSupportedException("TAS keep-awake mode currently supports macOS and Windows.");
    }

    private static string DetectOperatingSystem()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        return "Unknown";
    }
}
