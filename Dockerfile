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

FROM caddy:2-alpine
WORKDIR /srv

COPY --from=build /out/wwwroot/ /srv/
COPY deploy/Caddyfile.template /etc/caddy/Caddyfile.template
COPY deploy/docker-entrypoint.sh /usr/local/bin/sonnetart-entrypoint

RUN chmod +x /usr/local/bin/sonnetart-entrypoint

ENV SONNET_ART_PUBLIC_ORIGIN=:8080
EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/sonnetart-entrypoint"]
