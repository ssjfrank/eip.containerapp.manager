namespace ContainerManager.Service.Models;

public class QueueInfo
{
    public string Name { get; set; } = string.Empty;
    public long PendingMessageCount { get; set; }
    public int ReceiverCount { get; set; }
}