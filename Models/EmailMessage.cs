using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace ContainerManager.Service.Models;

public class EmailMessage
{
    [JsonProperty("subject", Required = Required.Always)]
    [Required(ErrorMessage = "Subject is required")]
    [StringLength(998, ErrorMessage = "Subject cannot exceed 998 characters")]
    public string Subject { get; set; } = string.Empty;

    [JsonProperty("message", Required = Required.Always)]
    [Required(ErrorMessage = "Body is required")]
    public string Body { get; set; } = string.Empty;

    [JsonProperty("to", Required = Required.Always)]
    [Required(ErrorMessage = "ToEmail is required")]
    public string ToEmail { get; set; } = string.Empty;

    public bool IsValid(out List<string> validationErrors)
    {
        validationErrors = new List<string>();
        var context = new ValidationContext(this);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(this, context, results, true))
        {
            validationErrors.AddRange(results.Select(r => r.ErrorMessage ?? "Unknown validation error"));
        }

        // Email validation
        if (!string.IsNullOrWhiteSpace(ToEmail))
        {
            var emails = ToEmail.Split(new[] { ";", "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrWhiteSpace(e));

            foreach (var email in emails)
            {
                if (!IsValidEmail(email))
                {
                    validationErrors.Add($"Invalid email format: {email}");
                }
            }
        }

        return !validationErrors.Any();
    }

    private static bool IsValidEmail(string email)
    {
        const string emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        return Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase);
    }
}
