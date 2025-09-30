using System.ComponentModel.DataAnnotations;

namespace ContainerManager.Service.Configuration;

public class ManagerSettings : IValidatableObject
{
    [Range(1, 3600, ErrorMessage = "PollingIntervalSeconds must be between 1 and 3600")]
    public int PollingIntervalSeconds { get; set; } = 30;

    [Range(1, 1440, ErrorMessage = "IdleTimeoutMinutes must be between 1 and 1440")]
    public int IdleTimeoutMinutes { get; set; } = 10;

    [Range(1, 60, ErrorMessage = "RestartVerificationTimeoutMinutes must be between 1 and 60")]
    public int RestartVerificationTimeoutMinutes { get; set; } = 5;

    [Range(1, 300, ErrorMessage = "RestartDelaySeconds must be between 1 and 300")]
    public int RestartDelaySeconds { get; set; } = 5;

    [Required(ErrorMessage = "QueueContainerMappings is required")]
    [MinLength(1, ErrorMessage = "At least one queue-container mapping is required")]
    public Dictionary<string, List<string>> QueueContainerMappings { get; set; } = new();

    [Required(ErrorMessage = "NotificationEmailRecipient is required")]
    public string NotificationEmailRecipient { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate NotificationEmailRecipient (supports semicolon or comma-separated list)
        if (!string.IsNullOrWhiteSpace(NotificationEmailRecipient))
        {
            var emails = NotificationEmailRecipient.Split(new[] { ";", "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrWhiteSpace(e));

            foreach (var email in emails)
            {
                if (!IsValidEmail(email))
                {
                    yield return new ValidationResult(
                        $"Invalid email format in NotificationEmailRecipient: {email}",
                        new[] { nameof(NotificationEmailRecipient) });
                }
            }
        }

        foreach (var mapping in QueueContainerMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key))
            {
                yield return new ValidationResult(
                    "Container app name cannot be empty",
                    new[] { nameof(QueueContainerMappings) });
            }

            if (mapping.Value == null || mapping.Value.Count == 0)
            {
                yield return new ValidationResult(
                    $"Container app '{mapping.Key}' must have at least one queue mapped",
                    new[] { nameof(QueueContainerMappings) });
            }

            // Check for empty queue names
            if (mapping.Value != null && mapping.Value.Any(string.IsNullOrWhiteSpace))
            {
                yield return new ValidationResult(
                    $"Container app '{mapping.Key}' has empty queue names in mapping",
                    new[] { nameof(QueueContainerMappings) });
            }
        }
    }

    private static bool IsValidEmail(string email)
    {
        const string emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        return System.Text.RegularExpressions.Regex.IsMatch(email, emailPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}