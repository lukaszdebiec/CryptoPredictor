# Multi-stage build for .NET 9 ASP.NET Core & SQLite
# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (optimizing Docker layer caching)
COPY ["CryptoPredictor.csproj", "./"]
RUN dotnet restore "CryptoPredictor.csproj"

# Copy remaining source code and publish
COPY . .
RUN dotnet publish "CryptoPredictor.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Stage 2: Minimal Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install curl for container healthchecks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Copy published application from build stage
COPY --from=build /app/publish .

# Expose Web API and Dashboard port
EXPOSE 5078

# Default environment configuration
ENV ASPNETCORE_URLS=http://+:5078 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_RUNNING_IN_CONTAINER=true

# Entry point
ENTRYPOINT ["dotnet", "CryptoPredictor.dll"]
