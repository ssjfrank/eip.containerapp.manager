namespace ContainerApp.Manager.Config;

public sealed class MonitorOptions
{
    public int PollIntervalSeconds { get; set; } = 15;
    public int CooldownMinutes { get; set; } = 5;
    public IList<AppMapping> Mappings { get; set; } = new List<AppMapping>();
    public MessageProcessingAlerts MessageProcessingAlerts { get; set; } = new();
}

public sealed class AppMapping
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string ContainerApp { get; set; } = string.Empty;
    public int DesiredReplicas { get; set; } = 1;
    public IList<string> Queues { get; set; } = new List<string>();
    public IList<ScheduleWindow> Schedules { get; set; } = new List<ScheduleWindow>();
    public IList<string> NotifyEmails { get; set; } = new List<string>();
    
    // New retry mechanism settings
    public int MaxRestartAttempts { get; set; } = 3;
    public int RestartCooldownMinutes { get; set; } = 5;
    public int ConsumerTimeoutMinutes { get; set; } = 10;
    public int StartupGracePeriodMinutes { get; set; } = 3;
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
    
    // Retry mechanism tracking
    public int RestartAttemptCount { get; set; }
    public DateTimeOffset? LastRestartTime { get; set; }
    public List<RestartAttempt> RestartHistory { get; set; } = new();
    
    // Schedule tracking
    public DateTimeOffset? LastScheduleStart { get; set; }
    public DateTimeOffset? ScheduleActiveUntil { get; set; }
    
    // Per-queue consumer tracking
    public Dictionary<string, QueueConsumerState> QueueConsumerStatus { get; set; } = new();
}

public sealed class RestartAttempt
{
    public DateTimeOffset Timestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public bool Success { get; set; }
}

public sealed class QueueConsumerState
{
    public DateTimeOffset? LastConsumerSeen { get; set; }
    public DateTimeOffset? LastMessageSeen { get; set; }
    public bool HasActiveConsumers { get; set; }
    public int MessageCount { get; set; }
    
    // Message processing duration tracking
    public DateTimeOffset? FirstMessageSeenAt { get; set; }
    public DateTimeOffset? LastProcessingAlert { get; set; }
    public int ProcessingAlertCount { get; set; }
}

public sealed class MessageProcessingAlerts
{
    public int FirstAlertMinutes { get; set; } = 20;
    public int FollowupIntervalMinutes { get; set; } = 5;
    public int MaxAlerts { get; set; } = 6;
    public IList<string> AlertEmails { get; set; } = new List<string>();
    public bool Enabled { get; set; } = true;
}


