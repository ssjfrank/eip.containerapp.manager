namespace ContainerApp.Manager.Config;

public sealed class MonitorOptions
{
    public int PollIntervalSeconds { get; set; } = 15;
    public int CooldownMinutes { get; set; } = 5;
    public IList<AppMapping> Mappings { get; set; } = new List<AppMapping>();
}

public sealed class AppMapping
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string ContainerApp { get; set; } = string.Empty;
    public int DesiredReplicas { get; set; } = 1;
    public int StartThreshold { get; set; } = 1;
    public int IdleTimeoutMinutes { get; set; } = 10;
    public int NoListenerTimeoutMinutes { get; set; } = 3;
    public IList<string> Queues { get; set; } = new List<string>();
    public IList<ScheduleWindow> Schedules { get; set; } = new List<ScheduleWindow>();
    public IList<string> NotifyEmails { get; set; } = new List<string>();
}

public sealed class ScheduleWindow
{
    public string Cron { get; set; } = string.Empty;
    public int DesiredReplicas { get; set; } = 1;
    public string? WindowLabel { get; set; }
    public int DurationMinutes { get; set; } = 60;
}

public sealed class RuntimeState
{
    public DateTimeOffset? LastStart { get; set; }
    public DateTimeOffset? LastStop { get; set; }
    public DateTimeOffset? LastRestart { get; set; }
    public string? LastAction { get; set; }
    public string? LastActionResult { get; set; }
    public DateTimeOffset? CooldownUntil { get; set; }
    public DateTimeOffset? LastNonZeroDepthAt { get; set; }
}


