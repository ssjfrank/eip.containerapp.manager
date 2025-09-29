using System.ComponentModel.DataAnnotations;

namespace ContainerManager.Service.Configuration;

public class AzureSettings : IValidatableObject
{
    [Required(ErrorMessage = "Azure SubscriptionId is required")]
    [MinLength(1, ErrorMessage = "SubscriptionId cannot be empty")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Azure ResourceGroupName is required")]
    [MinLength(1, ErrorMessage = "ResourceGroupName cannot be empty")]
    public string ResourceGroupName { get; set; } = string.Empty;

    public bool UseManagedIdentity { get; set; } = true;

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(TenantId))
            {
                yield return new ValidationResult(
                    "TenantId is required when UseManagedIdentity is false",
                    new[] { nameof(TenantId) });
            }

            if (string.IsNullOrWhiteSpace(ClientId))
            {
                yield return new ValidationResult(
                    "ClientId is required when UseManagedIdentity is false",
                    new[] { nameof(ClientId) });
            }

            if (string.IsNullOrWhiteSpace(ClientSecret))
            {
                yield return new ValidationResult(
                    "ClientSecret is required when UseManagedIdentity is false",
                    new[] { nameof(ClientSecret) });
            }
        }
    }
}