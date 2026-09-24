# Deploying Nebula API (Windows server, Docker Desktop)

Target: `https://api.antrias.site` → existing reverse proxy (80/443) → container on `127.0.0.1:8080`.
Run all commands in PowerShell on the server unless stated otherwise.

## 1. Prerequisites (once)

1. Install Docker Desktop with the WSL 2 backend (`wsl --install` first if WSL is missing, then reboot).
2. Docker Desktop → Settings → General: enable **Start Docker Desktop when you sign in**.
   Docker Desktop needs a user session: after a reboot the API comes back only once that user
   signs in (configure Windows auto-logon if the server must recover unattended).
3. Check: `docker version` shows a Linux server engine.

## 2. Get the deployment files

Only `compose.yaml` and `.env.example` are needed on the server.

```powershell
New-Item -ItemType Directory -Force C:\srv\nebula-api | Out-Null
Set-Location C:\srv\nebula-api
# Either clone the repo ...
git clone https://github.com/antryas/nebula-api.git .
# ... or copy compose.yaml and .env.example into this folder by hand.
```

## 3. Create `.env` with a fresh signing key

```powershell
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
$key = [Convert]::ToBase64String($b)
$content = (Get-Content .env.example -Raw) -replace '(?m)^Jwt__SigningKey=.*$', "Jwt__SigningKey=$key"
[IO.File]::WriteAllText("$PWD\.env", $content)   # UTF-8 without BOM
```

`.env` also sets `ForwardedHeaders__KnownProxies__0=172.30.80.1` (see step 8). Keep the file only on the server.

## 4. Pull and start

```powershell
docker compose pull
docker compose up -d --no-build
docker compose ps
docker compose logs --tail 20 api     # expect "Database created and seeded" and "Application started"
curl.exe http://127.0.0.1:8080/health # {"status":"Healthy"}
```

If `pull` is denied, the GHCR package is still private: make it public in GitHub → Packages →
nebula-api → Package settings, or run `docker login ghcr.io` with a PAT that has `read:packages`.

## 5. DNS

At the registrar add an **A** record: host `api`, value = the server's static IP, TTL 300–3600.
Check from any machine: `Resolve-DnsName api.antrias.site`.

## 6. Reverse proxy site

- **Caddy:** append `deploy/Caddyfile.snippet` to the Caddyfile and run `caddy reload --config <Caddyfile>`.
  TLS is automatic.
- **nginx:** add `deploy/nginx.conf.snippet` to the config, issue a certificate for `api.antrias.site`
  with the ACME client already in use (win-acme or certbot), fix the certificate paths,
  then `nginx -t` and `nginx -s reload`.

Both forward to `http://127.0.0.1:8080` and pass `X-Forwarded-For` / `X-Forwarded-Proto`.

## 7. Verify from outside

```powershell
curl.exe -s https://api.antrias.site/health
curl.exe -s -o NUL -w "%{http_code}`n" https://api.antrias.site/swagger/index.html
curl.exe -si -X OPTIONS https://api.antrias.site/api/auth/login `
  -H "Origin: https://antryas.github.io" -H "Access-Control-Request-Method: POST" |
  Select-String "HTTP/|Access-Control-Allow-Origin"
curl.exe -s -m 5 http://<server-ip>:8080/health   # must FAIL: 8080 is loopback-only
```

## 8. Forwarded headers (client IPs)

On Docker Desktop, requests from a proxy running on the Windows host reach the container from
the compose network gateway. `compose.yaml` pins that network to `172.30.80.0/24`, so the gateway
is always `172.30.80.1`, and `.env` trusts exactly that address. Without it every visitor shares one
rate-limit bucket (the gateway IP).

If the proxy itself runs in a Docker container, point it at `host.docker.internal:8080` instead and
tell the developer: the trusted proxy address changes.

## 9. Update

```powershell
Set-Location C:\srv\nebula-api
docker compose pull
docker compose up -d --no-build
docker image prune -f
```

The database is re-seeded on every start (and every 6 h), so updates lose nothing.

## 10. Rollback

Images are tagged `latest` and `sha-<7 chars>` (see GitHub → Packages → nebula-api).

```powershell
Add-Content .env "NEBULA_TAG=sha-1a2b3c4"   # previous good tag
docker compose up -d --no-build
```

Remove the `NEBULA_TAG` line to return to `latest`.

## Security

- **Loopback only:** the container port is published on `127.0.0.1:8080`; the reverse proxy is the
  only public entry point.
- **No new inbound ports:** nothing beyond the existing 80/443; do not add firewall rules for 8080.
- **Read-only root filesystem:** only `/tmp` (tmpfs, 64 MB) is writable; it holds the SQLite file.
- **Non-root, minimal image:** chiseled Ubuntu image, no shell or package manager, runs as UID 1654,
  all Linux capabilities dropped, `no-new-privileges`, memory and PID limits.
- **Secrets:** the JWT signing key lives only in `.env` on the server (git-ignored, never in the image
  or the repo). The app refuses to start without a key of at least 32 bytes. Rotating the key
  (edit `.env`, `docker compose up -d`) signs everyone out.
