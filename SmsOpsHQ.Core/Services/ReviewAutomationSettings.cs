namespace SmsOpsHQ.Core.Services;

// Persisted review automation options (API JSON file + Desktop Settings > Reviews).
public sealed class ReviewAutomationSettings
{
    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = 30;

    /// <summary>When true, the first scheduled tick after the API process starts runs immediately (then every IntervalMinutes).</summary>
    public bool RunOnStartup { get; set; }
}

public sealed class ReviewAutomationSchedulerStatus
{
    public bool SchedulerRunning { get; set; }

    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; }

    public bool RunOnStartup { get; set; }

    public string? SettingsFilePath { get; set; }
}
