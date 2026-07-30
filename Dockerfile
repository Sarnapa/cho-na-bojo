# Use the .NET 10 SDK for building the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy ONLY the .csproj files first to leverage Docker layer caching
COPY server/server.csproj server/
COPY shared/ChoNaBojo.Contracts.csproj shared/
COPY shared/ChoNaBojo.Utils.csproj shared/
COPY shared/ChoNaBojo.Validation.csproj shared/

# Restore dependencies for the main Web API project
RUN dotnet restore server/server.csproj

# Copy the rest of the source code
COPY server/ server/
COPY shared/ shared/

# Build and publish the application to the /app/out directory
# UseAppHost=false ensures we just build the .dll, keeping the image lighter
RUN dotnet publish server/server.csproj -c Release -o /app/out /p:UseAppHost=false

# Use the lightweight .NET 10 ASP.NET Core runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy the compiled output from the build stage
COPY --from=build /app/out .

# Expose port 8080 (the default fallback) for documentation purposes
EXPOSE 8080

# Execute the app using shell to evaluate the $PORT environment variable injected by Railway at runtime
# This mimics your custom start command perfectly
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet server.dll"]
