FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/RecipeAtlas.Api/RecipeAtlas.Api.csproj backend/RecipeAtlas.Api/
RUN dotnet restore backend/RecipeAtlas.Api/RecipeAtlas.Api.csproj
COPY backend/RecipeAtlas.Api/ backend/RecipeAtlas.Api/
RUN dotnet publish backend/RecipeAtlas.Api/RecipeAtlas.Api.csproj -c Release --no-restore -o /out

FROM node:24.19.0-bookworm-slim AS javascript

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
COPY --from=javascript /usr/local/bin/node /usr/local/bin/node
COPY tools/requirements-yt-dlp.txt /tmp/requirements-yt-dlp.txt
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv ca-certificates libstdc++6 \
    && python3 -m venv /opt/yt-dlp \
    && /opt/yt-dlp/bin/python -m pip install --no-cache-dir -r /tmp/requirements-yt-dlp.txt \
    && rm -rf /var/lib/apt/lists/* /tmp/requirements-yt-dlp.txt
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DataDirectory=/app/data
ENV SocialImport__YtDlpPath=/opt/yt-dlp/bin/yt-dlp
ENV SocialImport__NodePath=/usr/local/bin/node
EXPOSE 8080
ENTRYPOINT ["dotnet", "RecipeAtlas.Api.dll"]
