# 04. 诊断中间件 API 与 MCP 协议规范定义

> 编制日期：2026-10-22  
> 本文档定义了 `TemporaryDiagnostics` 的统一 REST 接口、OpenAPI 映射与 Model Context Protocol (MCP) JSON-RPC 2.0 工具协议。

---

## 一、REST API 路由规范

所有路由统一挂载于配置的 `{RoutePrefix}` 下（默认 `/__diag`）。除登录和静态资源外，所有接口均需要通过 `Authorization: Bearer <key>`、`X-Diag-Key: <key>` 或会话 Cookie 认证。

### 1. 认证与会话管理
| HTTP 方法 | 路径 | 描述 | 鉴权要求 |
|---|---|---|---|
| `POST` | `/__diag/api/auth/login` | 凭访问密钥登录，换取 Session Cookie / Token | 匿名 |
| `POST` | `/__diag/api/auth/logout` | 退出登录，注销当前会话与终端连接 | 已认证 |
| `GET` | `/__diag/api/auth/status` | 查询当前会话有效性与剩余 TTL | 匿名/已认证 |

### 2. 软件状态与系统指标
| HTTP 方法 | 路径 | 描述 |
|---|---|---|
| `GET` | `/__diag/api/system/overview` | 宿主系统全景（OS、CPU 核心数、物理内存总量/空闲、磁盘分区、网卡 IP） |
| `GET` | `/__diag/api/system/vitals` | .NET 运行时指标（GC 分代/堆、ThreadPool 线程与队列、进程 CPU%、工作集、句柄数） |
| `GET` | `/__diag/api/system/environment` | 环境变量与配置项（敏感字段自动脱敏） |
| `GET` | `/__diag/api/system/assemblies` | 已加载程序集与模块列表（名称、版本、路径） |

### 3. 日志、请求与异常探查
| HTTP 方法 | 路径 | 描述 |
|---|---|---|
| `GET` | `/__diag/api/logs/recent` | 获取内存环形缓冲中的最近应用日志 |
| `GET` | `/__diag/api/logs/requests` | 获取最近 HTTP 请求记录（路径、状态码、响应耗时、客户端 IP） |
| `GET` | `/__diag/api/logs/exceptions` | 获取最近未捕获异常详情与堆栈轨迹 |
| `POST` | `/__diag/api/logs/clear` | 清理当前内存环形缓冲中的日志记录 |

### 4. 安全文件管理
| HTTP 方法 | 路径 | 描述 |
|---|---|---|
| `GET` | `/__diag/api/files/list` | 浏览指定目录（参数 `path`，受白名单与防穿越守卫约束） |
| `GET` | `/__diag/api/files/read` | 查看文本文件内容（限制 2MB） |
| `POST` | `/__diag/api/files/save` | 保存文本文件内容 |
| `GET` | `/__diag/api/files/download` | 单文件流式下载或将目录打包为 Zip 下载（参数 `archive=true`） |
| `POST` | `/__diag/api/files/upload` | 上传文件至白名单受控目录 |
| `DELETE`| `/__diag/api/files/delete` | 安全删除文件或空目录（受保护系统目录禁止删除） |

### 5. 动态反射与 IL Emit 动态调用
| HTTP 方法 | 路径 | 描述 |
|---|---|---|
| `GET` | `/__diag/api/invoke/services` | 探查 `IServiceProvider` 中已注册的服务契约、生命周期与实现类型 |
| `GET` | `/__diag/api/invoke/methods` | 获取指定服务类型的公共方法与参数签名 |
| `POST` | `/__diag/api/invoke/call` | 动态执行目标方法（支持传参，支持快速 IL Emit 模式与经典反射模式，支持自动 await 解包） |

**调用入参示例 (`POST /__diag/api/invoke/call`)**：
```json
{
  "serviceTypeName": "MyApp.Services.IOrderService",
  "methodName": "GetOrderStatusAsync",
  "parameters": [ 10024 ],
  "useFastEmit": true
}
```

### 6. Web 交互式终端 (WebSocket)
| 协议 | 路径 | 描述 |
|---|---|---|
| `WS` | `/__diag/api/terminal/ws?token={sessionToken}&cols=120&rows=32` | 交互式终端双向数据流通道 |

### 7. OpenAPI 文档与控制台 UI
| HTTP 方法 | 路径 | 描述 |
|---|---|---|
| `GET` | `/__diag` | 诊断可视化控制台首页（单页 SPA 应用） |
| `GET` | `/__diag/docs` | 内嵌 OpenAPI / Scalar 交互式 API 文档页面 |
| `GET` | `/__diag/openapi.json` | OpenAPI 3.0 元数据描述文档 |

---

## 二、Model Context Protocol (MCP) 规范

`TemporaryDiagnostics` 原生实现 Anthropic MCP 协议规范（2024-11-05），支持通过 Streamable HTTP (`POST /__diag/mcp`) 与 SSE (`GET /__diag/mcp/sse`) 与外部 AI Agent 交互。

### 1. 暴露的 MCP Tools 清单

1. `get_system_overview`
   - **描述**：获取宿主系统硬件、操作系统、CPU 使用率、磁盘挂载点与网络 IP 配置。
   - **Schema**：无必填参数。

2. `get_runtime_vitals`
   - **描述**：获取 .NET 运行时内存堆、GC 收集次数、ThreadPool 状态及当前进程工作集。
   - **Schema**：无必填参数。

3. `query_recent_logs`
   - **描述**：检索最近发生的日志、HTTP 请求记录与未处理异常堆栈。
   - **Schema**：
     ```json
     {
       "type": "object",
       "properties": {
         "category": { "type": "string", "enum": ["all", "logs", "requests", "exceptions"] },
         "limit": { "type": "integer", "default": 50 }
       }
     }
     ```

4. `list_directory_files`
   - **描述**：列出受白名单保护目录下的文件与文件夹。
   - **Schema**：
     ```json
     {
       "type": "object",
       "properties": {
         "path": { "type": "string", "description": "相对或绝对路径（受白名单限制）" }
       },
       "required": ["path"]
     }
     ```

5. `read_text_file`
   - **描述**：只读查看指定白名单路径内的文本配置文件或日志文件内容。
   - **Schema**：
     ```json
     {
       "type": "object",
       "properties": {
         "path": { "type": "string" },
         "max_bytes": { "type": "integer", "default": 1048576 }
       },
       "required": ["path"]
     }
     ```

6. `inspect_di_services`
   - **描述**：探查应用依赖注入容器（DI）中注册的服务、生命周期类型及可用方法。
   - **Schema**：
     ```json
     {
       "type": "object",
       "properties": {
         "filter": { "type": "string", "description": "服务名称或命名空间关键字过滤" }
       }
     }
     ```

7. `invoke_diagnostic_method`
   - **描述**：通过 IL Emit / 反射动态调用已注册服务的诊断方法并返回 JSON 结果。
   - **Schema**：
     ```json
     {
       "type": "object",
       "properties": {
         "service_type": { "type": "string" },
         "method_name": { "type": "string" },
         "arguments": { "type": "array" }
       },
       "required": ["service_type", "method_name"]
     }
     ```

8. `execute_diagnostic_command`
   - **描述**：在受限终端沙箱中执行单次命令行诊断指令并返回标准输出。
   - **Schema**：
     ```json
     {
       "type": "object",
       "properties": {
         "command": { "type": "string" },
         "timeout_seconds": { "type": "integer", "default": 30 }
       },
       "required": ["command"]
     }
     ```
