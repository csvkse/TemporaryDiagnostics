# TemporaryDiagnostics

> 企业级 C# 临时诊断中间件：一键嵌入、极简配置、全功能受控诊断网关、动态反射与 IL Emit 调用、原生 MCP 支持。

---

## 🌟 核心特性与架构亮点

1. **一键启动，极简参数**：
   - 仅需配置访问密钥 `AccessKey`（强制高熵防弱口令），路由前缀 `RoutePrefix`（可选，默认 `/__diag`）。
   - 一行代码即插即用：`builder.Services.AddTemporaryDiagnostics(...)` + `app.UseTemporaryDiagnostics()`。
2. **纯内存零外部依赖**：
   - 不写磁盘 SQLite/LiteDB，避免受限容器/只读挂载文件系统报错。
   - 内置无锁固定容量环形缓冲区（Concurrent Ring Buffer），恒定内存开销（< 3MB），消除 GC 压力。
3. **交互式 Web 终端（Web Shell）**：
   - 基于 xterm.js + WebSocket 双向流。
   - 支持 Windows ConPTY、Linux 原生 PTY 与标准流降级模式。
   - 严格生命周期状态机与 Windows Job Object 绑定，断线不留僵尸孤儿进程。
4. **安全文件管理（Files）**：
   - 严防路径穿越（Path Traversal Guard），拦截 UNC、NTFS 流与非法越界。
   - 支持目录树浏览、文本安全只读查看（2MB 截断保护）、Zip 打包流式下载、受控上传。
5. **动态反射与 Fast Emit 高速调用**：
   - 实时探查 `IServiceProvider` 依赖注入容器服务及生命周期（Singleton/Scoped/Transient）。
   - **基于 IL Emit / 表达式树编译委托**，将方法调用编译为高性能强类型委托并并发缓存，执行开销微秒级，相比传统反射吞吐提升 10~30 倍。
   - 自动识别并异步解包 `Task`、`Task<T>`、`ValueTask`、`ValueTask<T>`。
6. **零 CDN 嵌入式单页控制台**：
   - 单程序集自包含现代化响应式前端，离线 100% 可用，无需外部 CDN。
   - 提供仪表盘、终端、文件管理、动态调用、日志历史与 MCP 测试器。
7. **类似 Swagger / OpenAPI 文档**：
   - 暴露 `/{prefix}/openapi.json` 与 Scalar 交互式 API 浏览器 `/{prefix}/docs`。
8. **原生 Model Context Protocol (MCP)**：
   - 原生支持 Anthropic MCP 协议规范 (2024-11-05)。
   - 提供 Streamable HTTP (`POST /{prefix}/mcp`) 与 SSE (`GET /{prefix}/mcp/sse`) 双通道。
   - 原生暴露 8 个受控诊断工具，供 Claude、Cursor 等 AI Agent 直接接入。

---

## 🚀 快速接入指南

### 1. 注册中间件服务

在 `Program.cs` 中添加配置：

```csharp
using TemporaryDiagnostics.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 注册业务服务
builder.Services.AddSingleton<IMyService, MyService>();

// 一键启用诊断中间件（仅 AccessKey 必填）
builder.Services.AddTemporaryDiagnostics(options =>
{
    options.AccessKey = builder.Configuration["DIAG_KEY"] ?? "MySecureDiagKey@2026";
    options.RoutePrefix = "/__diag"; // 可选：默认为 /__diag
    options.SessionTimeoutMinutes = 30; // 可选：会话自动过期时长
});

var app = builder.Build();

// 一键挂载中间件管道与端点
app.UseTemporaryDiagnostics();

app.Run();
```

### 2. 访问控制台与文档

- **可视化诊断控制台**：`http://localhost:5000/__diag`（输入配置的 `AccessKey` 登录）
- **OpenAPI 交互式文档**：`http://localhost:5000/__diag/docs`
- **OpenAPI 描述文件**：`http://localhost:5000/__diag/openapi.json`
- **MCP HTTP 端点**：`http://localhost:5000/__diag/mcp`
- **MCP SSE 端点**：`http://localhost:5000/__diag/mcp/sse`

---

## 🛠️ MCP 工具集清单

| 工具名称 | 描述说明 |
|---|---|
| `get_system_overview` | 获取系统操作系统、CPU 核心数、物理内存与磁盘挂载信息 |
| `get_runtime_vitals` | 获取 .NET 运行时 GC 分代、堆大小、ThreadPool 状态及进程工作集 |
| `query_recent_logs` | 获取内存环形缓冲区中的运行日志、HTTP 历史请求与未捕获异常 |
| `list_directory_files` | 受白名单保护的安全目录文件列表浏览 |
| `read_text_file` | 只读查看白名单路径下的文本文件内容 |
| `inspect_di_services` | 探查应用 DI 容器中注册的所有服务与公共方法签名 |
| `invoke_diagnostic_method` | 基于 IL Emit 动态执行目标方法并获取 JSON 结果 |
| `execute_diagnostic_command` | 在受控沙箱中执行单次命令行排查指令 |

---

## 📂 项目工程结构

```text
TemporaryDiagnostics/
├── docs/                                          # 详尽调研、头脑风暴与架构规范文档
│   ├── 01-Feasibility-and-Brainstorming.md        # 可行性深度分析、边界陷阱与设计模式应用
│   ├── 02-Industry-Comparison-and-Selection.md    # 现有库选型与对比报告
│   ├── 03-Architecture-and-Security-Design.md     # 架构设计、动态 Emit 机制与纵深安全防线
│   ├── 04-API-and-MCP-Specification.md           # REST 路由与 MCP JSON-RPC 2.0 协议规范
│   ├── 05-GitHubActions-and-NuGet-Publishing.md   # GitHub Actions 自动化 CI/CD 与 NuGet 发布方案
│   └── 06-Terminal-and-UI-Diagnosis-and-Optimization.md # 终端跨平台换行标准化、会话生命周期与现代控制台 UI 优化方案
├── src/
│   ├── TemporaryDiagnostics.Core/                 # 领域契约、Result<T>、配置模型与状态机
│   ├── TemporaryDiagnostics.Infrastructure/       # PTY 引擎、IL Emit 编译器、系统适配器、环形缓冲、MCP 引擎
│   ├── TemporaryDiagnostics.AspNetCore/           # 中间件、Minimal APIs、零 CDN 嵌入 UI、OpenAPI 文档
│   └── TemporaryDiagnostics.SampleHost/           # 一键启动示例可运行工程
└── tests/
    └── TemporaryDiagnostics.Tests/                # 25 项全量单元与 TestServer 端到端集成测试
```

---

## 🧪 运行单元与集成测试

```bash
dotnet test
```

所有 25 项测试覆盖：
- Fast Emit 与反射动态调用微秒级吞吐验证；
- 路径穿越、UNC 挂载与 NTFS 数据流防护；
- 环境变量与配置敏感键自动脱敏；
- MCP JSON-RPC 2.0 协议工具调度；
- TestServer 端到端身份鉴权、Cookie 签发与 OpenAPI 路由验证。
