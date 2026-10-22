using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class OpenApiEndpoints
{
    public static RouteGroupBuilder MapOpenApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/openapi.json", (IOptions<TemporaryDiagnosticsOptions> options) =>
        {
            var prefix = options.Value.RoutePrefix;
            var json = $$"""
            {
              "openapi": "3.0.1",
              "info": {
                "title": "TemporaryDiagnostics Diagnostic API",
                "description": "C# 临时诊断中间件统一受控 REST 与 MCP 接口文档",
                "version": "1.0.0"
              },
              "servers": [
                {
                  "url": "{{prefix}}"
                }
              ],
              "paths": {
                "/api/auth/login": {
                  "post": {
                    "summary": "凭访问密钥登录",
                    "tags": ["Auth"]
                  }
                },
                "/api/auth/status": {
                  "get": {
                    "summary": "查询当前会话鉴权状态",
                    "tags": ["Auth"]
                  }
                },
                "/api/system/overview": {
                  "get": {
                    "summary": "获取操作系统与硬件概览",
                    "tags": ["System"]
                  }
                },
                "/api/system/vitals": {
                  "get": {
                    "summary": "获取 .NET 运行时健康体征 (GC/ThreadPool/进程工作集)",
                    "tags": ["System"]
                  }
                },
                "/api/system/environment": {
                  "get": {
                    "summary": "获取环境变量与配置项 (脱敏)",
                    "tags": ["System"]
                  }
                },
                "/api/system/assemblies": {
                  "get": {
                    "summary": "获取已加载程序集列表",
                    "tags": ["System"]
                  }
                },
                "/api/files/list": {
                  "get": {
                    "summary": "浏览受控白名单目录树",
                    "tags": ["Files"]
                  }
                },
                "/api/files/read": {
                  "get": {
                    "summary": "查看文本文件内容",
                    "tags": ["Files"]
                  }
                },
                "/api/files/save": {
                  "post": {
                    "summary": "保存文本文件修改",
                    "tags": ["Files"]
                  }
                },
                "/api/files/download": {
                  "get": {
                    "summary": "下载单文件或打包为 Zip 下载目录",
                    "tags": ["Files"]
                  }
                },
                "/api/invoke/services": {
                  "get": {
                    "summary": "探查 DI 容器注册服务",
                    "tags": ["DynamicInvoke"]
                  }
                },
                "/api/invoke/methods": {
                  "get": {
                    "summary": "获取服务的公共方法与参数描述",
                    "tags": ["DynamicInvoke"]
                  }
                },
                "/api/invoke/call": {
                  "post": {
                    "summary": "通过 Fast Emit 或反射动态执行方法",
                    "tags": ["DynamicInvoke"]
                  }
                },
                "/api/logs/recent": {
                  "get": {
                    "summary": "获取内存环形缓冲中的最近应用日志",
                    "tags": ["Logs"]
                  }
                },
                "/api/logs/requests": {
                  "get": {
                    "summary": "获取最近 HTTP 请求记录",
                    "tags": ["Logs"]
                  }
                },
                "/api/logs/exceptions": {
                  "get": {
                    "summary": "获取未捕获异常诊断详情",
                    "tags": ["Logs"]
                  }
                },
                "/mcp": {
                  "post": {
                    "summary": "MCP JSON-RPC 2.0 Streamable HTTP 端点",
                    "tags": ["MCP"]
                  }
                },
                "/mcp/tools": {
                  "get": {
                    "summary": "获取全部支持的 MCP 工具列表",
                    "tags": ["MCP"]
                  }
                }
              }
            }
            """;

            return Results.Content(json, "application/json");
        });

        // 嵌入式 Scalar 风格现代交互式 API 浏览器
        group.MapGet("/docs", (IOptions<TemporaryDiagnosticsOptions> options) =>
        {
            var prefix = options.Value.RoutePrefix;
            var html = $$"""
            <!doctype html>
            <html>
              <head>
                <title>TemporaryDiagnostics API Docs</title>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <style>
                  body { margin: 0; background-color: #0f172a; color: #f8fafc; font-family: ui-sans-serif, system-ui, sans-serif; }
                  .header { display: flex; align-items: center; justify-content: space-between; padding: 16px 24px; background: #1e293b; border-bottom: 1px solid #334155; }
                  .title { font-size: 1.25rem; font-weight: bold; color: #38bdf8; }
                  .btn { background: #0284c7; color: white; border: none; padding: 8px 16px; border-radius: 6px; text-decoration: none; font-size: 14px; }
                  .container { max-width: 1200px; margin: 24px auto; padding: 0 16px; }
                  .card { background: #1e293b; border-radius: 8px; padding: 20px; border: 1px solid #334155; margin-bottom: 16px; }
                  .badge { display: inline-block; padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 12px; margin-right: 8px; }
                  .badge-get { background: #065f46; color: #34d399; }
                  .badge-post { background: #1e40af; color: #60a5fa; }
                  .badge-delete { background: #991b1b; color: #f87171; }
                  .path { font-family: monospace; font-size: 15px; color: #e2e8f0; }
                  .desc { color: #94a3b8; font-size: 14px; margin-top: 6px; }
                </style>
              </head>
              <body>
                <div class="header">
                  <div class="title">⚡ TemporaryDiagnostics API 文档</div>
                  <div>
                    <a href="{{prefix}}" class="btn">返回可视化控制台</a>
                    <a href="{{prefix}}/openapi.json" target="_blank" class="btn" style="background:#475569;margin-left:8px;">OpenAPI JSON</a>
                  </div>
                </div>
                <div class="container">
                  <h3 style="color:#94a3b8;">核心受控诊断 REST 端点 (前缀: {{prefix}})</h3>
                  
                  <div class="card">
                    <span class="badge badge-post">POST</span> <span class="path">{{prefix}}/api/auth/login</span>
                    <div class="desc">凭访问密钥 (AccessKey) 换取 Session Token 与 Cookie</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/system/overview</span>
                    <div class="desc">获取系统硬件、操作系统、物理内存与磁盘分区</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/system/vitals</span>
                    <div class="desc">获取 .NET 运行时健康体征（GC 分代/堆内存/ThreadPool 状态/进程资源）</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/system/environment</span>
                    <div class="desc">获取环境变量与配置源（内置敏感字段自动脱敏）</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/files/list?path={path}</span>
                    <div class="desc">受控浏览白名单物理目录树</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/files/read?path={path}</span>
                    <div class="desc">在线只读查看文本配置或日志（2MB 自动截断防护）</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/files/download?path={path}&archive=true</span>
                    <div class="desc">单文件下载或将整个目录即时打包为 Zip 下载</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/invoke/services</span>
                    <div class="desc">探查当前 Web 应用 IServiceProvider 中所有已注册的服务类型</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-post">POST</span> <span class="path">{{prefix}}/api/invoke/call</span>
                    <div class="desc">基于 IL Emit 编译委托或反射动态执行公共诊断方法并返回 JSON</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/logs/recent</span>
                    <div class="desc">获取内存环形缓冲中的最近应用日志（零 GC 写入）</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-get">GET</span> <span class="path">{{prefix}}/api/logs/requests</span>
                    <div class="desc">获取最近 HTTP 历史请求耗时与状态码（类似 AspNetDebugDashboard）</div>
                  </div>

                  <div class="card">
                    <span class="badge badge-post">POST</span> <span class="path">{{prefix}}/mcp</span>
                    <div class="desc">原生 Model Context Protocol (MCP) Streamable HTTP JSON-RPC 2.0 端点</div>
                  </div>
                </div>
              </body>
            </html>
            """;

            return Results.Content(html, "text/html");
        });

        return group;
    }
}
