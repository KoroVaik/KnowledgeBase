# Frontend and backend build in parallel stages, then meet in the runtime image: ASP.NET
# serves the SPA, so the browser stays on one origin and there is no CORS. Only
# KnowledgeBase.Api ships here - the worker runs on the home PC (docs/infra.md).

FROM node:24-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS backend
WORKDIR /src/backend
# csproj first, so a source-only change does not bust the restore layer.
COPY backend/KnowledgeBase.Api/KnowledgeBase.Api.csproj KnowledgeBase.Api/
COPY backend/KnowledgeBase.Core/KnowledgeBase.Core.csproj KnowledgeBase.Core/
RUN dotnet restore KnowledgeBase.Api/KnowledgeBase.Api.csproj
COPY backend/ ./
RUN dotnet publish KnowledgeBase.Api/KnowledgeBase.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
COPY --from=backend /app/publish ./
COPY --from=frontend /src/frontend/dist ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "KnowledgeBase.Api.dll"]
