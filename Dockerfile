FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EgoNetRevival.sln ./
COPY src/RaceNetShowdown.Server/RaceNetShowdown.Server.csproj src/RaceNetShowdown.Server/
COPY src/RaceNetShowdown.Patcher/RaceNetShowdown.Patcher.csproj src/RaceNetShowdown.Patcher/
COPY src/RaceNetShowdown.TlsProbe/RaceNetShowdown.TlsProbe.csproj src/RaceNetShowdown.TlsProbe/
RUN dotnet restore EgoNetRevival.sln

COPY . .
RUN dotnet publish src/RaceNetShowdown.Server/RaceNetShowdown.Server.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends openssl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "RaceNetShowdown.Server.dll"]
