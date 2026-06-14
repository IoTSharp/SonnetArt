FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*

COPY . .
RUN dotnet workload restore src/SonnetArt/SonnetArt.csproj
RUN dotnet publish src/SonnetArt/SonnetArt.csproj \
    -c Release \
    -f net10.0 \
    -o /out \
    /p:RunAOTCompilation=true
RUN dotnet publish src/SonnetHost/SonnetHost.csproj \
    -c Release \
    -f net10.0 \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

COPY --from=build /app/ ./
COPY --from=build /out/wwwroot/ ./wwwroot/

ENV SONNET_ART_PUBLIC_ORIGIN=:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SonnetHost.dll"]
