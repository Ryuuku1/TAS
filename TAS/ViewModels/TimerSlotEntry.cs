namespace TAS.ViewModels;

public sealed class TimerSlotEntry : ObservableObject
{
    private string _startTime;
    private string _endTime;
    private bool _isEnabled;
    private string _nextRunText = string.Empty;

    public TimerSlotEntry(string startTime, string endTime, bool isEnabled = true)
    {
        _startTime = startTime;
        _endTime = endTime;
        _isEnabled = isEnabled;
    }

    public string StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(HasStartTime));
            }
        }
    }

    public bool HasStartTime => !string.IsNullOrWhiteSpace(_startTime);

    public string EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(HasEndTime));
            }
        }
    }

    public bool HasEndTime => !string.IsNullOrWhiteSpace(_endTime);

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string NextRunText
    {
        get => _nextRunText;
        set
        {
            if (SetProperty(ref _nextRunText, value))
                OnPropertyChanged(nameof(HasNextRunText));
        }
    }

    public bool HasNextRunText => !string.IsNullOrWhiteSpace(_nextRunText);

    public string Summary
    {
        get
        {
            var s = HasStartTime ? StartTime : "∞";
            var e = HasEndTime ? EndTime : "∞";
            return $"{s} → {e}";
        }
    }
}
