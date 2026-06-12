# SonnetArt

[![Publish Docker Images](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml/badge.svg)](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml)

English | [中文](README.zh.md)

SonnetArt is an open-source GPT Image studio built with Blazor WebAssembly. It brings text-to-image generation, image editing, variations, outpainting, prompt polishing, prompt libraries, gallery management, and embedded launch flows into a lightweight browser application. It can run as a standalone site, as a Docker service, or as an embedded page inside a sub2api-compatible host.

## Features

- GPT Image studio for text-to-image, image editing, image variations, and outpainting workflows.
- Multi-image references, allowing generated images to be reused as references, edit sources, or variation seeds.
- Prompt polishing to turn rough creative ideas into model-ready image prompts.
- Prompt library with bilingual prompt materials, categories, search, and quick insertion.
- Workspaces and sessions for organizing prompts, conversations, parameters, and generation history by task.
- Gallery view with search, filters, favorites, tags, parameter copy, prompt copy, download, and regeneration actions.
- Matrix comparison workflows for exploring prompt, model, size, and parameter combinations.
- Local browser persistence for studio state and user preferences.
- Embedded mode with launch parameters such as `theme`, `lang`, `ui_mode`, `src_host`, and `src_url`.
- sub2api auto-login through launch parameters such as `token`, `access_token`, `auth_token`, `jwt`, and `bearer_token`.
- Docker deployment with Caddy serving static assets and proxying account and OpenAI-compatible image APIs.

## Architecture

SonnetArt is a frontend-first static web application. When deployed with Docker, the bundled Caddy server serves static assets and reverse proxies API traffic:

- `/api/openai/*` proxies to `SONNET_ART_AI_UPSTREAM_URL` for an OpenAI-compatible image model gateway.
- `/api/sonnet/*` proxies to `SONNET_ART_ACCOUNT_UPSTREAM_URL` and rewrites requests under `/api/v1` for account and sub2api-compatible APIs.

Authentication, quota enforcement, metering, billing, and model routing should live in the upstream gateway. SonnetArt focuses on the open creative interface and browser experience.

## Quick Start

Requirements:

- .NET SDK 10.0 or later
- .NET WebAssembly workload
- Git submodules checked out recursively

Clone and restore:

```bash
git clone --recursive https://github.com/iotsharp/SonnetArt.git
cd SonnetArt
dotnet workload restore src/SonnetArt/SonnetArt.csproj
dotnet restore src/SonnetArt/SonnetArt.csproj
```

Run locally:

```bash
dotnet run --project src/SonnetArt/SonnetArt.csproj
```

Publish an AOT static build:

```bash
dotnet publish src/SonnetArt/SonnetArt.csproj \
  -c Release \
  -f net10.0 \
  -p:RunAOTCompilation=true
```

## Docker

Build:

```bash
docker build -t sonnetart:local .
```

Run:

```bash
docker run --rm -p 8080:8080 \
  -e SONNET_ART_AI_UPSTREAM_URL=https://your-ai-gateway.example.com \
  -e SONNET_ART_ACCOUNT_UPSTREAM_URL=https://your-account-api.example.com \
  sonnetart:local
```

Open:

```text
http://localhost:8080
```

### Environment Variables

| Variable | Required | Default | Description |
| --- | --- | --- | --- |
| `SONNET_ART_PUBLIC_ORIGIN` | No | `:8080` | Caddy listen address. Containers usually keep `:8080`. |
| `SONNET_ART_AI_UPSTREAM_URL` | Yes | - | OpenAI-compatible image model gateway URL. |
| `SONNET_ART_ACCOUNT_UPSTREAM_URL` | Yes | - | Account / sub2api-compatible API URL. |

Do not bake API keys, JWT secrets, account tokens, or production credentials into the image. Inject production configuration through environment variables or your container platform secret manager.

## Embedded Mode

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

## Docker Image Releases

This repository includes a GitHub Actions workflow at `.github/workflows/docker.yml`.

Pushing a semantic version tag builds an AOT Docker image and publishes it to GitHub Container Registry and Docker Hub:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Publish targets:

- GitHub Container Registry: `ghcr.io/<owner>/sonnetart`
- Docker Hub: `<dockerhub-username>/sonnetart`

Docker Hub publishing requires these GitHub repository secrets:

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

GHCR publishing uses the repository `GITHUB_TOKEN`; the workflow grants `packages: write`.

Generated image tags:

- `v0.1.0`
- `0.1.0`
- `0.1`
- `latest`

## Development Notes

- The target framework is `net10.0`.
- WebAssembly AOT is enabled by default through `RunAOTCompilation=true`.
- `external/AntDesignXBlazor` is a required submodule; both local development and CI need recursive checkout.
- The Dockerfile runs `dotnet workload restore`, then publishes static assets with `RunAOTCompilation=true`.
- Runtime upstream URLs are injected with environment variables and are not baked into the image at build time.
- `bin/`, `obj/`, and `artifacts/` are generated outputs and are ignored.

## Documentation Maintenance

`README.md` is the default English README. `README.zh.md` is the Chinese version. When changing project documentation, update both files in the same change so the two language versions stay synchronized.

## Security

- Do not commit real API keys, JWT secrets, customer data, passwords, private keys, or production tokens.
- Prefer short-lived tokens for embedded mode.
- Serve production deployments over HTTPS.
- Keep authentication, quota enforcement, metering, billing, and model routing in the upstream gateway instead of pushing sensitive logic into the static frontend.

## License

MIT. See [LICENSE](LICENSE).
