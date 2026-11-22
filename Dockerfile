# TeamBoard: one image, one process. The API serves the built Angular app, so there is no CORS and one thing to deploy.
#
#   docker build -t teamboard .
#
# Needs at run time (see docs/DEPLOY.md): ConnectionStrings__DefaultConnection and Jwt__Key.
# Migrations are a separate step: `docker run ... teamboard --migrate` applies them and exits.

# 1. The Angular app
FROM node:22-alpine AS client
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

# 2. The API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY TeamBoard.API/TeamBoard.API.csproj TeamBoard.API/
RUN dotnet restore TeamBoard.API/TeamBoard.API.csproj
COPY TeamBoard.API/ TeamBoard.API/
RUN dotnet publish TeamBoard.API/TeamBoard.API.csproj --configuration Release --no-restore --output /app

# 3. What actually runs: the runtime only, as a non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /app ./
COPY --from=client /src/client/dist/client/browser ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
# Behind the platform's proxy (which ends TLS) the app needs to trust its forwarded headers; see docs/DEPLOY.md.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TeamBoard.API.dll"]
