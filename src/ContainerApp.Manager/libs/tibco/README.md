# TIBCO EMS Manual DLL Files

## Required Files

This directory should contain the following DLL files from your TIBCO EMS installation:

### Required DLLs
- `TIBCO.EMS.ADMIN.dll` - TIBCO EMS Administration API
- `TIBCO.EMS.UFO.dll` - TIBCO EMS Unified Field Object (if required)

### Optional Dependencies
Depending on your TIBCO EMS version, you may also need:
- `TIBCO.EMS.FAULT.dll`
- Other TIBCO EMS dependencies as required

## How to Obtain These Files

1. **Install TIBCO EMS** on your development machine
2. **Locate Installation Directory** (typically):
   - Windows: `C:\tibco\ems\[version]\lib\`
   - Linux: `/opt/tibco/ems/[version]/lib/`

3. **Copy Required DLLs** from the installation lib directory to this folder

## Version Compatibility

- Ensure DLL versions match your TIBCO EMS server version
- Currently configured for TIBCO EMS 10.4.0 
- Check DLL properties for version information

## Development Setup

After copying the DLLs:
1. Build the project: `dotnet build`
2. Verify references are resolved correctly
3. Update configuration with admin credentials

## Docker Deployment

These DLL files will be copied into the Docker image during the build process.
Ensure they are present before building the container image.

## License Considerations

⚠️ **Important**: Ensure you have proper licensing for distributing these TIBCO DLL files.
Check with your TIBCO licensing agreement before including in source control or distribution.

## Troubleshooting

If you encounter reference errors:
1. Verify DLL files are present in this directory
2. Check DLL version compatibility
3. Ensure admin credentials are configured
4. Review project file references

## Alternative: Development-Only Setup

If you cannot include DLLs in source control:
1. Add `libs/tibco/*.dll` to `.gitignore`
2. Document DLL setup in team onboarding
3. Consider using a shared network location for team access