# SonnetArt

[![Publish Docker Images](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml/badge.svg)](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml)

[English](README.md) | 中文

SonnetArt 是一个开源的 GPT Image 创作工作台，基于 Blazor WebAssembly 构建。它把文生图、图像编辑、变化生成、扩图、提示词润色、提示词库、作品管理和嵌入式启动整合在一个轻量的浏览器应用里，适合作为独立站点运行，也适合作为 sub2api 兼容平台中的内嵌页面。

## 项目能力

- GPT Image 创作工作台：支持文生图、图像编辑、变化生成和扩图工作流。
- 多参考图：支持上传参考图，并把生成结果继续作为参考图、编辑源图或变化种子使用。
- 提示词润色：把粗略创意整理成更适合图像模型执行的提示词。
- 提示词库：内置双语提示词素材，支持分类、搜索和一键填入。
- 工作区与会话：按任务组织提示词、对话、参数和生成历史。
- 作品图库：支持搜索、筛选、收藏、标签、参数复制、提示词复制、下载和再生成。
- 矩阵对比：适合批量比较提示词、模型、尺寸和参数组合。
- 本地状态保存：在浏览器中保存工作台状态和用户偏好。
- 嵌入模式：支持 `theme`、`lang`、`ui_mode`、`src_host`、`src_url` 等启动参数。
- sub2api 自动登录：支持通过 `token`、`access_token`、`auth_token`、`jwt`、`bearer_token` 等参数注入登录态。
- Docker 部署：镜像内使用 Caddy 提供静态资源，并代理账号接口与 OpenAI 兼容图像接口。

## 架构说明

SonnetArt 是前端优先的静态 Web 应用。使用 Docker 部署时，容器内的 Caddy 会承担静态资源服务和 API 反向代理：

- `/api/openai/*` 转发到 `SONNET_ART_AI_UPSTREAM_URL`，用于访问 OpenAI 兼容的图像模型网关。
- `/api/sonnet/*` 转发到 `SONNET_ART_ACCOUNT_UPSTREAM_URL`，并重写到 `/api/v1`，用于访问账号和 sub2api 兼容接口。

认证、配额、计量、模型路由等能力应由上游网关实现，SonnetArt 只负责开放的创作界面和浏览器端体验。

## 快速开始

环境要求：

- .NET SDK 10.0 或更高版本
- .NET WebAssembly workload
- 递归拉取 Git submodule

拉取代码并还原：

```bash
git clone --recursive https://github.com/iotsharp/SonnetArt.git
cd SonnetArt
dotnet workload restore src/SonnetArt/SonnetArt.csproj
dotnet restore src/SonnetArt/SonnetArt.csproj
```

本地运行：

```bash
dotnet run --project src/SonnetArt/SonnetArt.csproj
```

发布 AOT 静态构建：

```bash
dotnet publish src/SonnetArt/SonnetArt.csproj \
  -c Release \
  -f net10.0 \
  -p:RunAOTCompilation=true
```

## Docker 使用

构建镜像：

```bash
docker build -t sonnetart:local .
```

运行容器：

```bash
docker run --rm -p 8080:8080 \
  -e SONNET_ART_AI_UPSTREAM_URL=https://your-ai-gateway.example.com \
  -e SONNET_ART_ACCOUNT_UPSTREAM_URL=https://your-account-api.example.com \
  sonnetart:local
```

访问：

```text
http://localhost:8080
```

### 环境变量

| 变量 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `SONNET_ART_PUBLIC_ORIGIN` | 否 | `:8080` | Caddy 监听地址。容器内通常保持 `:8080`。 |
| `SONNET_ART_AI_UPSTREAM_URL` | 是 | - | OpenAI 兼容图像模型网关地址。 |
| `SONNET_ART_ACCOUNT_UPSTREAM_URL` | 是 | - | 账号 / sub2api 兼容 API 地址。 |

不要把 API Key、JWT Secret、账号 Token 或生产环境凭据写入镜像。生产部署应通过环境变量或容器平台的 Secret 注入运行时配置。

## 嵌入模式

SonnetArt 可以作为宿主系统中的嵌入页面启动：

```text
/?theme=light&lang=zh&ui_mode=embedded&src_host=https%3A%2F%2Fexample.com&src_url=https%3A%2F%2Fexample.com%2Fcustom%2Fpage
```

sub2api 兼容宿主可以在启动链接中带入登录凭据：

```text
/?user_id=1&token=<jwt>&ui_mode=embedded&theme=light&lang=zh
```

支持的凭据参数名：

- `token`
- `access_token`
- `auth_token`
- `jwt`
- `bearer_token`

应用完成账号初始化后，会从浏览器地址栏中清理这些凭据参数，减少 Token 暴露时间。

## Tag 发布 Docker 镜像

仓库已提供 GitHub Actions 工作流：`.github/workflows/docker.yml`。

推送语义化版本 Tag 后会自动构建 AOT Docker 镜像，并发布到 GitHub Container Registry 和 Docker Hub：

```bash
git tag v0.1.0
git push origin v0.1.0
```

发布目标：

- GitHub Container Registry：`ghcr.io/<owner>/sonnetart`
- Docker Hub：`<dockerhub-username>/sonnetart`

Docker Hub 发布需要在 GitHub 仓库 Secrets 中配置：

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

GHCR 使用仓库自带的 `GITHUB_TOKEN` 发布，工作流已配置 `packages: write` 权限。

镜像标签规则：

- `v0.1.0`
- `0.1.0`
- `0.1`
- `latest`

## 开发说明

- 项目目标框架为 `net10.0`。
- 默认启用 WebAssembly AOT：`RunAOTCompilation=true`。
- `external/AntDesignXBlazor` 是必需子模块，本地和 CI 都需要递归拉取。
- Dockerfile 会执行 `dotnet workload restore`，然后使用 `RunAOTCompilation=true` 发布静态资源。
- 运行时 upstream 地址由环境变量注入，不在镜像构建阶段固化。
- `bin/`、`obj/`、`artifacts/` 是构建产物，已被忽略。

## 文档维护

`README.md` 是默认英文版，`README.zh.md` 是中文版。修改项目文档时，两种语言版本需要在同一次变更中同步更新。

## 安全说明

- 不提交真实 API Key、JWT Secret、客户数据、密码、私钥或生产 Token。
- 嵌入模式建议使用短生命周期 Token。
- 生产环境必须通过 HTTPS 访问。
- 认证、配额、计量、计费和模型路由应放在上游网关，避免把敏感逻辑下沉到静态前端。

## License

MIT. See [LICENSE](LICENSE).
