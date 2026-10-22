# 01. TemporaryDiagnostics 可行性调研、深度头脑风暴与设计模式优化

> 编制日期：2026-10-22  
> 面向场景：ASP.NET Core 8.0/9.0/10.0 应用的一键嵌入式临时诊断网关，兼顾 Docker 容器与裸机部署。

---

## 一、需求总览与核心功能定位

目标是构建一个企业级、开箱即用的 **C# 临时诊断中间件 (`TemporaryDiagnostics`)**。其核心哲学是：
**「只需访问密钥、接口前缀可选，一行代码即可启用全功能受控诊断控制台与 AI MCP 接口；诊断结束可即时销毁或自动过期。」**

核心功能模块涵盖：
1. **交互式 Web 终端（Terminal / Web Shell）**：基于 WebSocket + xterm.js，支持 Windows ConPTY、Linux 原生 PTY 与降级 Process 流重定向；
2. **安全文件管理（Files）**：白名单限制下的目录树、文件预览、增量编辑、安全下载与打包 Zip 下载、限制性上传；
3. **软件与应用状态（Software State & App Vitals）**：.NET 运行时指标（GC 分代/暂停、堆内存、ThreadPool 队列/完成端口、JIT 状态）、进程指标（CPU%、工作集、私有内存、句柄数、线程拓扑）、已加载程序集与模块、配置源与环境变量（敏感脱敏）、最近 HTTP 请求与全局未捕获异常捕捉；
4. **宿主与系统状态（System & Hardware）**：操作系统内核版本、CPU 拓扑、内存使用率、磁盘分区挂载点与剩余空间、网卡流量与 IP 拓扑；
5. **动态反射与 IL Emit 动态调用方案（Dynamic Invocation）**：
   - 依赖注入容器（`IServiceProvider`）实时探查（注册生命周期、具体实现类型、公开方法签名）；
   - 支持动态反射调用（Reflection Invoker）；
   - **基于 IL Emit / DynamicMethod 编译委托的高性能动态调用（Fast Emit Invoker）**，避免频繁装箱拆箱与反射慢速开销；
   - 动态 C# 表达式执行沙箱（Expression / Scripting Evaluator）；
6. **零 CDN 嵌入式可视化控制台（Visual UI Dashboard）**：单程序集内嵌 Vue 3 ESM 现代化控制台，零外部网络拉取，离线高可靠；
7. **类似 Swagger / OpenAPI 文档接口**：内嵌 OpenAPI 3.0 / Scalar 文档交互界面；
8. **原生 Model Context Protocol (MCP) 接口**：兼容 Anthropic MCP 协议规范（2024-11-05），提供 Streamable HTTP (`POST /{prefix}/mcp`) 与 SSE (`GET /{prefix}/mcp/sse`) 双通道，赋予 Claude / Cursor / 外部 Agent 统一诊断能力。

---

## 二、可行性分析与潜在缺陷（头脑风暴）

针对上述 8 大功能领域，逐一剖析可行性、逻辑缺陷、性能隐患与安全防线：

### 1. 终端模块（Terminal / Web Shell）
- **可行性**：
  - Windows 环境下依赖 Windows 10 (1809+) / Windows 11 / Windows Server 2019+ 引入的原生 ConPTY (`CreatePseudoConsole`) API，配合 `STARTUPINFOEX` 与匿名管道；
  - Linux 环境下优先依赖 Posix `openpty` / `forkpty`，通过小型共享库（如 `liblinuxwebtool_pty.so`）或直接 P/Invoke libc；
  - 在环境受限无原生 PTY 时，平滑降级为 `ProcessStartInfo` 标准输入/输出重定向。
- **潜在缺陷与风险**：
  - **僵尸进程泄露**：若浏览器或 WebSocket 意外关闭/网络中断，子进程（如 `powershell.exe` 或 `bash`）可能脱离宿主变成后台死锁的孤儿进程。必须引入 **Windows Job Object (KillOnJobClose)** 或 Linux SIGHUP 进程监控与状态机生命周期托管。
  - **权限越界**：直接运行交互式 Shell 会继承 ASP.NET Core 进程的 UID/Token，若业务在 Docker 内以 root 运行，Shell 将具有 root 权限。必须提供终端默认禁用/可选启用开关，以及会话并发上限限制（默认限 2 个）。
  - **高频输出卡死**：运行 `cat large_file.log` 时，瞬时百万级字节输出可能撑爆 WebSocket 发送缓冲区。必须引入 **Channels 背压（Backpressure）限流**与输出分片。

