FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG SONNET_ART_PROJECT=src/SonnetArt/SonnetArt.csproj
ARG SONNET_HOST_PROJECT=src/SonnetHost/SonnetHost.csproj

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*

COPY . .
COPY external/SonnetDB /SonnetDB
RUN dotnet workload restore "$SONNET_ART_PROJECT"
RUN dotnet publish "$SONNET_ART_PROJECT" \
    -c Release \
    -f net10.0 \
    -o /out \
    /p:RunAOTCompilation=true
RUN dotnet publish "$SONNET_HOST_PROJECT" \
    -c Release \
    -f net10.0 \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

COPY --from=build /app/ ./
COPY --from=build /out/wwwroot/ ./wwwroot/

EXPOSE 8080

ENTRYPOINT ["dotnet", "SonnetHost.dll"]
