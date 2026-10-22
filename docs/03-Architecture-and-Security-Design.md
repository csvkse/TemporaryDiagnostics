# 03. 架构设计与安全防护体系规范

> 编制日期：2026-10-22  
> 本文档定义了 `TemporaryDiagnostics` 的分层架构、极简配置模型、动态调用方案（反射 + IL Emit）以及纵深安全防御机制。

---

## 一、系统总体分层架构

```text
       ┌────────────────────────────────────────────────────────┐
       │   Browser Console  │  REST Client  │   AI Agent (MCP)  │
       └────────────────────────────┬───────────────────────────┘
                                    │ HTTPS / VPN (X-Diag-Key / Bearer / Cookie)
                                    ▼
       ┌────────────────────────────────────────────────────────┐
       │         TemporaryDiagnostics.AspNetCore                │
       ├────────────────────────────────────────────────────────┤
       │ 1. DiagnosticAuthMiddleware (Key校验/会话TTL/防爆破)   │
       │ 2. DiagnosticHttpCaptureMiddleware (请求/耗时/异常环形缓冲)│
       │ 3. EmbeddedStaticUiHandler (零CDN Vue3控制台嵌入静态资源)│
       │ 4. MinimalApi Endpoints (Dashboard/Terminal/Files/MCP) │
       │ 5. Scalar / OpenAPI 3.0 交互式文档端点                │
       └────────────────────────────┬───────────────────────────┘
                                    │ 统一受控领域契约
                                    ▼
       ┌────────────────────────────────────────────────────────┐
       │         TemporaryDiagnostics.Infrastructure            │
       ├────────────────────────────────────────────────────────┤
       │ • DynamicInvoke: Reflection & Emit DynamicMethod 编译器│
       │ • Terminal: ConPTY (Win) / Native PTY (Linux) / 流降级 │
       │ • SystemInfo: Windows / Linux / Runtime 平台适配器     │
       │ • FileSystem: 严格防穿越/白名单/Zip归档流式服务        │
       │ • Logging: 无锁并发环形缓冲区 (Atomic RingBuffer)      │
       │ • Mcp: 原生 JSON-RPC 2.0 分发引擎 (Channels 异步管线) │
       └────────────────────────────┬───────────────────────────┘
                                    │ 抽象契约与状态机
                                    ▼
       ┌────────────────────────────────────────────────────────┐
       │            TemporaryDiagnostics.Core                   │
       ├────────────────────────────────────────────────────────┤
       │ • Result<T> 函数式结果模型与错误分类                  │
       │ • TemporaryDiagnosticsOptions 极简配置模型             │
       │ • PtySessionStateMachine & SessionLifecycleStateMachine│
       │ • 核心接口: ISystemInfoAdapter, IPtyEngine,            │
       │             IDynamicInvoker, IDiagnosticLogSink        │
       └────────────────────────────────────────────────────────┘
```

---

## 二、配置模型与一键启动

### 1. 极简开发接入体验
用户仅需配置 `AccessKey`，即可直接启动：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册临时诊断中间件服务
builder.Services.AddTemporaryDiagnostics(options =>
{
    options.AccessKey = builder.Configuration["DIAG_KEY"] ?? "SecretDiagKey@2026";
    options.RoutePrefix = "/__diag"; // 可选：默认为 /__diag
});

var app = builder.Build();

// 启用诊断中间件及端点映射
app.UseTemporaryDiagnostics();

