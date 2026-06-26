# 🎨 SonnetArt

[![Publish Docker Images](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml/badge.svg)](https://github.com/iotsharp/SonnetArt/actions/workflows/docker.yml)
![Docker Pulls](https://img.shields.io/docker/pulls/iotsharp/sonnetart?label=Docker%20%E4%B8%8B%E8%BD%BD%E6%AC%A1%E6%95%B0)
[![GHCR](https://img.shields.io/badge/GHCR-ghcr.io%2Fiotsharp%2Fsonnetart-24292f?logo=github)](https://github.com/iotsharp/SonnetArt/pkgs/container/sonnetart)
[![Docker Hub](https://img.shields.io/badge/Docker%20Hub-iotsharp%2Fsonnetart-2496ed?logo=docker&logoColor=white)](https://hub.docker.com/r/iotsharp/sonnetart)

[English](README.md) | 中文

🎨 SonnetArt 是一个开源的 GPT Image 创作工作台，基于 Blazor WebAssembly 构建。它把文生图、图像编辑、变化生成、扩图、提示词润色、提示词库、作品管理和嵌入式启动整合在一个轻量的浏览器应用里。🚀 它适合作为独立站点运行，也适合作为 sub2api 兼容平台中的内嵌页面。

## ✨ 项目能力

- 🖼️ GPT Image 创作工作台：支持文生图、图像编辑、变化生成和扩图工作流。
- 🧩 多参考图：支持上传参考图，并把生成结果继续作为参考图、编辑源图或变化种子使用。
- ✨ 提示词润色：把粗略创意整理成更适合图像模型执行的提示词。
- 📚 提示词库：内置双语提示词素材，支持分类、搜索和一键填入。
- 🗂️ 工作区与会话：按任务组织提示词、对话、参数和生成历史。
- 🖼️ 作品图库：支持搜索、筛选、收藏、标签、参数复制、提示词复制、下载和再生成。
- 🧪 矩阵对比：适合批量比较提示词、模型、尺寸和参数组合。
- 💾 服务端状态保存：在服务器中保存工作台状态、用户偏好、会话和生成历史。
- 🔗 嵌入模式：支持 `theme`、`lang`、`ui_mode`、`src_host`、`src_url` 等启动参数。
- 🔐 sub2api 自动登录：支持通过 `token`、`access_token`、`auth_token`、`jwt`、`bearer_token` 等参数注入登录态。
- 🐳 Docker 部署：镜像内使用 ASP.NET Core SonnetHost 提供静态资源，并代理账号接口与 OpenAI 兼容图像接口。

## 🧭 架构说明

SonnetArt 是由 ASP.NET Core 10 SonnetHost 承载的浏览器应用。使用 Docker 部署时，SonnetHost 会承担静态资源服务、服务端工作台快照持久化和 API 反向代理：

- `/api/openai/*` 转发到 `SonnetArt:AiUpstreamUrl`，用于访问 OpenAI 兼容的图像模型网关。
- `/api/sonnet/*` 转发到 `SonnetArt:AccountUpstreamUrl`，并重写到 `/api/v1`，用于访问账号和 sub2api 兼容接口。
- `/api/studio/snapshot` 通过配置的 SonnetDB 服务器连接保存工作台快照。

认证、配额、计量、模型路由等能力应由上游网关实现，SonnetArt 只负责开放的创作界面和浏览器端体验。

## 🚀 快速开始

### 本地一键启动

在仓库根目录运行：

```powershell
.\start-web.ps1
```

Windows 也可以直接运行 `start-web.cmd`。脚本会恢复所需的 .NET workload，启动 `http://localhost:5131` 上的 SonnetHost，并打开浏览器。

上游地址和存储连接通过 ASP.NET Core 配置提供，例如 `appsettings.Development.json`。

### Docker 启动

创建一个 `docker-compose.yml`，把 SonnetArt 和 sub2api 兼容网关一起部署：

```yaml
services:
  sonnet-art:
    image: ghcr.io/iotsharp/sonnetart:latest
    restart: unless-stopped
    ports:
      - "8080:8080"
    volumes:
      - ./appsettings.Production.json:/app/appsettings.Production.json:ro
    depends_on:
      - sub2api
      - sonnetdb

  sonnetdb:
    image: iotsharp/sonnetdb:latest
    restart: unless-stopped
    expose:
      - "5080"

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

在 compose 文件同级创建 `appsettings.Production.json`：

```json
{
  "SonnetArt": {
    "PublicOrigin": ":8080",
    "AiUpstreamUrl": "http://sub2api:8080/",
    "AccountUpstreamUrl": "http://sub2api:8080/",
    "SonnetDbConnection": "Data Source=sonnetdb+http://sonnetdb:5080/sonnetart;Token=change-me-sonnetdb-token",
    "PromptImageWarmup": true
  }
}
```

### ⚙️ 配置

| 键 | 必填 | 说明 |
| --- | --- | --- |
| `SonnetArt:PublicOrigin` | 是 | SonnetHost 监听地址。容器内通常保持 `:8080`。 |
| `SonnetArt:AiUpstreamUrl` | 是 | OpenAI 兼容图像模型网关地址。 |
| `SonnetArt:AccountUpstreamUrl` | 是 | 账号 / sub2api 兼容 API 地址。 |
| `SonnetArt:SonnetDbConnection` | 是 | SonnetDB 服务器连接字符串，用于服务端工作台持久化。 |
| `SonnetArt:PromptImageWarmup` | 否 | 是否预热提示词库远程图片缓存。 |

## 🔗 嵌入模式

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

## 📄 License

MIT. See [LICENSE](LICENSE).
