namespace TAS.Services;

public sealed record ScheduleSnapshot(IReadOnlyList<TimerSlotSnapshot> Slots)
{
    public static ScheduleSnapshot Disabled => new([]);
}
