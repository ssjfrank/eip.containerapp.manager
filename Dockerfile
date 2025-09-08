# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and manual DLL dependencies first
COPY ./src/ContainerApp.Manager/ContainerApp.Manager.csproj ./src/ContainerApp.Manager/
COPY ./src/ContainerApp.Manager/libs ./src/ContainerApp.Manager/libs
RUN dotnet restore ./src/ContainerApp.Manager/ContainerApp.Manager.csproj

# Copy source code
COPY ./src ./src
RUN dotnet publish ./ContainerApp.Manager/ContainerApp.Manager.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContainerApp.Manager.dll"]


