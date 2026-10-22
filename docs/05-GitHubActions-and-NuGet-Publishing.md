# 05. GitHub Actions 自动化 CI/CD 与 NuGet 发布方案

> 编制日期：2026-10-22  
> 本文档详细记录了 `TemporaryDiagnostics` 的 GitHub Actions 持续集成、Release 生成以及自动化推送到官方 nuget.org 与 GitHub Packages 的完整流水线规范。

---

## 一、流水线架构概览

```text
开发者 / Maintainer
   │
   ├─► git push origin main ───────► [.github/workflows/ci.yml]
   │                                   └─► 运行还原、编译与 25 项测试
   │
   └─► git push origin v* (如 v1.0.1) ─► [.github/workflows/release.yml]
         或 GitHub Actions 手动触发       ├─► 1. 检出完整代码与版本识别
                                         ├─► 2. 自动化执行单元与集成测试
                                         ├─► 3. 编译并打包 3 个 NuGet 包
                                         ├─► 4. 自动提取 Git 提交生成更新日志
                                         ├─► 5. 创建 GitHub Release 并附带包资产
                                         ├─► 6. 推送至 GitHub Packages NuGet 源
                                         └─► 7. 基于 NUGET_API_KEY 推送至官方 nuget.org
```

---

## 二、工作流详细配置规范

### 1. 持续集成：[`.github/workflows/ci.yml`](../.github/workflows/ci.yml)
- **触发分支**：`main`
- **忽略路径**：文档变更（`*.md`, `docs/**`）不触发重复构建；
- **执行环境**：`ubuntu-latest`
- **执行步骤**：
  1. 安装 .NET 10 SDK（含预览支持）；
  2. `dotnet restore`；
  3. `dotnet build --no-restore -c Release`；
  4. `dotnet test --no-build -c Release --verbosity normal`。

### 2. NuGet 打包与发布：[`.github/workflows/release.yml`](../.github/workflows/release.yml)
- **触发条件**：
  - 推送带 `v` 前缀的 Tag（如 `v1.0.1`, `v1.1.0`）；
  - 手动调度（`workflow_dispatch`），提供自定义版本号与 `push_to_nuget` 开关。
- **打包清单**：
  - `TemporaryDiagnostics.Core`: 核心契约、Result 模型与状态机；
  - `TemporaryDiagnostics.Infrastructure`: PTY 终端引擎、Fast Emit 编译器、系统适配器与 MCP 引擎；
  - `TemporaryDiagnostics.AspNetCore`: 一键中间件集成、Minimal API、单页可视化 UI 与 OpenAPI。
- **双发布通道**：
  - **通道 A：GitHub Release**：自动附带版本更新日志和所有 `.nupkg` 文件；
  - **通道 B：官方 nuget.org**：
    - **首选现代方案**：**Trusted Publishing (OIDC 无密钥)**，利用 GitHub Actions 的 `id-token: write` 与 `NuGet/login@v1` 交换临时单次密钥，免维护、零泄露风险；
    - **备选传统方案**：读取仓库 Secret `NUGET_API_KEY`。

---

## 三、nuget.org 认证配置规范

### 1. 推荐：Trusted Publishing（受信任发布者 / OIDC 无密钥）
微软官方推荐的现代化发布方式，无需在 GitHub 存储静态 API Key：
1. 访问 [https://www.nuget.org/account/trustedpublishing](https://www.nuget.org/account/trustedpublishing)；
2. 填写 Policy 参数：
   - **Policy Name**：`TemporaryDiagnostics-Release`
   - **Package Owner**：`csvkse`
   - **CI/CD Provider**：`GitHub Actions`
   - **Repository Owner**：`csvkse`
   - **Repository**：`TemporaryDiagnostics`
   - **Workflow File**：`release.yml`
   - **Environment**：留空
   - **Scopes**：勾选 `Push new packages and package versions`
   - **Glob patterns**：`*`
3. 工作流中已预置 `permissions: id-token: write` 与 `NuGet/login@v1`，直接触发 Tag 即可自动完成 OIDC 鉴权发布。

### 2. 备用：传统 API Key 方式
1. 访问 [https://www.nuget.org/account/apikeys](https://www.nuget.org/account/apikeys)；
2. 点击 **Create**：
   - **Key Name**：`TemporaryDiagnostics-Release`
   - **Package Owner**：`csvkse`
   - **Scopes**：勾选 `Push` ➔ `Push new packages and package versions`
   - **Glob pattern**：`*`
3. 复制生成的 Key，在 GitHub 仓库设置 `Settings > Secrets and variables > Actions` 中添加 Secret：`NUGET_API_KEY`。

---

## 四、版本发布与验证标准

发布新版本遵循严格语义化版本（SemVer 2.0）：

```bash
# 1. 更新全局版本号（Directory.Build.props）
# 2. 提交并推送到 main 分支
git add -A
git commit -m "chore(release): bump version to 1.0.2"
git push origin main

# 3. 创建并推送 Git Tag 触发自动化工作流
git tag -a v1.0.2 -m "Release v1.0.2: Enable NuGet Trusted Publishing"
git push origin v1.0.2
```

发布完成后可在以下位置验证：
1. **GitHub Actions 运行日志**：查看 `Push to Official NuGet.org` 步骤输出；
2. **GitHub Releases**：[https://github.com/csvkse/TemporaryDiagnostics/releases](https://github.com/csvkse/TemporaryDiagnostics/releases)；
3. **nuget.org 个人包列表**：[https://www.nuget.org/profiles/](https://www.nuget.org/profiles/)（通常上传后经过 2~5 分钟的系统索引即全网生效）。

---

## 五、常见发布异常排查 (Troubleshooting)

### 1. `Push to Official NuGet.org` 失败报 `403 (Forbidden)`
- **原因 A（最常见）**：API Key 的 Scopes 未勾选 **`Push new packages and package versions`**（允许推送新包和新版本）。
  - 若勾选了默认的 `Push only new package versions`（仅推送新包版本），因 `TemporaryDiagnostics.*` 是新上架的包，nuget.org 会拦截并返回 403。
  - **解决办法**：在 nuget.org 重新编辑该 API Key，修改权限为 `Push new packages and package versions`，更新 GitHub Secrets。
- **原因 B**：Glob pattern 未匹配。
  - 若填写了 `TemporaryDiagnostics`，子包 `TemporaryDiagnostics.Core` 会匹配失败。
  - **解决办法**：Glob pattern 改为 `*` 或 `TemporaryDiagnostics*`。
- **原因 C**：GitHub Secret 名称拼写错误。
  - 必须严格为 `NUGET_API_KEY`（无空格）。

### 2. 重试发布（无需重新打 tag）
在 GitHub Actions 页面直接点击失败的 Run -> 点击右上角 **`Re-run jobs`** -> **`Re-run failed jobs`**，配置生效后即可自动重新推送，无需删除重建 Tag。
