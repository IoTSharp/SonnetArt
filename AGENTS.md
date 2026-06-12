# Agent Notes

## Docker Image Releases

- The Docker image workflow lives at `.github/workflows/docker.yml`.
- Pushing a semantic version tag builds an AOT Docker image and publishes it to GitHub Container Registry and Docker Hub.
- Publish targets are `ghcr.io/<owner>/sonnetart` and `<dockerhub-username>/sonnetart`.
- Docker Hub publishing requires `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` GitHub repository secrets.
- GHCR publishing uses the repository `GITHUB_TOKEN`; the workflow grants `packages: write`.
- Generated image tags include `v0.1.0`, `0.1.0`, `0.1`, and `latest`.

## Development Notes

- The target framework is `net10.0`.
- WebAssembly AOT is enabled by default through `RunAOTCompilation=true`.
- `external/AntDesignXBlazor` is a required submodule; local development and CI need recursive checkout.
- The Dockerfile runs `dotnet workload restore`, then publishes static assets with `RunAOTCompilation=true`.
- Runtime upstream URLs are injected with environment variables and are not baked into the image at build time.
- `bin/`, `obj/`, and `artifacts/` are generated outputs and should stay ignored.

## Documentation Maintenance

- `README.md` is the default English README.
- `README.zh.md` is the Chinese README.
- When changing project documentation, update both README files in the same change so the two language versions stay synchronized.
- Keep public README content focused on the project facade: product value, features, architecture, quick start, embedded usage, and license.

## Security

- Do not commit real API keys, JWT secrets, customer data, passwords, private keys, account tokens, or production tokens.
- Prefer short-lived tokens for embedded mode.
- Serve production deployments over HTTPS.
- Inject production configuration through environment variables or the container platform secret manager.
- Keep authentication, quota enforcement, metering, billing, and model routing in the upstream gateway instead of pushing sensitive logic into the static frontend.