### 2. 安全文件管理（Files）
- **可行性**：基于 .NET `System.IO` 与 `ZipArchive`，Windows 映射磁盘驱动器（C:/, D:/），Linux 映射根目录或预设安全挂载点。
- **潜在缺陷与风险**：
  - **路径穿越（Path Traversal）**：恶意构造 `../../../../etc/shadow` 或 Windows UNC 路径 `\\malicious\share`、大小写转换、Unicode 欺骗、符号链接（Symlink）逃逸。
  - **应对**：必须使用规范化路径检查（Canonicalization via `Path.GetFullPath`），并支持 **Root Allowlist 白名单策略**，对绝对路径执行越界探测并拦截。
  - **大文件 OOM**：单次查看超大文件（如 5GB 日志）导致进程内存溢出。必须限制最大文本查看上限（例如 2MB），更大文件仅允许只读流式 Range 下载。

### 3. 软件与应用状态（Software State & App Vitals）
- **可行性**：
  - 利用 `System.GC`、`Process.GetCurrentProcess()`、`ThreadPool.GetAvailableThreads`、`AppDomain.CurrentDomain.GetAssemblies()` 获取完备指标；
  - HTTP 请求与异常探查：参考 AspNetDebugDashboard 与 WatchDog.NET 的思想，通过轻量级 Middleware 拦截请求，写入基于内存的高效环形缓冲区（Ring Buffer），无需外部数据库即可保留最近 200 条请求指标和 100 条异常堆栈。
- **潜在缺陷与风险**：
  - **敏感信息泄露**：环境变量或 `IConfiguration` 中包含数据库连接串、JWT 密钥、AWS 凭证等。必须建立自动脱敏掩码字典（匹配 `*KEY*`、`*SECRET*`、`*PASSWORD*`、`*TOKEN*`，输出替换为 `******`）。
  - **内存无限制膨胀**：请求和异常日志若用无界列表存储，长期运行会导致内存泄漏。必须采用定长线程安全环形缓冲区（Lock-free Ring Buffer）。

### 4. 动态反射与 IL Emit 动态调用方案（Dynamic Invocation）
- **可行性**：
  - 探查业务注册的 `IServiceProvider`（遍历 `IServiceCollection` 单例与作用域工厂），列出所有已注册服务、公开方法与属性；
  - 动态调用：标准反射 `MethodInfo.Invoke` 机制通用但较慢，且在多次高频诊断时存在显著的装箱（Boxing）及元数据查找开销；
  - **IL Emit 动态编译优化**：利用 `System.Reflection.Emit.DynamicMethod` 或 `System.Linq.Expressions`，在首次调用时即时生成强类型委托并缓存在并发字典中，后续调用达到与原生编译代码同级的微秒级执行吞吐；
  - 支持 `async Task` 与 `async Task<T>` 异步方法的结果动态 `await` 解包与 JSON 序列化。
- **潜在缺陷与风险**：
  - **破坏性操作风险**：远程人员可能误调用 `DatabaseService.DropAllTables()` 或 `PaymentService.Refund()`。
  - **应对**：默认只读模式；高危调用需在 UI/API 上提示警告确认；记录审计日志；支持方法调用黑名单（如禁止调用私有成员或特定危险命名空间）。

### 5. 一键启动与极简配置
- **可行性**：
  - 只需：
    ```csharp
    builder.Services.AddTemporaryDiagnostics(options =>
    {
        options.AccessKey = "your-high-entropy-secure-key"; // 必填
        options.RoutePrefix = "/__diag";                   // 可选，默认 /__diag
    });
    // ...
    app.UseTemporaryDiagnostics(); // 或 app.MapTemporaryDiagnostics();
    ```
- **潜在缺陷与风险**：
  - 弱密钥漏洞：若用户配置了空字符串或简短密码（如 "123456"），容易被暴力破解。必须强制校验密钥最小长度（默认 >= 8 字符），否则启动时直接抛出 `ArgumentException` 拦截。
  - 路由冲突：若用户配置的前缀为 `/` 或与业务已有 API 冲突，会导致业务路由覆盖。必须对路由前缀做规范化校验与边界隔离。

---

## 三、设计模式深度优化

为了解决上述复杂性并保证代码的高内聚、低耦合、高性能与跨平台可扩展性，本中间件引入以下关键设计模式：

