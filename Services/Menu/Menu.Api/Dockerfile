# =====================================================================
# SmakoViz · Menu API — образ контейнера (multi-stage).
# Збирати з КОРЕНЯ репозиторію (там Directory.Build.props):
#   docker build -f Services/Menu/Menu.Api/Dockerfile -t smakoviz/menu-api .
#   docker run -p 8080:8080 -e ConnectionStrings__MenuDb="Server=host.docker.internal,1433;..." smakoviz/menu-api
# =====================================================================

# ---------- 1. Збірка ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# спершу лише файли проєктів → restore кешується, поки не змінились залежності
COPY Directory.Build.props ./
COPY Platform.ServiceDefaults/Platform.ServiceDefaults.csproj Platform.ServiceDefaults/
COPY Platform.EventBus/Platform.EventBus.csproj Platform.EventBus/
COPY Services/Menu/Menu.Domain/Menu.Domain.csproj Services/Menu/Menu.Domain/
COPY Services/Menu/Menu.Dal/Menu.Dal.csproj       Services/Menu/Menu.Dal/
COPY Services/Menu/Menu.Bll/Menu.Bll.csproj       Services/Menu/Menu.Bll/
COPY Services/Menu/Menu.Api/Menu.Api.csproj       Services/Menu/Menu.Api/
RUN dotnet restore Services/Menu/Menu.Api/Menu.Api.csproj

COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.EventBus/ Platform.EventBus/
COPY Services/Menu/ Services/Menu/
RUN dotnet publish Services/Menu/Menu.Api/Menu.Api.csproj -c Release -o /app/publish --no-restore

# ---------- 2. Рантайм (без SDK — менший образ) ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .
# не root-користувач (є в офіційних образах .NET 8+)
USER app
ENTRYPOINT ["dotnet", "Menu.Api.dll"]
