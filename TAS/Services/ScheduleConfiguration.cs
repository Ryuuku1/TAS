namespace TAS.Services;

using System;

public readonly record struct ScheduleConfiguration(
    bool StartEnabled,
    TimeOnly? StartTime,
    bool StopEnabled,
    TimeOnly? StopTime)
{
    public static ScheduleConfiguration Disabled => new(
        StartEnabled: false,
        StartTime: null,
        StopEnabled: false,
        StopTime: null);
}
