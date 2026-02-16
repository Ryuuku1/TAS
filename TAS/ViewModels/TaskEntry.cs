namespace TAS.ViewModels;

public sealed class TaskEntry : ObservableObject
{
    private string _name;
    private string _duration;
    private string _startTime;
    private string _endTime;
    private bool _isCompleted;

    /// <summary>Database row id (0 if not persisted yet).</summary>
    public long Id { get; set; }

    public TaskEntry(string name, string duration, string startTime = "", string endTime = "")
    {
        _name = name;
        _duration = duration;
        _startTime = startTime;
        _endTime = endTime;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public string StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(TimeRange));
                OnPropertyChanged(nameof(HasTimeRange));
            }
        }
    }

    public string EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                OnPropertyChanged(nameof(TimeRange));
                OnPropertyChanged(nameof(HasTimeRange));
            }
        }
    }

    public string TimeRange => $"{StartTime} - {EndTime}";

    public bool HasTimeRange =>
        !string.IsNullOrWhiteSpace(StartTime)
        && !string.IsNullOrWhiteSpace(EndTime);

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }
}
