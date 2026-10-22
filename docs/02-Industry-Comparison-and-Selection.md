# 02. 业界主流方案对比与选型评估报告

> 编制日期：2026-10-22  
> 本报告对比了现存主流 .NET 及业界诊断方案，并论证了 `TemporaryDiagnostics` 的差异化优势与复用路径。

---

## 一、业界主流诊断方案对比矩阵

| 特性维度 | AspNetDebugDashboard | WatchDog.NET | dotnet-monitor | Aspire Dashboard | Spring Boot Actuator | **TemporaryDiagnostics (本项目)** |
|---|---|---|---|---|---|---|
| **集成形式** | 嵌入式 NuGet 中间件 | 嵌入式 NuGet 中间件 | 独立 CLI / Sidecar 进程 | 独立容器 / 遥测收集端点 | 嵌入式 Starter 库 | **单依赖嵌入式 NuGet 中间件** |
| **一键启动配置** | 需配置 LiteDB 路径与环境 | 需配置外部 SQL/LiteDB 连接 | 需配置命名管道/诊断端口 | 需配置 OTLP 导出协议 | 需配置 management endpoints | **仅需 `AccessKey` + 可选 `RoutePrefix`** |
| **存储依赖** | LiteDB（写磁盘文件） | 外部关系型数据库 / LiteDB | 无（流式导出 / Dump） | 内存 / 遥测存储 | 内存 / 监控后端（Prometheus） | **纯内存环形缓冲（零写盘、零外挂 DB）** |
| **Web 终端能力** | ❌ 无 | ❌ 无 | ❌ 无 | ❌ 无 | ❌ 无 | **✅ 跨平台 PTY/ConPTY 交互式终端** |
| **安全文件管理** | ❌ 无 | ❌ 无 | ❌ 仅限 Dump 导出 | ❌ 无 | ❌ 无 | **✅ 目录树/预览/Zip 下载/防穿越** |
| **系统与进程状态** | 需配合 AspNetVitals | 仅限异常与请求日志 | ✅ 深度运行时与 GC 指标 | ✅ OTel 指标与链路追踪 | ✅ JVM/OS 指标 | **✅ 系统拓扑 + GC/ThreadPool/进程全景** |
| **动态调用 (反射/Emit)**| ❌ 无 | ❌ 无 | ❌ 无 | ❌ 无 | ❌ 仅限 Bean 属性只读探查 | **✅ DI 探查 + 反射 + IL Emit 高速调用** |
| **API 文档支持** | 仅内部 REST API | 仅自带专有 UI | 仅 Swagger OpenAPI | 专注遥测数据展示 | 开放 OpenAPI/Actuator 端点 | **✅ 内嵌 OpenAPI 3.0 / Scalar 交互文档** |
| **Model Context Protocol**| 仅外部单独 dotnet tool | ❌ 无 | ❌ 无 | 仅概念验证 Agent | ❌ 无 | **✅ 原生内嵌 MCP (HTTP/SSE 双通道)** |
| **资源与内存开销** | ~15MB (LiteDB 缓冲) | ~20MB (ORM 缓存) | 独立进程 ~80MB+ | 独立容器 ~150MB+ | ~10MB | **< 3MB (极简内存环形缓冲)** |
| **生命周期管控** | 随宿主长久驻留 | 随宿主长久驻留 | 独立启停 | 独立服务 | 随宿主长久驻留 | **✅ 支持 TTL 自动过期与一键撤销** |

---

## 二、现有项目深度剖析

### 1. AspNetDebugDashboard
- **核心亮点**：
  - 界面美观，集中捕获 HTTP 请求详情、SQL 执行日志（通过 Dapper/EF Core 拦截）、未处理异常；
  - 提供了 AspNetVitals 插件用于收集内存和 GC 状况；
  - 拥有配套的 `AspNetDebugDashboard.Mcp` 工具。
- **存在的缺口与局限**：
  - **缺少终端与文件能力**：运维与诊断人员在排查容器内特定路径配置文件或排查网络路由时，仍然必须依赖外部 SSH 或 `docker exec`；
  - **磁盘写入负担**：强制写入 LiteDB 数据库文件，在只读文件系统（如受限容器或无写入权限的环境）中会直接启动报错；
  - **无动态调用执行方案**：无法在运行时主动调用业务方法验证逻辑修复。

### 2. dotnet-monitor
- **核心亮点**：
  - 微软官方力推的深度诊断工具，基于 EventPipe 与 DiagnosticsClient，能采集最权威的 gcdump、process dump、speedscope trace 等。
- **局限性**：
  - 属于**重型诊断工具**，主要提供 REST API 和命令行，没有为非专业人员提供易用的单页面自包含文件与终端控制台；
  - 无法直接探查业务层 DI 容器与执行业务方法；
  - 配置复杂度高（需映射 Unix Domain Socket 或 Named Pipes）。

### 3. WatchDog.NET
- **核心亮点**：
  - 极轻量的异常记录器，集成简单。
- **局限性**：
  - 功能过于单一，只局限于 Exception / Request 记录；
  - 同样需要配置存储提供程序（DbProvider）。

---

## 三、代码复用与架构借鉴方案

本项目将深度复用并重构 `WebToolForLinux` 与 `Reader.Core` 的优秀设计资产：

### 1. 从 `WebToolForLinux` 复用与优化：
- **跨平台 PTY 引擎 (`CrossPlatformPtyEngine`)**：
  - 复用其对 Windows `kernel32.dll` ConPTY 的完整 P/Invoke 结构定义（`STARTUPINFOEX`、`CreatePseudoConsole`）；
  - 复用其 Linux Native PTY 抽象与进程流重定向回退机制；
  - 增加 **Session 状态机** 与 **Windows Job Object 自动回收**，确保 WebSocket 断开不产生僵尸子进程。
- **系统硬件与网络指标探查 (`SystemInfo`)**：
  - 提取其跨平台 CPU、内存、多网卡与磁盘分区的采集算法。
- **原生 AOT 兼容的 MCP 引擎 (`McpServerEngine`)**：
  - 复用其基于 `System.Threading.Channels` 的 SSE 下行管道与 JSON-RPC 2.0 分发引擎；
  - 重构工具注册机制，使所有诊断功能（系统状态、进程指标、文件、日志、动态方法调用）自动投影为统一的 MCP Tools。
- **零外部 CDN 嵌入式 Vue 3 ESM 控制台**：
  - 裁剪与重构其前端页面为自包含单文件/嵌入式资源，支持动态根据 `RoutePrefix` 自适应路由前缀。

### 2. 从 `Reader.Core` 借鉴与升华：
- **领域设计与统一结果模型 (`Result` / `Result<T>`)**：
  - 严格采用 `Reader.Core` 的 Functional Result 模式，彻底杜绝在内部业务逻辑中随意抛出不可控异常；
  - 提供 `ResultExtensions` 将结果无缝转化为 ASP.NET Core Minimal API 的 `IResult`（`Ok`, `BadRequest`, `NotFound`, `Unauthorized`, `Forbid`）。
- **特性切片与干净依赖注入 (`ServiceCollectionExtensions`)**：
  - 借鉴其清晰的模块注册组织风格，将中间件拆解为 `Core`（纯抽象与模型）、`Infrastructure`（平台适配器与引擎）、`AspNetCore`（路由与中间件绑定）。
