# 🎨 SonnetArt

[![Publish Docker Images](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml/badge.svg)](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml)
![Docker Pulls](https://img.shields.io/docker/pulls/iotsharp/sonnetart?label=Docker%20Pulls)
[![GHCR](https://img.shields.io/badge/GHCR-ghcr.io%2Fiotsharp%2Fsonnetart-24292f?logo=github)](https://github.com/iotsharp/SonnetArt/pkgs/container/sonnetart)
[![Docker Hub](https://img.shields.io/badge/Docker%20Hub-iotsharp%2Fsonnetart-2496ed?logo=docker&logoColor=white)](https://hub.docker.com/r/iotsharp/sonnetart)

English | [中文](README.zh.md)

🎨 SonnetArt is an open-source GPT Image studio built with Blazor WebAssembly. It brings text-to-image generation, image editing, variations, outpainting, prompt polishing, prompt libraries, gallery management, and embedded launch flows into a lightweight browser application. 🚀 It can run as a standalone site, as a Docker service, or as an embedded page inside a sub2api-compatible host.

## ✨ Features

- 🖼️ GPT Image studio for text-to-image, image editing, image variations, and outpainting workflows.
- 🧩 Multi-image references, allowing generated images to be reused as references, edit sources, or variation seeds.
- ✨ Prompt polishing to turn rough creative ideas into model-ready image prompts.
- 📚 Prompt library with bilingual prompt materials, categories, search, and quick insertion.
- 🗂️ Workspaces and sessions for organizing prompts, conversations, parameters, and generation history by task.
- 🖼️ Gallery view with search, filters, favorites, tags, parameter copy, prompt copy, download, and regeneration actions.
- 🧪 Matrix comparison workflows for exploring prompt, model, size, and parameter combinations.
- 💾 Local browser persistence for studio state and user preferences.
- 🔗 Embedded mode with launch parameters such as `theme`, `lang`, `ui_mode`, `src_host`, and `src_url`.
- 🔐 sub2api auto-login through launch parameters such as `token`, `access_token`, `auth_token`, `jwt`, and `bearer_token`.
- 🐳 Docker deployment with Caddy serving static assets and proxying account and OpenAI-compatible image APIs.

## 🧭 Architecture

SonnetArt is a frontend-first static web application. When deployed with Docker, the bundled Caddy server serves static assets and reverse proxies API traffic:

- `/api/openai/*` proxies to `SONNET_ART_AI_UPSTREAM_URL` for an OpenAI-compatible image model gateway.
- `/api/sonnet/*` proxies to `SONNET_ART_ACCOUNT_UPSTREAM_URL` and rewrites requests under `/api/v1` for account and sub2api-compatible APIs.

Authentication, quota enforcement, metering, billing, and model routing should live in the upstream gateway. SonnetArt focuses on the open creative interface and browser experience.

## 🚀 Quick Start

Create a `docker-compose.yml` and deploy SonnetArt together with a sub2api-compatible gateway:

```yaml
services:
  sonnet-art:
    image: ghcr.io/iotsharp/sonnetart:latest
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      SONNET_ART_PUBLIC_ORIGIN: :8080
      SONNET_ART_AI_UPSTREAM_URL: http://sub2api:8080
      SONNET_ART_ACCOUNT_UPSTREAM_URL: http://sub2api:8080
    depends_on:
      - sub2api

  sub2api:
    image: weishaw/sub2api:latest
    restart: unless-stopped
    expose:
      - "8080"
    environment:
      SERVER_HOST: 0.0.0.0
      SERVER_PORT: 8080
      BASE_URL: https://your-domain.example.com
    volumes:
      - ./data/sub2api:/app/data
```

### ⚙️ Environment Variables

| Variable | Required | Default | Description |
| --- | --- | --- | --- |
| `SONNET_ART_PUBLIC_ORIGIN` | No | `:8080` | Caddy listen address. Containers usually keep `:8080`. |
| `SONNET_ART_AI_UPSTREAM_URL` | Yes | - | OpenAI-compatible image model gateway URL. |
| `SONNET_ART_ACCOUNT_UPSTREAM_URL` | Yes | - | Account / sub2api-compatible API URL. |

## 🔗 Embedded Mode

SonnetArt can be launched as an embedded page from a host system:

```text
/?theme=light&lang=zh&ui_mode=embedded&src_host=https%3A%2F%2Fexample.com&src_url=https%3A%2F%2Fexample.com%2Fcustom%2Fpage
```

A sub2api-compatible host can pass login credentials in the launch URL:

```text
/?user_id=1&token=<jwt>&ui_mode=embedded&theme=light&lang=zh
```

Supported credential parameter names:

- `token`
- `access_token`
- `auth_token`
- `jwt`
- `bearer_token`

After account initialization completes, the application removes these credential parameters from the browser address bar to reduce token exposure time.

## 📄 License

MIT. See [LICENSE](LICENSE).
