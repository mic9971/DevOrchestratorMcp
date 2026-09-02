FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY DevOrchestratorMcp.sln .
COPY src ./src

RUN dotnet restore src/DevOrchestrator.McpServer/DevOrchestrator.McpServer.csproj
RUN dotnet publish src/DevOrchestrator.McpServer/DevOrchestrator.McpServer.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    && chown -R app:app /app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl --fail --silent http://127.0.0.1:8080/healthz >/dev/null || exit 1

ENTRYPOINT ["dotnet", "DevOrchestrator.McpServer.dll"]
