# The local server as a container: the product's API and MCP endpoint on Kestrel over Azure SQL (or a SQLite file).
# Build from src/, because the engine and the MCP server core are project references:
#   docker build -f adapters/azure/Product.Azure.Server/Dockerfile -t product-server .
# Dockerfile.dockerignore, next to this file, keeps local build output and local state (SQLite files, local settings)
# out of the build context.
# Run with configuration from the environment (never baked into the image), for example:
#   Product__PublicBaseUrl, Product__Auth__TenantId, Product__Auth__ClientId, Product__TelemetrySalt,
#   Product__Store__Provider=sqlserver and ConnectionStrings__Product (managed identity, no password).
# The development identity cannot run here: the environment is Production, and startup fails if it is switched on.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish adapters/azure/Product.Azure.Server/Product.Azure.Server.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Product.Azure.Server.dll"]
