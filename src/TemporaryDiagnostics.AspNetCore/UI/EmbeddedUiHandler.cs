using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;

namespace TemporaryDiagnostics.AspNetCore.UI;

/// <summary>
/// 零 CDN、完全内嵌式的单页面可视化诊断控制台 HTML 处理器。
/// 离线 100% 可用，无任何外部网络拉取依赖。
/// </summary>
public static class EmbeddedUiHandler
{
    public static RouteGroupBuilder MapDiagnosticUi(this RouteGroupBuilder group, string prefix)
    {
        group.MapGet("/", (HttpContext context, IOptions<TemporaryDiagnosticsOptions> options) =>
        {
            var html = RenderDashboardHtml(options.Value.RoutePrefix);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        group.MapGet("/index.html", (HttpContext context, IOptions<TemporaryDiagnosticsOptions> options) =>
        {
            var html = RenderDashboardHtml(options.Value.RoutePrefix);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        return group;
    }

    private static string RenderDashboardHtml(string prefix) => $$"""
    <!DOCTYPE html>
    <html lang="zh-CN">
    <head>
      <meta charset="UTF-8">
      <meta name="viewport" content="width=device-width, initial-scale=1.0">
      <title>TemporaryDiagnostics 临时诊断控制台</title>
      <style>
        :root {
          --bg-main: #0b0f19;
          --bg-card: #151d30;
          --bg-hover: #1e2942;
          --border: #23314f;
          --primary: #38bdf8;
          --primary-hover: #0284c7;
          --text-main: #f1f5f9;
          --text-muted: #94a3b8;
          --success: #10b981;
          --warning: #f59e0b;
          --danger: #ef4444;
          --font-mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { background: var(--bg-main); color: var(--text-main); font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; font-size: 14px; line-height: 1.5; }
        
        /* 导航栏 */
        .navbar { display: flex; align-items: center; justify-content: space-between; padding: 12px 24px; background: #0e1526; border-bottom: 1px solid var(--border); position: sticky; top: 0; z-index: 100; }
        .logo { font-size: 1.15rem; font-weight: 700; color: var(--primary); display: flex; align-items: center; gap: 8px; }
        .nav-links { display: flex; gap: 4px; }
        .nav-tab { padding: 6px 14px; border-radius: 6px; cursor: pointer; color: var(--text-muted); font-weight: 500; transition: all 0.2s; border: none; background: transparent; }
        .nav-tab:hover { color: var(--text-main); background: var(--bg-hover); }
        .nav-tab.active { color: white; background: var(--primary-hover); }
        
        .nav-actions { display: flex; align-items: center; gap: 12px; }
        .btn { padding: 6px 12px; border-radius: 6px; font-size: 13px; font-weight: 500; cursor: pointer; border: 1px solid transparent; transition: all 0.2s; }
        .btn-primary { background: var(--primary); color: #000; font-weight: 600; }
        .btn-primary:hover { background: #7dd3fc; }
        .btn-outline { background: transparent; border-color: var(--border); color: var(--text-main); }
        .btn-outline:hover { background: var(--bg-hover); }
        .btn-danger { background: var(--danger); color: white; }
        
        /* 主布局 */
        .main-container { max-width: 1400px; margin: 20px auto; padding: 0 20px; }
        .grid-cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; margin-bottom: 20px; }
        .card { background: var(--bg-card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; position: relative; }
        .card-title { font-size: 12px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; margin-bottom: 8px; }
        .card-value { font-size: 1.8rem; font-weight: 700; color: var(--text-main); font-family: var(--font-mono); }
        .card-desc { font-size: 12px; color: var(--text-muted); margin-top: 6px; }
        
        /* 标签页视图 */
        .tab-view { display: none; }
        .tab-view.active { display: block; }
        
        /* 表格 */
        .table-container { overflow-x: auto; border: 1px solid var(--border); border-radius: 8px; background: var(--bg-card); }
        table { width: 100%; border-collapse: collapse; text-align: left; }
        th, td { padding: 10px 14px; border-bottom: 1px solid var(--border); }
        th { background: #0e1526; font-size: 12px; font-weight: 600; color: var(--text-muted); }
        tr:hover td { background: var(--bg-hover); }
        
        /* 徽章 */
        .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 600; font-family: var(--font-mono); }
        .badge-green { background: #064e3b; color: #34d399; }
        .badge-blue { background: #1e3a8a; color: #60a5fa; }
        .badge-yellow { background: #78350f; color: #fbbf24; }
        .badge-red { background: #7f1d1d; color: #f87171; }
        
        /* 终端界面 */
        .terminal-container { background: #000; border: 1px solid var(--border); border-radius: 8px; height: 560px; display: flex; flex-direction: column; overflow: hidden; }
        .terminal-header { background: #1e293b; padding: 8px 16px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--border); }
        .terminal-screen { flex: 1; padding: 12px; font-family: var(--font-mono); font-size: 13px; line-height: 1.4; color: #38bdf8; overflow-y: auto; white-space: pre-wrap; word-break: break-all; }
        .terminal-input-bar { display: flex; background: #090d16; border-top: 1px solid var(--border); padding: 8px 12px; align-items: center; }
        .terminal-input { flex: 1; background: transparent; border: none; color: #fff; font-family: var(--font-mono); font-size: 14px; outline: none; margin-left: 8px; }
        
        /* 弹窗与编辑器 */
        .modal-mask { position: fixed; inset: 0; background: rgba(0,0,0,0.7); display: none; align-items: center; justify-content: center; z-index: 1000; }
        .modal-mask.show { display: flex; }
        .modal-body { background: var(--bg-card); border: 1px solid var(--border); border-radius: 8px; width: 800px; max-width: 90vw; max-height: 85vh; display: flex; flex-direction: column; padding: 20px; }
        .modal-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; border-bottom: 1px solid var(--border); padding-bottom: 10px; }
        .modal-content { flex: 1; overflow-y: auto; }
        
        textarea.code-editor { width: 100%; height: 360px; background: #080c14; border: 1px solid var(--border); border-radius: 6px; color: #38bdf8; font-family: var(--font-mono); font-size: 13px; padding: 10px; outline: none; resize: vertical; }
        input.text-input { background: #080c14; border: 1px solid var(--border); border-radius: 6px; color: #fff; padding: 8px 12px; font-size: 13px; outline: none; width: 100%; }
        
        /* 登录遮罩 */
        .login-screen { position: fixed; inset: 0; background: var(--bg-main); display: flex; align-items: center; justify-content: center; z-index: 999; }
        .login-box { width: 380px; background: var(--bg-card); border: 1px solid var(--border); border-radius: 12px; padding: 32px; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5); text-align: center; }
        .login-logo { font-size: 2rem; margin-bottom: 8px; }
      </style>
    </head>
    <body>
      <!-- 登录遮罩 -->
      <div id="loginScreen" class="login-screen">
        <div class="login-box">
          <div class="login-logo">⚡</div>
          <h2 style="margin-bottom:8px;">临时诊断中间件</h2>
          <p style="color:var(--text-muted);font-size:13px;margin-bottom:24px;">请输入后端配置的受控访问密钥 (AccessKey)</p>
          <input id="keyInput" type="password" class="text-input" placeholder="输入访问密钥..." style="margin-bottom:16px;text-align:center;font-size:15px;letter-spacing:1px;" />
          <button id="loginBtn" class="btn btn-primary" style="width:100%;padding:10px;" onclick="doLogin()">进入诊断控制台</button>
          <div id="loginError" style="color:var(--danger);font-size:12px;margin-top:12px;display:none;"></div>
        </div>
      </div>

      <!-- 顶部导航 -->
      <header class="navbar">
        <div class="logo">⚡ TemporaryDiagnostics</div>
        <nav class="nav-links">
          <button class="nav-tab active" onclick="switchTab('dashboard')">仪表盘总览</button>
          <button class="nav-tab" onclick="switchTab('terminal')">交互终端</button>
          <button class="nav-tab" onclick="switchTab('files')">安全文件管理</button>
          <button class="nav-tab" onclick="switchTab('invoke')">动态反射与 Emit</button>
          <button class="nav-tab" onclick="switchTab('logs')">日志与请求</button>
          <button class="nav-tab" onclick="switchTab('mcp')">AI MCP 智能体</button>
        </nav>
        <div class="nav-actions">
          <a href="{{prefix}}/docs" target="_blank" class="btn btn-outline">API 文档</a>
          <button class="btn btn-danger" onclick="doLogout()">退出会话</button>
        </div>
      </header>

      <main class="main-container">
        <!-- 1. 仪表盘 -->
        <section id="tab-dashboard" class="tab-view active">
          <div class="grid-cards">
            <div class="card">
              <div class="card-title">进程 CPU 使用率</div>
              <div id="cardCpu" class="card-value">0.0%</div>
              <div id="cardThreads" class="card-desc">0 线程 | 0 句柄</div>
            </div>
            <div class="card">
              <div class="card-title">工作集物理内存</div>
              <div id="cardMem" class="card-value">0 MB</div>
              <div id="cardPrivateMem" class="card-desc">私有字节: 0 MB</div>
            </div>
            <div class="card">
              <div class="card-title">.NET GC 堆内存</div>
              <div id="cardGc" class="card-value">0 MB</div>
              <div id="cardGcGen" class="card-desc">Gen0: 0 | Gen1: 0 | Gen2: 0</div>
            </div>
            <div class="card">
              <div class="card-title">运行运行时长</div>
              <div id="cardUptime" class="card-value">00:00:00</div>
              <div id="cardFramework" class="card-desc">.NET Core</div>
            </div>
          </div>

          <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:20px;">
            <div class="card">
              <div class="card-title">宿主操作系统与硬件全景</div>
              <div style="margin-top:12px;font-family:var(--font-mono);font-size:13px;line-height:2;">
                <div>主机名称: <span id="sysMachine" style="color:var(--primary);">-</span></div>
                <div>系统平台: <span id="sysOs">-</span></div>
                <div>CPU 拓扑: <span id="sysCpus">-</span> 核心</div>
                <div>物理总内存: <span id="sysTotalMem">-</span></div>
                <div>可用物理内存: <span id="sysAvailMem" style="color:var(--success);">-</span></div>
              </div>
            </div>
            <div class="card">
              <div class="card-title">磁盘挂载分区概览</div>
              <div class="table-container" style="max-height:220px;margin-top:10px;">
                <table id="diskTable">
                  <thead><tr><th>盘符/挂载点</th><th>格式</th><th>总空间</th><th>空闲空间</th><th>使用率</th></tr></thead>
                  <tbody></tbody>
                </table>
              </div>
            </div>
          </div>
        </section>

        <!-- 2. 交互式终端 -->
        <section id="tab-terminal" class="tab-view">
          <div class="terminal-container">
            <div class="terminal-header">
              <span style="font-family:var(--font-mono);font-size:13px;color:var(--text-muted);">Web Shell (ConPTY / PTY 模式)</span>
              <div>
                <button class="btn btn-outline" style="padding:4px 8px;" onclick="reconnectTerminal()">重新连接</button>
                <button class="btn btn-outline" style="padding:4px 8px;margin-left:6px;" onclick="clearTerminal()">清屏</button>
              </div>
            </div>
            <div id="terminalScreen" class="terminal-screen">正在准备终端连接...</div>
            <div class="terminal-input-bar">
              <span style="color:var(--success);font-family:var(--font-mono);">$</span>
              <input id="termInput" type="text" class="terminal-input" placeholder="输入命令后回车 (如 dir, ls, dotnet --info)..." onkeydown="handleTermKey(event)" />
            </div>
          </div>
        </section>

        <!-- 3. 安全文件管理 -->
        <section id="tab-files" class="tab-view">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
            <div style="display:flex;align-items:center;gap:8px;font-family:var(--font-mono);">
              <span style="color:var(--text-muted);">当前路径:</span>
              <span id="currentFilePath" style="color:var(--primary);font-weight:600;">/</span>
            </div>
            <div style="display:flex;gap:8px;">
              <button class="btn btn-outline" onclick="loadFiles(parentFilePath)">返回上级</button>
              <button class="btn btn-outline" onclick="loadFiles(currentFilePathStr)">刷新</button>
              <button class="btn btn-outline" onclick="downloadCurrentFolderZip()">打包 Zip 下载</button>
            </div>
          </div>

          <div class="table-container">
            <table id="fileTable">
              <thead><tr><th>名称</th><th>类型</th><th>大小</th><th>修改时间</th><th>操作</th></tr></thead>
              <tbody></tbody>
            </table>
          </div>
        </section>

        <!-- 4. 动态反射与 Emit 调用 -->
        <section id="tab-invoke" class="tab-view">
          <div style="display:grid;grid-template-columns:360px 1fr;gap:16px;">
            <div class="card" style="height:650px;display:flex;flex-direction:column;">
              <div class="card-title">DI 服务容器探查</div>
              <input id="serviceFilter" class="text-input" placeholder="搜索服务名称..." oninput="filterServices()" style="margin:8px 0;" />
              <div id="serviceList" style="flex:1;overflow-y:auto;border:1px solid var(--border);border-radius:6px;padding:4px;"></div>
            </div>

            <div class="card" style="height:650px;display:flex;flex-direction:column;">
              <div class="card-title">公共方法执行与 Fast Emit 动态调用</div>
              <div id="invokeTargetInfo" style="margin:8px 0;font-family:var(--font-mono);color:var(--primary);">请在左侧选择一个服务进行探查</div>

              <div id="methodListContainer" style="max-height:220px;overflow-y:auto;border:1px solid var(--border);border-radius:6px;margin-bottom:12px;">
                <table id="methodTable">
                  <thead><tr><th>方法名</th><th>返回类型</th><th>异步</th><th>入参</th><th>操作</th></tr></thead>
                  <tbody></tbody>
                </table>
              </div>

              <div style="flex:1;display:flex;flex-direction:column;border-top:1px solid var(--border);padding-top:10px;">
                <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;">
                  <span style="font-weight:600;">调用入参 (JSON 数组)</span>
                  <label style="font-size:12px;color:var(--primary);cursor:pointer;">
                    <input type="checkbox" id="chkFastEmit" checked /> 启用 IL Emit 高性能动态编译
                  </label>
                </div>
                <input id="invokeArgsInput" class="text-input" placeholder='[ "arg1", 100 ] 或留空' style="margin-bottom:10px;" />
                <button id="btnExecInvoke" class="btn btn-primary" onclick="executeMethodCall()" style="margin-bottom:10px;">执行动态调用 (Invoke)</button>
                <div style="font-weight:600;margin-bottom:4px;">执行结果与诊断输出</div>
                <textarea id="invokeOutput" class="code-editor" style="flex:1;height:120px;" readonly placeholder="等待执行..."></textarea>
              </div>
            </div>
          </div>
        </section>

        <!-- 5. 日志与请求 -->
        <section id="tab-logs" class="tab-view">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
            <div style="display:flex;gap:8px;">
              <button id="btnLogModeRecent" class="btn btn-primary" onclick="switchLogMode('recent')">应用运行日志</button>
              <button id="btnLogModeRequests" class="btn btn-outline" onclick="switchLogMode('requests')">最近 HTTP 请求</button>
              <button id="btnLogModeExceptions" class="btn btn-outline" onclick="switchLogMode('exceptions')">未捕获异常</button>
            </div>
            <button class="btn btn-outline" onclick="clearLogs()">清空缓冲区</button>
          </div>

          <div class="table-container" style="max-height:600px;">
            <table id="logTable">
              <thead id="logThead"></thead>
              <tbody id="logTbody"></tbody>
            </table>
          </div>
        </section>

        <!-- 6. AI MCP 智能体 -->
        <section id="tab-mcp" class="tab-view">
          <div class="card" style="margin-bottom:16px;">
            <div class="card-title">Model Context Protocol (MCP) 端点状态</div>
            <div style="margin-top:8px;font-family:var(--font-mono);font-size:13px;line-height:1.8;">
              <div>Streamable HTTP 接口: <span style="color:var(--primary);">POST {{prefix}}/mcp</span></div>
              <div>SSE 下行推送通道: <span style="color:var(--primary);">GET {{prefix}}/mcp/sse</span></div>
              <div>协议规范版本: <span class="badge badge-green">2024-11-05</span></div>
            </div>
          </div>

          <div class="card">
            <div class="card-title">可用 MCP 工具能力集 (Tools)</div>
            <div class="table-container" style="margin-top:10px;">
              <table id="mcpToolTable">
                <thead><tr><th>工具名称</th><th>功能说明</th><th>Input Schema</th><th>操作</th></tr></thead>
                <tbody></tbody>
              </table>
            </div>
          </div>
        </section>
      </main>

      <!-- 文件编辑弹窗 -->
      <div id="fileEditModal" class="modal-mask">
        <div class="modal-body">
          <div class="modal-header">
            <h3 id="editModalTitle">文本预览与编辑</h3>
            <button class="btn btn-outline" onclick="closeEditModal()">✕</button>
          </div>
          <div class="modal-content">
            <textarea id="fileEditorContent" class="code-editor"></textarea>
          </div>
          <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:12px;">
            <button class="btn btn-outline" onclick="closeEditModal()">取消</button>
            <button class="btn btn-primary" onclick="saveFileContent()">保存文件</button>
          </div>
        </div>
      </div>

      <script>
        const API_PREFIX = '{{prefix}}';
        let currentTab = 'dashboard';
        let currentFilePathStr = '/';
        let parentFilePath = null;
        let ws = null;
        let currentEditingPath = '';
        let servicesCache = [];
        let selectedServiceType = '';
        let selectedMethodName = '';

        // 页面初始化
        window.addEventListener('load', async () => {
          await checkAuth();
          startVitalsPolling();
        });

        // 鉴权检查
        async function checkAuth() {
          try {
            const res = await fetch(`${API_PREFIX}/api/auth/status`);
            const data = await res.json();
            if (data.isAuthenticated) {
              document.getElementById('loginScreen').style.display = 'none';
              loadDashboardData();
            } else {
              document.getElementById('loginScreen').style.display = 'flex';
            }
          } catch (e) {
            document.getElementById('loginScreen').style.display = 'flex';
          }
        }

        async function doLogin() {
          const key = document.getElementById('keyInput').value;
          const errDiv = document.getElementById('loginError');
          errDiv.style.display = 'none';

          try {
            const res = await fetch(`${API_PREFIX}/api/auth/login`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ accessKey: key })
            });

            if (res.ok) {
              document.getElementById('loginScreen').style.display = 'none';
              loadDashboardData();
            } else {
              const err = await res.json();
              errDiv.innerText = err.error || '访问密钥错误';
              errDiv.style.display = 'block';
            }
          } catch (e) {
            errDiv.innerText = '无法连接到后端诊断中间件';
            errDiv.style.display = 'block';
          }
        }

        async function doLogout() {
          await fetch(`${API_PREFIX}/api/auth/logout`, { method: 'POST' });
          location.reload();
        }

        // 标签切换
        function switchTab(tab) {
          currentTab = tab;
          document.querySelectorAll('.nav-tab').forEach(t => t.classList.remove('active'));
          document.querySelectorAll('.tab-view').forEach(v => v.classList.remove('active'));

          event.target.classList.add('active');
          document.getElementById(`tab-${tab}`).classList.add('active');

          if (tab === 'terminal' && !ws) initTerminal();
          if (tab === 'files') loadFiles(currentFilePathStr);
          if (tab === 'invoke') loadServices();
          if (tab === 'logs') switchLogMode('recent');
          if (tab === 'mcp') loadMcpTools();
        }

        // 1. 仪表盘指标轮询
        async function loadDashboardData() {
          try {
            const [vitalsRes, sysRes] = await Promise.all([
              fetch(`${API_PREFIX}/api/system/vitals`),
              fetch(`${API_PREFIX}/api/system/overview`)
            ]);

            if (vitalsRes.ok) {
              const v = await vitalsRes.json();
              document.getElementById('cardCpu').innerText = `${v.cpuPercent}%`;
              document.getElementById('cardThreads').innerText = `${v.threadCount} 线程 | ${v.handleCount} 句柄`;
              document.getElementById('cardMem').innerText = `${(v.workingSetBytes / 1048576).toFixed(1)} MB`;
              document.getElementById('cardPrivateMem').innerText = `私有内存: ${(v.privateMemoryBytes / 1048576).toFixed(1)} MB`;
              document.getElementById('cardGc').innerText = `${(v.heapSizeBytes / 1048576).toFixed(1)} MB`;
              document.getElementById('cardGcGen').innerText = `Gen0:${v.gen0Collections} Gen1:${v.gen1Collections} Gen2:${v.gen2Collections}`;
              document.getElementById('cardUptime').innerText = v.processUptime.split('.')[0] || v.processUptime;
            }

            if (sysRes.ok) {
              const s = await sysRes.json();
              document.getElementById('sysMachine').innerText = s.machineName;
              document.getElementById('sysOs').innerText = s.osDescription;
              document.getElementById('sysCpus').innerText = s.processorCount;
              document.getElementById('sysTotalMem').innerText = `${(s.totalPhysicalMemoryBytes / 1073741824).toFixed(2)} GB`;
              document.getElementById('sysAvailMem').innerText = `${(s.availablePhysicalMemoryBytes / 1073741824).toFixed(2)} GB (${(100 - s.memoryUsagePercent).toFixed(1)}% 可用)`;

              const diskTbody = document.querySelector('#diskTable tbody');
              diskTbody.innerHTML = '';
              s.drives.forEach(d => {
                const tr = document.createElement('tr');
                tr.innerHTML = `
                  <td style="font-family:var(--font-mono);font-weight:600;">${d.name}</td>
                  <td>${d.fileSystem || 'NTFS'}</td>
                  <td>${(d.totalSizeBytes / 1073741824).toFixed(1)} GB</td>
                  <td>${(d.freeSizeBytes / 1073741824).toFixed(1)} GB</td>
                  <td><span class="badge ${d.usagePercent > 85 ? 'badge-red' : 'badge-green'}">${d.usagePercent}%</span></td>
                `;
                diskTbody.appendChild(tr);
              });
            }
          } catch (e) { }
        }

        function startVitalsPolling() {
          setInterval(() => {
            if (currentTab === 'dashboard') loadDashboardData();
          }, 2000);
        }

        // 2. 终端 WebSocket 交互
        function initTerminal() {
          const screen = document.getElementById('terminalScreen');
          screen.innerHTML = '正在建立 PTY 终端 WebSocket 连接...\r\n';

          const loc = window.location;
          const proto = loc.protocol === 'https:' ? 'wss:' : 'ws:';
          const wsUrl = `${proto}//${loc.host}${API_PREFIX}/api/terminal/ws?cols=120&rows=32`;

          ws = new WebSocket(wsUrl);
          ws.binaryType = 'arraybuffer';

          ws.onopen = () => {
            screen.innerHTML = '\x1b[32m[PTY Ready] 交互终端已就绪。输入命令即可执行。\x1b[0m\r\n\r\n';
          };

          ws.onmessage = (event) => {
            if (event.data instanceof ArrayBuffer) {
              const decoder = new TextDecoder('utf-8');
              const text = decoder.decode(event.data);
              appendTerminalText(text);
            } else {
              appendTerminalText(event.data);
            }
          };

          ws.onclose = () => {
            appendTerminalText('\r\n\x1b[33m[PTY Closed] 终端会话已断开。\x1b[0m\r\n');
            ws = null;
          };

          ws.onerror = () => {
            appendTerminalText('\r\n\x1b[31m[PTY Error] 终端通信异常。\x1b[0m\r\n');
          };
        }

        function appendTerminalText(text) {
          const screen = document.getElementById('terminalScreen');
          screen.innerText += text;
          screen.scrollTop = screen.scrollHeight;
        }

        function handleTermKey(e) {
          if (e.key === 'Enter') {
            const input = document.getElementById('termInput');
            const cmd = input.value;
            input.value = '';
            if (ws && ws.readyState === WebSocket.OPEN) {
              const encoder = new TextEncoder();
              ws.send(encoder.encode(cmd + '\r\n'));
            }
          }
        }

        function reconnectTerminal() {
          if (ws) ws.close();
          initTerminal();
        }

        function clearTerminal() {
          document.getElementById('terminalScreen').innerText = '';
        }

        // 3. 文件管理
        async function loadFiles(path) {
          try {
            const url = path ? `${API_PREFIX}/api/files/list?path=${encodeURIComponent(path)}` : `${API_PREFIX}/api/files/list`;
            const res = await fetch(url);
            if (!res.ok) return;

            const data = await res.json();
            currentFilePathStr = data.currentPath;
            parentFilePath = data.parentPath;
            document.getElementById('currentFilePath').innerText = data.currentPath;

            const tbody = document.querySelector('#fileTable tbody');
            tbody.innerHTML = '';

            data.items.forEach(item => {
              const tr = document.createElement('tr');
              const icon = item.isDirectory ? '📁' : '📄';
              const size = item.isDirectory ? '-' : formatBytes(item.sizeBytes);
              tr.innerHTML = `
                <td style="font-family:var(--font-mono);cursor:pointer;" onclick="${item.isDirectory ? `loadFiles('${item.fullPath}')` : `openFileEditor('${item.fullPath}')`}">
                  ${icon} ${item.name}
                </td>
                <td><span class="badge ${item.isDirectory ? 'badge-blue' : 'badge-green'}">${item.isDirectory ? '目录' : '文件'}</span></td>
                <td style="font-family:var(--font-mono);">${size}</td>
                <td>${item.lastModified ? new Date(item.lastModified).toLocaleString() : '-'}</td>
                <td>
                  ${!item.isDirectory ? `<button class="btn btn-outline" style="padding:2px 8px;" onclick="downloadSingleFile('${item.fullPath}')">下载</button>` : ''}
                </td>
              `;
              tbody.appendChild(tr);
            });
          } catch (e) { }
        }

        async function openFileEditor(path) {
          currentEditingPath = path;
          document.getElementById('editModalTitle').innerText = `编辑: ${path}`;
          const res = await fetch(`${API_PREFIX}/api/files/read?path=${encodeURIComponent(path)}`);
          if (res.ok) {
            const data = await res.json();
            document.getElementById('fileEditorContent').value = data.content;
            document.getElementById('fileEditModal').classList.add('show');
          }
        }

        function closeEditModal() {
          document.getElementById('fileEditModal').classList.remove('show');
        }

        async function saveFileContent() {
          const content = document.getElementById('fileEditorContent').value;
          const res = await fetch(`${API_PREFIX}/api/files/save`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fullPath: currentEditingPath, content })
          });
          if (res.ok) {
            alert('保存成功！');
            closeEditModal();
          } else {
            alert('保存失败');
          }
        }

        function downloadSingleFile(path) {
          window.open(`${API_PREFIX}/api/files/download?path=${encodeURIComponent(path)}`);
        }

        function downloadCurrentFolderZip() {
          window.open(`${API_PREFIX}/api/files/download?path=${encodeURIComponent(currentFilePathStr)}&archive=true`);
        }

        function formatBytes(bytes) {
          if (!bytes || bytes === 0) return '0 B';
          const k = 1024;
          const sizes = ['B', 'KB', 'MB', 'GB'];
          const i = Math.floor(Math.log(bytes) / Math.log(k));
          return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
        }

        // 4. 动态反射与 Emit
        async function loadServices() {
          const res = await fetch(`${API_PREFIX}/api/invoke/services`);
          if (!res.ok) return;
          servicesCache = await res.json();
          renderServiceList(servicesCache);
        }

        function filterServices() {
          const q = document.getElementById('serviceFilter').value.toLowerCase();
          const filtered = servicesCache.filter(s => s.serviceType.toLowerCase().includes(q) || s.fullName.toLowerCase().includes(q));
          renderServiceList(filtered);
        }

        function renderServiceList(list) {
          const box = document.getElementById('serviceList');
          box.innerHTML = '';
          list.forEach(s => {
            const item = document.createElement('div');
            item.style.padding = '8px';
            item.style.borderBottom = '1px solid var(--border)';
            item.style.cursor = 'pointer';
            item.innerHTML = `
              <div style="font-weight:600;color:var(--primary);font-size:13px;">${s.serviceType}</div>
              <div style="font-size:11px;color:var(--text-muted);">${s.lifetime} | ${s.implementationType}</div>
            `;
            item.onclick = () => selectService(s.fullName);
            box.appendChild(item);
          });
        }

        async function selectService(serviceFullName) {
          selectedServiceType = serviceFullName;
          document.getElementById('invokeTargetInfo').innerText = `已选服务: ${serviceFullName}`;
          const res = await fetch(`${API_PREFIX}/api/invoke/methods?serviceTypeName=${encodeURIComponent(serviceFullName)}`);
          if (!res.ok) return;

          const methods = await res.json();
          const tbody = document.querySelector('#methodTable tbody');
          tbody.innerHTML = '';

          methods.forEach(m => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
              <td style="font-family:var(--font-mono);font-weight:600;">${m.name}</td>
              <td style="font-family:var(--font-mono);color:var(--text-muted);">${m.returnTypeName}</td>
              <td>${m.isAsync ? '<span class="badge badge-blue">Async</span>' : '-'}</td>
              <td style="font-size:12px;">${m.parameters.map(p => `${p.typeName} ${p.name}`).join(', ') || '无'}</td>
              <td><button class="btn btn-outline" style="padding:2px 8px;" onclick="chooseMethod('${m.name}')">选定</button></td>
            `;
            tbody.appendChild(tr);
          });
        }

        function chooseMethod(methodName) {
          selectedMethodName = methodName;
          document.getElementById('invokeTargetInfo').innerText = `已选目标: ${selectedServiceType} -> ${methodName}()`;
        }

        async function executeMethodCall() {
          if (!selectedServiceType || !selectedMethodName) {
            alert('请先选择服务和目标方法！');
            return;
          }

          const argsStr = document.getElementById('invokeArgsInput').value.trim();
          let parsedArgs = [];
          if (argsStr) {
            try { parsedArgs = JSON.parse(argsStr); } catch (e) { alert('参数必须为有效 JSON 数组，如 [10, "test"]'); return; }
          }

          const useFastEmit = document.getElementById('chkFastEmit').checked;
          const outputBox = document.getElementById('invokeOutput');
          outputBox.value = '正在执行调用...';

          try {
            const res = await fetch(`${API_PREFIX}/api/invoke/call`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                serviceTypeName: selectedServiceType,
                methodName: selectedMethodName,
                parameters: parsedArgs,
                useFastEmit
              })
            });

            const data = await res.json();
            outputBox.value = JSON.stringify(data, null, 2);
          } catch (e) {
            outputBox.value = `调用异常: ${e.message}`;
          }
        }

        // 5. 日志与 HTTP 历史
        async function switchLogMode(mode) {
          const thead = document.getElementById('logThead');
          const tbody = document.getElementById('logTbody');
          tbody.innerHTML = '';

          if (mode === 'recent') {
            thead.innerHTML = '<tr><th>时间</th><th>级别</th><th>分类</th><th>消息</th></tr>';
            const res = await fetch(`${API_PREFIX}/api/logs/recent?limit=100`);
            const logs = await res.json();
            logs.forEach(l => {
              const tr = document.createElement('tr');
              tr.innerHTML = `
                <td style="font-family:var(--font-mono);font-size:12px;">${new Date(l.timestamp).toLocaleTimeString()}</td>
                <td><span class="badge ${l.logLevel === 'Error' ? 'badge-red' : 'badge-blue'}">${l.logLevel}</span></td>
                <td style="font-family:var(--font-mono);color:var(--text-muted);font-size:12px;">${l.category}</td>
                <td>${l.message}</td>
              `;
              tbody.appendChild(tr);
            });
          } else if (mode === 'requests') {
            thead.innerHTML = '<tr><th>时间</th><th>方法</th><th>路径</th><th>状态码</th><th>耗时</th><th>客户端 IP</th></tr>';
            const res = await fetch(`${API_PREFIX}/api/logs/requests?limit=100`);
            const requests = await res.json();
            requests.forEach(r => {
              const tr = document.createElement('tr');
              tr.innerHTML = `
                <td style="font-family:var(--font-mono);font-size:12px;">${new Date(r.timestamp).toLocaleTimeString()}</td>
                <td><span class="badge badge-blue">${r.method}</span></td>
                <td style="font-family:var(--font-mono);">${r.path}</td>
                <td><span class="badge ${r.statusCode >= 400 ? 'badge-red' : 'badge-green'}">${r.statusCode}</span></td>
                <td>${r.durationMs} ms</td>
                <td>${r.clientIp || '-'}</td>
              `;
              tbody.appendChild(tr);
            });
          } else if (mode === 'exceptions') {
            thead.innerHTML = '<tr><th>时间</th><th>异常类型</th><th>错误信息</th><th>请求路径</th></tr>';
            const res = await fetch(`${API_PREFIX}/api/logs/exceptions?limit=100`);
            const exList = await res.json();
            exList.forEach(e => {
              const tr = document.createElement('tr');
              tr.innerHTML = `
                <td style="font-family:var(--font-mono);font-size:12px;">${new Date(e.timestamp).toLocaleTimeString()}</td>
                <td style="color:var(--danger);font-weight:600;">${e.exceptionType}</td>
                <td>${e.message}</td>
                <td>${e.requestPath || '-'}</td>
              `;
              tbody.appendChild(tr);
            });
          }
        }

        async function clearLogs() {
          await fetch(`${API_PREFIX}/api/logs/clear`, { method: 'POST' });
          switchLogMode('recent');
        }

        // 6. MCP 工具集
        async function loadMcpTools() {
          const res = await fetch(`${API_PREFIX}/mcp/tools`);
          if (!res.ok) return;

          const data = await res.json();
          const tbody = document.querySelector('#mcpToolTable tbody');
          tbody.innerHTML = '';

          data.tools.forEach(t => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
              <td style="font-family:var(--font-mono);color:var(--primary);font-weight:600;">${t.name}</td>
              <td>${t.description}</td>
              <td style="font-family:var(--font-mono);font-size:11px;color:var(--text-muted);">${JSON.stringify(t.inputSchema)}</td>
              <td><button class="btn btn-outline" style="padding:2px 8px;" onclick="testMcpTool('${t.name}')">测试调用</button></td>
            `;
            tbody.appendChild(tr);
          });
        }

        async function testMcpTool(toolName) {
          const rpcBody = {
            jsonrpc: "2.0",
            id: 1,
            method: "tools/call",
            params: {
              name: toolName,
              arguments: {}
            }
          };

          const res = await fetch(`${API_PREFIX}/mcp`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(rpcBody)
          });

          const data = await res.json();
          alert(`[MCP Response]\n${JSON.stringify(data, null, 2)}`);
        }
      </script>
    </body>
    </html>
    """;
}
