namespace TAS.Services;

using System;

public readonly record struct TimerSlotConfiguration(
    TimeOnly? Start,
    TimeOnly? End,
    bool IsEnabled);
