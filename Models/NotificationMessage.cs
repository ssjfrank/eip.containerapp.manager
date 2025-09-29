namespace ContainerManager.Service.Models;

public class NotificationMessage
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ContainerApp { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "RESTART", "STOP"
    public string Status { get; set; } = string.Empty; // "SUCCESS", "FAILURE", "WARNING"
    public string Message { get; set; } = string.Empty;
    public string QueueName { get; set; } = string.Empty;
}