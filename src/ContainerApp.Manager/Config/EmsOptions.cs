namespace ContainerApp.Manager.Config;

public sealed class EmsOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int ConnectionTimeoutMs { get; set; } = 30000;
    public int ReconnectDelayMs { get; set; } = 5000;
    public int MaxReconnectAttempts { get; set; } = 3;
    
    // Admin API Configuration
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public bool UseAdminAPI { get; set; } = true;
    public int AdminConnectionTimeoutMs { get; set; } = 15000;
    public bool FallbackToBasicMode { get; set; } = true;
}