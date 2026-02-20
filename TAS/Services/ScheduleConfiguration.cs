namespace TAS.Services;

public sealed record ScheduleConfiguration(IReadOnlyList<TimerSlotConfiguration> Slots)
{
    public static ScheduleConfiguration Disabled => new([]);
}
