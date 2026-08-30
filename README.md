# Cuddns

A small, self-hosted Dynamic DNS updater. It runs on a schedule, checks your
public IP, and updates DNS records through a pluggable provider (Route53,
DuckDNS, Cloudflare, and No-IP today) — only when the IP actually changed,
tracked via a local cache so it doesn't hammer your DNS provider's API every
run.
Built to replace a pile of personal `update-r53` bash scripts with something
configurable, testable, and easy to run in a homelab.

> [!NOTE]
> This project was vibe coded — designed and built almost entirely through
> conversation with an AI assistant (Claude). It's been reviewed and tested,
> but review the code yourself before trusting it with your DNS.

## Features

- **Scheduled updates** — configurable interval, no external cron needed.
- **IP caching** — skips the update (and the API call) when the public IP
  hasn't changed since last run.
- **Plugin-style providers** — ships with AWS Route53, DuckDNS, Cloudflare,
  and No-IP; more can be added without touching the core update loop.
- **YAML config with secret substitution** — `${VAR_NAME}` placeholders
  resolved from the environment or a `.env` file, so credentials never live
  in the config file itself.
- **Docker image** — small Alpine-based image, published to GHCR for
  `linux/amd64` and `linux/arm64`.

## Quick start

1. Copy the example config and env files:

   ```bash
   cp config/config.example.yaml config/config.yaml
   cp config/.env.example config/.env
   ```

2. Edit `config/config.yaml` and `config/.env` for your domains and
   credentials (see below).

3. Run it:

   ```bash
   docker run -d \
     --name cuddns \
     -v "$(pwd)/config:/config:ro" \
     -v cuddns-data:/data \
     ghcr.io/sesser/cuddns:latest
   ```

   `latest` is published once the first `vX.Y.Z` release tag is pushed; until
   then, use `ghcr.io/sesser/cuddns:edge` for the latest build off `develop`.

   `/config` holds your `config.yaml` and `.env`; `/data` persists the IP
   cache across restarts.

## Example `docker-compose.yml`

Same setup as the `docker run` command above, if you'd rather manage it
declaratively:

```yaml
services:
  cuddns:
    image: ghcr.io/sesser/cuddns:latest
    container_name: cuddns
    restart: unless-stopped
    volumes:
      - ./config:/config:ro
      - cuddns-data:/data

volumes:
  cuddns-data:
```

```bash
docker compose up -d
```

## Example `config.yaml`

```yaml
intervalSeconds: 300

# Optional: publicIpSources: [ifconfig, ipify, icanhazip, identme]  (default order shown)

providers:
  - type: route53
    accessKeyId: ${AWS_ACCESS_KEY_ID}
    secretAccessKey: ${AWS_SECRET_ACCESS_KEY}
    region: us-east-1
    zones:
      - hostedZoneId: Z0123456789ABCDEFGHIJ
        ttl: 300
        records:
          - example.com
          - www.example.com
          - vpn.example.com

  - type: duckdns
    token: ${DUCKDNS_TOKEN}
    records:
      - home.duckdns.org

  - type: cloudflare
    apiToken: ${CLOUDFLARE_API_TOKEN}
    zones:
      - zoneId: 023e105f4ecef8ad9ca31a8372d0c353
        ttl: 300
        proxied: false
        records:
          - home.example.com
          - vpn.example.com

  - type: noip
    username: ${NOIP_USERNAME}
    password: ${NOIP_PASSWORD}
    records:
      - home.example.com
```

- `intervalSeconds` — how often to check the public IP and update records.
- `publicIpSources` — optional; which public-IP lookup sources to try, in
  order (the first that answers wins, resolved independently for IPv4 and
  IPv6). Defaults to `[ifconfig, ipify, icanhazip, identme]` if omitted.
  `ifconfig` (ifconfig.net) only ever answers for IPv4 — it has no way to
  pin the address family — so IPv6 detection relies on the others, which
  each expose separate v4/v6 endpoints. A host with no IPv6 connectivity
  just gets `null` back for IPv6 and any `AAAA` records are skipped that
  run rather than failing the whole update.
- `providers[].type` — which provider implementation to use (`route53`,
  `duckdns`, `cloudflare`, or `noip`).

**route53**
- `accessKeyId` / `secretAccessKey` — AWS credentials; use `${VAR}`
  placeholders, never commit real values.
- `zones[].hostedZoneId` — the Route53 hosted zone ID.
- `zones[].ttl` — TTL applied to updated records.
- `zones[].records` — the A records to keep pointed at your current public IP.

**duckdns**
- `token` — your DuckDNS account token; use a `${VAR}` placeholder.
- `records` — one or more `*.duckdns.org` hostnames to keep updated. TTL
  isn't configurable — DuckDNS manages it itself.

**cloudflare**
- `apiToken` — an API token scoped to `Zone:DNS:Edit` for the target
  zone(s); use a `${VAR}` placeholder. Create one under
  My Profile → API Tokens on the Cloudflare dashboard.
- `zones[].zoneId` — the Cloudflare zone ID (found on the zone's Overview page).
- `zones[].ttl` — TTL applied to updated records; use `1` for Cloudflare's
  "Auto" TTL.
- `zones[].proxied` — whether records are proxied through Cloudflare (orange
  cloud) rather than DNS-only. Defaults to `false`.
- `zones[].records` — the A records to keep pointed at your current public IP.

**noip**
- `username` / `password` — your No-IP account credentials; use `${VAR}`
  placeholders.
- `records` — one or more hostnames configured under your No-IP account
  (not restricted to a No-IP-owned domain). TTL isn't configurable via the
  update API — No-IP manages it (paid plans can change it from their
  dashboard).

## Example `.env`

```dotenv
AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
DUCKDNS_TOKEN=00000000-0000-0000-0000-000000000000
CLOUDFLARE_API_TOKEN=your-cloudflare-api-token
NOIP_USERNAME=your-noip-username
NOIP_PASSWORD=your-noip-password
```

Any `${VAR_NAME}` in `config.yaml` is resolved from this file first, then
falls back to the container's real environment. Missing variables fail
startup immediately with a clear error rather than updating DNS with a
blank value.

## Configuration paths

Override these via environment variables if the defaults don't fit your setup:

| Variable             | Default                | Purpose                          |
| -------------------- | ----------------------- | --------------------------------- |
| `CUDDNS_CONFIG_PATH` | `/config/config.yaml`  | Path to the YAML config file      |
| `CUDDNS_ENV_PATH`    | `/config/.env`         | Path to the optional `.env` file  |
| `CUDDNS_CACHE_PATH`  | `/data/cache.json`     | Path to the IP cache file         |

The container runs as a non-root user (uid/gid `1000`). If you're reusing a
`/data` volume from before this was fixed and see `UnauthorizedAccessException`
/ `Permission denied` writing `cache.json`, fix its ownership once:

```bash
docker run --rm -v cuddns-data:/data alpine chown -R 1000:1000 /data
```

(swap `cuddns-data` for your actual volume/mount).

## Development

```bash
dotnet build
dotnet test
docker build -t cuddns .
```

Versioning and image publishing are handled by
[`.github/workflows/docker-publish.yml`](.github/workflows/docker-publish.yml) —
push a `vX.Y.Z` tag to cut a release; see that file's header comment for the
full tagging scheme.

## License

[MIT](LICENSE)
