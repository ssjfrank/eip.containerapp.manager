using System.ComponentModel.DataAnnotations;

namespace ContainerManager.Service.Configuration;

public class AzureSettings
{
    [Required(ErrorMessage = "Azure SubscriptionId is required")]
    [MinLength(1, ErrorMessage = "SubscriptionId cannot be empty")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Azure ResourceGroupName is required")]
    [MinLength(1, ErrorMessage = "ResourceGroupName cannot be empty")]
    public string ResourceGroupName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Azure ManagedIdentityClientId is required")]
    [MinLength(1, ErrorMessage = "ManagedIdentityClientId cannot be empty")]
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}