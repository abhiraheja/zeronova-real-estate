# Build from the repository root (ZeroNovaPackages checked out next to this repo):
#   docker build --build-context zeronovapackages=../ZeroNovaPackages -f ZeroNovaRealEstate.Api/Dockerfile .
# Local:  docker compose up --build   (see docker-compose.yml — passes the context)
#
# ZeroNova's own packages (`ZeroNova_Packages.*`) are local project references to ../ZeroNovaPackages, not a feed.
# The csproj files point at `../../ZeroNovaPackages/...` from /src/<project>, so the `zeronovapackages` build context
# is copied to /ZeroNovaPackages. Everything else is public NuGet.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["nuget.config", "global.json", "Directory.Build.props", "./"]
COPY ["ZeroNovaRealEstate.Api/ZeroNovaRealEstate.Api.csproj", "ZeroNovaRealEstate.Api/"]
COPY ["ZeroNovaRealEstate.Infrastructure/ZeroNovaRealEstate.Infrastructure.csproj", "ZeroNovaRealEstate.Infrastructure/"]
COPY ["ZeroNovaRealEstate.PubSub/ZeroNovaRealEstate.PubSub.csproj", "ZeroNovaRealEstate.PubSub/"]
COPY ["ZeroNovaRealEstate.Application/ZeroNovaRealEstate.Application.csproj", "ZeroNovaRealEstate.Application/"]
COPY ["ZeroNovaRealEstate.Domain/ZeroNovaRealEstate.Domain.csproj", "ZeroNovaRealEstate.Domain/"]
COPY ["ZeroNovaRealEstate.Models/ZeroNovaRealEstate.Models.csproj", "ZeroNovaRealEstate.Models/"]
COPY --from=zeronovapackages . /ZeroNovaPackages/
RUN dotnet restore "ZeroNovaRealEstate.Api/ZeroNovaRealEstate.Api.csproj"
COPY . .
RUN dotnet publish "ZeroNovaRealEstate.Api/ZeroNovaRealEstate.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ZeroNovaRealEstate.Api.dll"]
