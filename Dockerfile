FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Cuddns/ src/Cuddns/
RUN dotnet publish src/Cuddns/Cuddns.csproj -c Release -o /app /p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
ARG VERSION=0.0.0-dev
WORKDIR /app
RUN addgroup -g 1000 -S cuddns && \
    adduser -u 1000 -S cuddns -G cuddns && \
    mkdir -p /data /config && \
    chown -R cuddns:cuddns /data /config
COPY --chown=cuddns:cuddns --from=build /app .
VOLUME ["/config", "/data"]
ENV CUDDNS_CONFIG_PATH=/config/config.yaml
ENV CUDDNS_ENV_PATH=/config/.env
ENV CUDDNS_CACHE_PATH=/data/cache.json
ENV CUDDNS_VERSION=$VERSION
USER cuddns
ENTRYPOINT ["dotnet", "Cuddns.dll"]
