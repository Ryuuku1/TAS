namespace TAS.Services;

using System;

public readonly record struct ScheduleSnapshot(
    bool StartEnabled,
    TimeOnly? StartTime,
    DateTime? NextStartAt,
    bool StopEnabled,
    TimeOnly? StopTime,
    DateTime? NextStopAt)
{
    public static ScheduleSnapshot Disabled => new(
        StartEnabled: false,
        StartTime: null,
        NextStartAt: null,
        StopEnabled: false,
        StopTime: null,
        NextStopAt: null);
}
