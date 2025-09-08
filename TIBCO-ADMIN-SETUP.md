# TIBCO EMS Admin API Setup Guide

## Overview

This application supports enhanced queue monitoring using the TIBCO EMS Administration API for more accurate consumer counts, message statistics, and queue information. The admin API provides significant advantages over basic queue browsing.

## Benefits of Admin API Integration

### Enhanced Monitoring Capabilities
- **Exact Consumer Counts**: Get precise active and total consumer counts per queue
- **Detailed Message Statistics**: Access message rates, throughput, and queue statistics
- **Message Age Information**: Determine the age of the oldest message in queues
- **Queue Health Metrics**: Monitor queue performance and health indicators
- **Real-time Statistics**: Access live throughput and performance data

### Better Scaling Decisions
- **Accurate Consumer Detection**: Know exactly how many consumers are processing messages
- **Performance Monitoring**: Make scaling decisions based on actual throughput data
- **Queue Analysis**: Understand queue behavior patterns for optimal scaling

## Required DLL Files

The following files must be obtained from your TIBCO EMS installation and placed in `src/ContainerApp.Manager/libs/tibco/`:

### Core Admin DLLs
- `TIBCO.EMS.ADMIN.dll` - Main administration API
- `TIBCO.EMS.UFO.dll` - Unified Field Objects (typically required)

### Potentially Required Dependencies
Depending on your TIBCO EMS version, you may also need:
- `TIBCO.EMS.FAULT.dll` - Fault tolerance support
- Other TIBCO-specific dependencies

## Setup Instructions

### 1. Locate TIBCO EMS Installation

**Windows:**
```
C:\tibco\ems\[version]\lib\
C:\Program Files\TIBCO\EMS\[version]\lib\
```

**Linux/Unix:**
```
/opt/tibco/ems/[version]/lib/
/usr/local/tibco/ems/[version]/lib/
```

### 2. Copy Required DLL Files

Copy the required DLL files to:
```
src/ContainerApp.Manager/libs/tibco/TIBCO.EMS.ADMIN.dll
src/ContainerApp.Manager/libs/tibco/TIBCO.EMS.UFO.dll
```

### 3. Configure Admin Credentials

Update your `appsettings.json` or environment variables:

```json
{
  "Ems": {
    "ConnectionString": "tcp://your-ems-server:7222",
    "Username": "regular-user",
    "Password": "regular-password",
    "AdminUsername": "admin-user",
    "AdminPassword": "admin-password",
    "UseAdminAPI": true,
    "AdminConnectionTimeoutMs": 15000,
    "FallbackToBasicMode": true
  }
}
```

### 4. Admin User Requirements

Ensure your admin user has the following permissions:
- **View Queue Information**: Read queue statistics and properties
- **Monitor Consumers**: Access consumer connection information
- **Query Server Statistics**: Read server-level performance metrics

## Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `UseAdminAPI` | Enable/disable admin API usage | `true` |
| `AdminUsername` | EMS admin username | `""` |
| `AdminPassword` | EMS admin password | `""` |
| `AdminConnectionTimeoutMs` | Admin connection timeout | `15000` |
| `FallbackToBasicMode` | Use basic mode if admin fails | `true` |

## Fallback Strategy

The application implements a robust fallback strategy:

1. **Primary Mode**: Attempt to use Admin API for enhanced monitoring
2. **Fallback Mode**: If admin API fails, fall back to basic queue browsing
3. **Configuration Control**: Control fallback behavior via `FallbackToBasicMode` setting

## Troubleshooting

### Common Issues

**DLL Not Found Errors:**
```
Could not load file or assembly 'TIBCO.EMS.ADMIN.dll'
```
- Verify DLL files are in `libs/tibco/` directory
- Check DLL version compatibility with your EMS server
- Ensure all dependencies are present

**Admin Connection Failures:**
```
Admin API requested but not available
```
- Check admin credentials are correct
- Verify admin user has required permissions
- Ensure EMS server allows admin connections

**Assembly Loading Issues:**
```
System.IO.FileNotFoundException: Could not load file or assembly
```
- Verify DLL files are copied to output directory
- Check project file references are correct
- Ensure Docker build includes DLL files

### Verification Steps

1. **Check DLL Availability:**
   - Verify files exist in `libs/tibco/` directory
   - Check file permissions are correct

2. **Test Configuration:**
   - Enable debug logging: `"ContainerApp.Manager": "Debug"`
   - Check application startup logs for admin API status

3. **Validate Credentials:**
   - Test admin credentials using TIBCO EMS admin tools
   - Verify user has required permissions

## Development vs Production

### Development Setup
- Copy DLLs to local development environment
- Use fallback mode for development without admin setup
- Test with debug configuration

### Production Deployment
- Include DLLs in Docker image build
- Configure proper admin credentials securely
- Monitor admin API connectivity and fallback scenarios

## Security Considerations

### Credential Management
- Store admin credentials securely (Azure Key Vault, etc.)
- Use separate admin accounts with minimal required permissions
- Rotate admin credentials regularly

### Network Security
- Secure EMS admin port access
- Use TLS/SSL for EMS connections when possible
- Monitor admin API access and usage

## License Compliance

⚠️ **Important**: Ensure you have proper licensing for:
- TIBCO EMS Admin API usage
- Distribution of TIBCO DLL files
- Admin API features in production

Consult your TIBCO licensing agreement before including DLL files in source control or distribution packages.

## Support and Maintenance

### Version Compatibility
- Keep admin DLLs synchronized with EMS server version
- Test admin API functionality after EMS server upgrades
- Monitor for deprecated admin API features

### Monitoring
- Monitor admin API connection health
- Track fallback scenarios and frequency
- Alert on admin API failures

### Updates
- Document DLL version and source
- Include DLL update process in deployment procedures
- Test admin API functionality in staging environment

## Next Steps After Setup

1. **Deploy DLL files** to your development environment
2. **Configure admin credentials** in your EMS server
3. **Update application configuration** with admin settings
4. **Test enhanced monitoring** in debug environment
5. **Verify fallback functionality** works correctly
6. **Deploy to production** with proper credential management