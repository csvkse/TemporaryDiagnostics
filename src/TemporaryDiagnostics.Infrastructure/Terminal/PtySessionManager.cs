using System.Collections.Concurrent;
using TemporaryDiagnostics.Core.Contracts;

namespace TemporaryDiagnostics.Infrastructure.Terminal;

/// <summary>
/// 终端会话管理器，负责会话查找、注册与主动关闭。
/// </summary>
public sealed class PtySessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IPtySession> _sessions = new();
    private readonly Timer _cleanupTimer;

    public PtySessionManager()
    {
        // 每 30 秒轮询清理已经终止或退出的死会话
        _cleanupTimer = new Timer(CleanupDeadSessions, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void RegisterSession(IPtySession session)
    {
        _sessions[session.Id] = session;
    }

    public IPtySession? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public async Task<bool> CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.TerminateAsync(cancellationToken);
            await session.DisposeAsync();
            return true;
        }
        return false;
    }

    public IReadOnlyList<IPtySession> ListActiveSessions()
    {
        var active = new List<IPtySession>();
        foreach (var (id, session) in _sessions)
        {
            if (session.IsActive)
            {
                active.Add(session);
            }
            else
            {
                // 惰性移除已失效的死会话，避免在重连时误占并发配额
                if (_sessions.TryRemove(id, out var deadSession))
                {
                    _ = deadSession.DisposeAsync();
                }
            }
        }
        return active;
    }

    private void CleanupDeadSessions(object? state)
    {
        foreach (var (id, session) in _sessions)
        {
            if (!session.IsActive)
            {
                if (_sessions.TryRemove(id, out var deadSession))
                {
                    _ = deadSession.DisposeAsync();
                }
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var session in _sessions.Values)
        {
            _ = session.DisposeAsync();
        }
        _sessions.Clear();
    }
}
