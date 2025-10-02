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

    [Range(1, 60, ErrorMessage = "OperationTimeoutMinutes must be between 1 and 60")]
    public int OperationTimeoutMinutes { get; set; } = 10;

    [Range(1, 120, ErrorMessage = "StuckOperationCleanupMinutes must be between 1 and 120")]
    public int StuckOperationCleanupMinutes { get; set; } = 15;

    [Required(ErrorMessage = "QueueContainerMappings is required")]
    [MinLength(1, ErrorMessage = "At least one queue-container mapping is required")]
    public Dictionary<string, List<string>> QueueContainerMappings { get; set; } = new();

    [Required(ErrorMessage = "NotificationEmailRecipient is required")]
    public string NotificationEmailRecipient { get; set; } = string.Empty;

    // Notification level settings - control when to send email alerts
    public bool NotifyOnSuccess { get; set; } = false;  // Skip SUCCESS notifications (reduce noise)
    public bool NotifyOnWarning { get; set; } = true;   // Send WARNING notifications (important alerts)
    public bool NotifyOnFailure { get; set; } = true;   // Send FAILURE notifications (critical alerts)

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

        // Validate timeout relationships - CRITICAL for proper operation
        // StuckOperationCleanupMinutes MUST be greater than OperationTimeoutMinutes
        // This ensures Layer 2 (timeout) gets chance to work before Layer 3 (force cleanup)
        if (StuckOperationCleanupMinutes <= OperationTimeoutMinutes)
        {
            yield return new ValidationResult(
                $"StuckOperationCleanupMinutes ({StuckOperationCleanupMinutes}) must be greater than OperationTimeoutMinutes ({OperationTimeoutMinutes}). " +
                $"Stuck cleanup is a safety net and should trigger AFTER normal timeout. Recommended: StuckOperationCleanupMinutes = OperationTimeoutMinutes + 5.",
                new[] { nameof(StuckOperationCleanupMinutes), nameof(OperationTimeoutMinutes) });
        }

        // Validate OperationTimeoutMinutes is sufficient for normal restart operations
        // Typical restart: Azure Stop (2-5 min) + RestartDelay (~0 min) + Azure Start (2-5 min) + Verification (5 min) = 9-15 min
        // OperationTimeoutMinutes should be at least RestartVerificationTimeoutMinutes + 2 min for Azure API calls
        if (OperationTimeoutMinutes < RestartVerificationTimeoutMinutes + 2)
        {
            yield return new ValidationResult(
                $"OperationTimeoutMinutes ({OperationTimeoutMinutes}) should be at least {RestartVerificationTimeoutMinutes + 2} minutes " +
                $"(RestartVerificationTimeoutMinutes + 2 minutes for Azure API calls). " +
                $"Current setting may cause false timeouts during normal restart operations. " +
                $"Recommended: OperationTimeoutMinutes >= {RestartVerificationTimeoutMinutes + 2}.",
                new[] { nameof(OperationTimeoutMinutes), nameof(RestartVerificationTimeoutMinutes) });
        }
    }

    private static bool IsValidEmail(string email)
    {
        const string emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        return System.Text.RegularExpressions.Regex.IsMatch(email, emailPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}