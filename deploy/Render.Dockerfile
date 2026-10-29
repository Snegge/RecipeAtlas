FROM node:24.19.0-bookworm-slim AS frontend
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS backend
WORKDIR /src
COPY backend/backend/ ./backend/
RUN dotnet restore backend/RecipeAtlas.Api/Recipoke.Api.csproj --locked-mode
RUN dotnet publish backend/RecipeAtlas.Api/Recipoke.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
COPY --from=frontend /usr/local/bin/node /usr/local/bin/node
COPY backend/tools/requirements-yt-dlp.txt /tmp/requirements-yt-dlp.txt
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv ca-certificates libstdc++6 \
    && python3 -m venv /opt/yt-dlp \
    && /opt/yt-dlp/bin/python -m pip install --no-cache-dir -r /tmp/requirements-yt-dlp.txt \
    && rm -rf /var/lib/apt/lists/* /tmp/requirements-yt-dlp.txt
WORKDIR /app
COPY --from=backend /out/ ./
COPY --from=frontend /src/frontend/dist/recipoke/ ./wwwroot/
ARG BACKEND_REVISION
ARG FRONTEND_REVISION
ENV Release__Backend=$BACKEND_REVISION Release__Frontend=$FRONTEND_REVISION
LABEL org.opencontainers.image.revision=$BACKEND_REVISION io.recipoke.frontend.revision=$FRONTEND_REVISION
ENV ASPNETCORE_ENVIRONMENT=Production Database__Provider=PostgreSql PORT=10000 SocialImport__YtDlpPath=/opt/yt-dlp/bin/yt-dlp SocialImport__NodePath=/usr/local/bin/node
USER app
EXPOSE 10000
ENTRYPOINT ["dotnet", "Recipoke.Api.dll"]
