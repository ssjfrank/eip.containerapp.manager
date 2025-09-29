using System.ComponentModel.DataAnnotations;

namespace ContainerManager.Service.Configuration;

public class EmsSettings : IValidatableObject
{
    [Required(ErrorMessage = "EMS ServerUrl is required")]
    [RegularExpression(@"^tcp://.*:\d+$", ErrorMessage = "ServerUrl must be in format tcp://host:port")]
    public string ServerUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "EMS Username is required")]
    public string Username { get; set; } = string.Empty;

    // Password can be empty for some EMS configurations
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "NotificationQueueName is required")]
    [MinLength(1, ErrorMessage = "NotificationQueueName cannot be empty")]
    public string NotificationQueueName { get; set; } = "NOTIFICATION.QUEUE";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Additional validation if needed
        yield break;
    }
}