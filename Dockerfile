# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published app from build stage
COPY --from=build /app/publish .

# Copy TIBCO EMS DLLs (they should already be in publish output)
# If not, uncomment the following lines:
# COPY Libs/TIBCO.EMS.dll ./
# COPY Libs/TIBCO.EMS.ADMIN.dll ./

# Create logs directory
RUN mkdir -p /app/logs

ENTRYPOINT ["dotnet", "ContainerManager.Service.dll"]