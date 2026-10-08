FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/RecipeAtlas.Api/RecipeAtlas.Api.csproj backend/RecipeAtlas.Api/
RUN dotnet restore backend/RecipeAtlas.Api/RecipeAtlas.Api.csproj
COPY backend/RecipeAtlas.Api/ backend/RecipeAtlas.Api/
RUN dotnet publish backend/RecipeAtlas.Api/RecipeAtlas.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DataDirectory=/app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "RecipeAtlas.Api.dll"]
