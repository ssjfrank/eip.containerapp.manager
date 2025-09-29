namespace ContainerManager.Service.Models;

public class QueueIdleState
{
    public string QueueName { get; set; } = string.Empty;
    public DateTime? IdleStartTime { get; set; }
    public bool IsIdle => IdleStartTime.HasValue;

    /// <summary>
    /// Gets the duration for which the queue has been idle.
    /// Uses Math.Max(0, ...) to handle edge cases where system clock is adjusted backwards,
    /// preventing negative TimeSpan values that could cause unexpected behavior.
    /// </summary>
    public TimeSpan? IdleDuration => IdleStartTime.HasValue
        ? TimeSpan.FromTicks(Math.Max(0, (DateTime.UtcNow - IdleStartTime.Value).Ticks))
        : null;
}