# Frontend and backend build in parallel stages, then meet in the runtime image:
# ASP.NET serves the SPA itself, so the browser stays on one origin and there is no CORS.

FROM node:24-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS backend
WORKDIR /src/backend
COPY backend/Backend.csproj ./
RUN dotnet restore Backend.csproj
COPY backend/ ./
RUN dotnet publish Backend.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
COPY --from=backend /app/publish ./
COPY --from=frontend /src/frontend/dist ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Backend.dll"]
