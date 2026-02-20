namespace TAS.Services;

using System;

public readonly record struct TimerSlotSnapshot(
    TimeOnly? Start,
    TimeOnly? End,
    bool IsEnabled,
    DateTime? NextStartAt,
    DateTime? NextStopAt);
