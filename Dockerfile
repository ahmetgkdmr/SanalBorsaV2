# SanalBorsa API — multi-stage (.NET 8) — Render.com
# Repo kökü = bu klasör (SanalBorsa.sln burada)

# ─── restore ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

COPY SanalBorsa.sln ./
COPY SanalBorsa/SanalBorsa.csproj SanalBorsa/
COPY SanalBorsa.Application/SanalBorsa.Application.csproj SanalBorsa.Application/
COPY SanalBorsa.Domain/SanalBorsa.Domain.csproj SanalBorsa.Domain/
COPY SanalBorsa.Infrastructure/SanalBorsa.Infrastructure.csproj SanalBorsa.Infrastructure/

RUN dotnet restore SanalBorsa/SanalBorsa.csproj

# ─── build ──────────────────────────────────────────────────────────────────
FROM restore AS build
WORKDIR /src
COPY . .
RUN dotnet build SanalBorsa/SanalBorsa.csproj -c Release --no-restore

# ─── publish ────────────────────────────────────────────────────────────────
FROM build AS publish
WORKDIR /src
RUN dotnet publish SanalBorsa/SanalBorsa.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ─── runtime ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "SanalBorsa.dll"]