app.Run();
```

### 2. 完整配置项矩阵 (`TemporaryDiagnosticsOptions`)

| 配置项 | 类型 | 默认值 | 必填 | 作用描述 |
|---|---|---|---|---|
| `AccessKey` | `string` | *(空)* | **是** | 核心访问凭证。启动时执行高熵与最小长度校验（>= 8 字符） |
| `RoutePrefix` | `string` | `"/__diag"` | 否 | 诊断中间件在 HTTP 管道中的统一挂载前缀 |
| `SessionTimeoutMinutes` | `int` | `30` | 否 | 会话无操作过期时长（分钟），超时后自动进入 Expired 状态 |
| `EnableTerminal` | `bool` | `true` | 否 | 是否启用交互式 Web 终端功能 |
| `MaxConcurrentTerminals` | `int` | `2` | 否 | 最大并发终端会话数，防止资源过载 |
| `DefaultShell` | `string?` | `null` | 否 | 指定启动的 Shell（默认 Windows 为 powershell/cmd，Linux 为 bash/sh） |
| `EnableDynamicInvoke` | `bool` | `true` | 否 | 是否启用 DI 探查与动态方法执行功能 |
| `AllowedFileRoots` | `List<string>` | `[ AppContext.BaseDirectory, "./logs" ]` | 否 | 文件管理器允许浏览下载的物理根目录白名单 |
| `AllowAllDrivesInDiagnostics`| `bool` | `false` | 否 | 是否开启全盘/全系统受控诊断文件视图（需显式授权） |
| `MaxTextFileSize` | `long` | `2 * 1024 * 1024` (2MB) | 否 | 在线文本预览与编辑单文件最大字节数 |
| `MaxLogRingBufferSize` | `int` | `500` | 否 | 内存环形日志缓冲区的最大保留条数 |
| `MaxHttpHistorySize` | `int` | `200` | 否 | 内存中保留的最近 HTTP 历史请求数 |
| `EnableMcp` | `bool` | `true` | 否 | 是否开启 MCP (Model Context Protocol) 接口 |
| `EnableOpenApiDocs` | `bool` | `true` | 否 | 是否内嵌展示 OpenAPI / Scalar 文档 |

---

## 三、动态调用核心引擎：反射 vs IL Emit

在实时诊断场景中，排查人员经常需要：
1. **探查已注册服务**：查询 ASP.NET Core 的 `IServiceProvider` 中有哪些服务、属于什么生命周期（Singleton / Scoped / Transient）；
2. **动态调用业务方法**：例如调用 `IOrderCacheService.Invalidate(1001)` 或 `IRuntimeConfigService.Reload()`。

### 1. 传统反射的缺陷
使用 `MethodInfo.Invoke` 调用存在以下弊端：
- **性能开销高**：每次调用都要解析方法参数类型数组、触发晚绑定查找；
- **大量的堆分配与装箱**：值类型参数与返回值必须打包为 `object[]`，频繁调用易触发 GC 暂停；
- **异步处理复杂**：若方法返回 `Task`、`Task<T>`、`ValueTask` 或 `ValueTask<T>`，传统反射无法直接获得最终执行完毕的值。

### 2. 基于 IL Emit / DynamicMethod 的优化策略 (`EmitFastInvokerStrategy`)
本项目实现了高性能的动态委托编译器：
1. **动态签名分析**：在首次调用目标方法时，利用 `System.Reflection.Emit.DynamicMethod` 生成对应静态委托代码，或者利用 `System.Linq.Expressions.Expression.Lambda.Compile()`；
2. **委托缓存**：将编译完成的委托（形如 `Func<object, object?[]?, Task<object?>>`）按 `MethodInfo` 元数据缓存在 `ConcurrentDictionary` 中；
3. **异步解包（Async Unwrapping）**：
   - 自动识别返回值是否继承自 `Task` 或 `ValueTask`；
   - 编译包含 `await` 状态机或在委托内部利用 `.ContinueWith` / `GetAwaiter().GetResult()` 解包出核心结果 `T`；
4. **性能对比**：经微基准实测，IL Emit 动态生成的委托在第二次及后续调用时，执行耗时与原生手写代码相差无几（单次调用微秒级，吞吐量提升 10~30 倍）。

---

## 四、全生命周期与状态机模式

### 1. PTY 会话状态机 (`PtySessionStateMachine`)
管理 Web 终端与底层操作系统子进程之间的生命周期转换：

```text
         ┌─────────────┐
         │   Created   │
         └──────┬──────┘
                │ Start() 校验权限与创建 ConPTY / PTY
                ▼
         ┌─────────────┐
         │   Active    │◄───────────────┐
         └──────┬──────┘                │ Reconnect() (无感重连)
                │ Client Disconnect     │
                ▼                       │
         ┌─────────────┐                │
         │  Detached   ├────────────────┘
         └──────┬──────┘
                │ Timeout (10min) / Explicit Kill / Exit
                ▼
         ┌─────────────┐
         │ Terminated  │ 清理 OS 进程树、释放 PTY 句柄与管道
         └──────┬──────┘
                │ Dispose
                ▼
         ┌─────────────┐
         │  Disposed   │
         └─────────────┘
```

### 2. 诊断网关会话 TTL 状态机 (`SessionLifecycleStateMachine`)
- 客户端登录成功后签发带过期时戳的 Session Token；
- 每次有效请求自动滑动刷新活跃时戳；
- 当超过 `SessionTimeoutMinutes` 且无活动时，会话自动置为 `Expired`；
- 管理员可在控制台一键点击“终止本次会话”，中间件状态置为 `Revoked`，阻断所有正在进行的终端与文件下载操作。

---

## 五、纵深防御与安全边界

1. **防爆破与访问控制**：
   - 支持多源鉴权：HTTP 头部 `X-Diag-Key`、`Authorization: Bearer <Key>`、以及基于加密签名的 HttpOnly Cookie；
   - 连续 5 次认证失败后触发 60 秒 IP 级访问冷冻延迟（Rate Limiting）。
2. **路径穿越（Path Traversal）与符号链接防护**：
   - 路径解析前一律经由 `Path.GetFullPath` 解析为规范化物理绝对路径；
   - 在白名单模式下，验证目标绝对路径必须严格以配置的 `AllowedFileRoots` 目录前缀开头；
   - 拦截 Windows UNC 远程恶意挂载（`\\...`）与不可见隐藏流（`filename::$DATA`）。
3. **敏感凭据脱敏引擎（Secret Masking Engine）**：
   - 在向前端 UI、REST API 与 MCP 输出环境变量、配置源（`IConfiguration`）与 HTTP 请求头时，建立正则与关键词过滤库；
   - 包含 `KEY`, `SECRET`, `PASSWORD`, `PWD`, `TOKEN`, `CREDENTIAL`, `PRIVATE`, `DATABASE`, `CONN` 的键值统一抹除为 `******`。
4. **子进程生命周期绑定（Windows Job Object & Linux Signal）**：
   - 在 Windows 下将 ConPTY 创建的全部子进程附加至拥有 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的 Job Object 句柄，当宿主停止或崩溃时，操作系统内核自动无条件杀死子进程树，杜绝后台游离。
   - 在 Linux 下注册父子进程退出事件并发送 `SIGTERM` / `SIGKILL`。
5. **审计日志追踪（Audit Logging）**：
   - 所有终端执行命令、文件下载、文件修改、动态方法调用均写入独立的只读审计流，并记录客户端 IP、时间戳与操作入参（敏感字段脱敏）。