### 1. 适配器模式（Adapter Pattern）
- **跨平台系统指标适配器 (`ISystemInfoAdapter`)**：
  - `WindowsSystemInfoAdapter`：利用 P/Invoke GlobalMemoryStatusEx、GetSystemTimes 与 DriveInfo；
  - `LinuxSystemInfoAdapter`：解析 `/proc/meminfo`、`/proc/stat`、`/proc/net/dev` 与 `/sys` 虚拟文件系统；
  - `FallbackSystemInfoAdapter`：使用跨平台 .NET 运行时基类库作为安全垫底。
- **跨平台 PTY 适配器 (`IPtyAdapter`)**：
  - `WindowsConPtyAdapter`：适配 Windows ConPTY；
  - `LinuxPtyAdapter`：适配 Posix PTY；
  - `ProcessStreamPtyAdapter`：跨平台标准流降级适配器。
- **存储与日志适配器 (`ILogSinkAdapter`)**：
  - `InMemoryRingBufferLogSink`：零依赖内存环形缓冲；
  - `CompositeLogSink`：可插拔扩充到外部存储或控制台。

### 2. 策略模式（Strategy Pattern）
- **动态方法调用策略 (`IDynamicInvokeStrategy`)**：
  - `ReflectionInvokerStrategy`：经典反射策略，适用于单次临时小批量调用；
  - `EmitFastInvokerStrategy`：基于 `DynamicMethod` 的 IL Emit 高性能委托生成与缓存策略；
  - `ExpressionTreeInvokerStrategy`：基于表达式树的调用策略，兼具灵活性与类型转换能力。
- **身份鉴权策略 (`IAuthenticationStrategy`)**：
  - `BearerTokenAuthStrategy`：请求头 `Authorization: Bearer <key>` 或 `X-Diag-Key`，适用于 REST 与 MCP；
  - `SessionCookieAuthStrategy`：浏览器登录生成的短期加密 HttpOnly Cookie，杜绝 URL 传参；
  - `SignedWebSocketTokenStrategy`：为 WebSocket 握手颁发短期一次性签名票据。
- **文件沙箱访问策略 (`IFileAccessStrategy`)**：
  - `StrictAllowlistFileStrategy`：严格限制在预定义目录（如 logs、appData）内；
  - `FullDriveDiagnosticsStrategy`：受控全盘诊断策略（需管理员显式开启）。

### 3. 状态机模式（State Machine Pattern）
- **PTY 会话生命周期状态机 (`PtySessionStateMachine`)**：
  - 状态流转：`Created -> Initializing -> Active -> Detached -> Terminated -> Disposed`
  - 核心保障：在网络波动断开时转入 `Detached` 保留后台会话与输出缓冲区（允许无感重连），会话超时（如 10 分钟未重连）或客户端主动 Exit 时安全转入 `Terminated`，确保底层进程与管道彻底销毁，绝对不留孤儿僵尸。
- **临时诊断网关生命周期状态机 (`DiagnosticSessionStateMachine`)**：
  - 状态流转：`Inactive -> Active -> Expired -> Revoked`
  - 支持临时开启模式：配置会话有效时长（TTL，如 30 分钟），过期后中间件自动进入 `Expired` 状态，所有后续请求拒绝响应，实现自动安全回收。

### 4. 观察者与反应式环形缓冲模式（Observer & Ring-Buffer Pattern）
- **内存遥测观察者 (`DiagnosticTelemetryObserver`)**：
  - 拦截 ASP.NET Core `ILogger` 事件与 HTTP 请求生命周期；
  - 通过无锁并发环形队列（Atomic Lock-free Ring Buffer），实现零 GC 分配开销的高频写入；
  - 结合 `System.Threading.Channels` 向前端 SSE / WebSocket 实时广播变更通知。

---

## 四、可行性评估结论

1. **架构可行性：100% 可行**。纯 C# 技术栈可完全自包含实现上述全部功能，无需任何外部 C++ 安装包、无需外部 Docker 镜像、无需外部数据库，即可作为轻量级 NuGet 中间件直接嵌入任何 ASP.NET Core 应用。
2. **逻辑完备性：高度收敛**。通过适配器隔离平台差异、通过策略模式分流反射与 Emit、通过状态机严格管理进程与会话生命周期，从根本上杜绝了僵尸进程、路径穿越、内存泄漏等顽疾。
3. **性能表现：极高**。IL Emit 消除反射慢速瓶颈；Ring Buffer 消除日志 GC 瓶颈；Channels 异步消除 CPU 轮询空转瓶颈。
