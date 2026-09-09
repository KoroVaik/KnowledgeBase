# AI worker image for the home PC. Build context is backend/.
# Not part of the prod (Render) deploy - see DEPLOYMENT.md.

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
# csproj first, so a source-only change keeps the restore layer cached.
COPY KnowledgeBase.Worker/KnowledgeBase.Worker.csproj KnowledgeBase.Worker/
COPY KnowledgeBase.Core/KnowledgeBase.Core.csproj KnowledgeBase.Core/
RUN dotnet restore KnowledgeBase.Worker/KnowledgeBase.Worker.csproj
COPY KnowledgeBase.Core/ KnowledgeBase.Core/
COPY KnowledgeBase.Worker/ KnowledgeBase.Worker/
RUN dotnet publish KnowledgeBase.Worker/KnowledgeBase.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
# No DOTNET_ENVIRONMENT -> Production: appsettings.json + env vars, no appsettings.Local.json.
ENTRYPOINT ["dotnet", "KnowledgeBase.Worker.dll"]
