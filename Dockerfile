FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY DevOrchestratorMcp.sln .
COPY src ./src

RUN dotnet restore src/DevOrchestrator.McpServer/DevOrchestrator.McpServer.csproj
RUN dotnet publish src/DevOrchestrator.McpServer/DevOrchestrator.McpServer.csproj     -c Release     -o /app/publish     --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DevOrchestrator.McpServer.dll"]
