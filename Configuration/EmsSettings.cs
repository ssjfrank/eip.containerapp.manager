using System.ComponentModel.DataAnnotations;

namespace ContainerManager.Service.Configuration;

public class EmsSettings : IValidatableObject
{
    [Required(ErrorMessage = "EMS ServerUrl is required")]
    [RegularExpression(@"^(tcp|ssl)://.*:\d+$", ErrorMessage = "ServerUrl must be in format tcp://host:port or ssl://host:port")]
    public string ServerUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "EMS Username is required")]
    public string Username { get; set; } = string.Empty;

    // Password can be empty for some EMS configurations
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "NotificationQueueName is required")]
    [MinLength(1, ErrorMessage = "NotificationQueueName cannot be empty")]
    public string NotificationQueueName { get; set; } = "NOTIFICATION.QUEUE";

    // SSL Configuration (optional, only used when ServerUrl starts with ssl://)

    /// <summary>
    /// SSL target hostname for certificate validation (optional).
    /// If not specified, hostname from ServerUrl will be used.
    /// </summary>
    public string? SslTargetHostName { get; set; }

    /// <summary>
    /// Enable SSL trace/debugging (optional, default: false)
    /// </summary>
    public bool SslTrace { get; set; } = false;

    /// <summary>
    /// Path to client certificate file for mutual TLS (optional)
    /// </summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>
    /// Password for client certificate (optional)
    /// </summary>
    public string? ClientCertificatePassword { get; set; }

    /// <summary>
    /// Path to trust store containing server certificates (optional)
    /// </summary>
    public string? TrustStorePath { get; set; }

    /// <summary>
    /// Password for trust store (optional)
    /// </summary>
    public string? TrustStorePassword { get; set; }

    /// <summary>
    /// Whether to verify server hostname against certificate (default: true)
    /// </summary>
    public bool VerifyHostName { get; set; } = true;

    /// <summary>
    /// Whether to verify server certificate (default: true)
    /// Set to false only for testing with self-signed certificates
    /// </summary>
    public bool VerifyServerCertificate { get; set; } = true;

    public bool IsSSL => ServerUrl.StartsWith("ssl://", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate SSL-specific settings if using SSL
        if (IsSSL)
        {
            if (!string.IsNullOrEmpty(ClientCertificatePath) && !File.Exists(ClientCertificatePath))
            {
                yield return new ValidationResult(
                    $"Client certificate file not found: {ClientCertificatePath}",
                    new[] { nameof(ClientCertificatePath) });
            }

            if (!string.IsNullOrEmpty(TrustStorePath) && !File.Exists(TrustStorePath))
            {
                yield return new ValidationResult(
                    $"Trust store file not found: {TrustStorePath}",
                    new[] { nameof(TrustStorePath) });
            }
        }
    }
}