using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.StateMachines;

namespace TemporaryDiagnostics.AspNetCore.Security;

/// <summary>
/// 诊断中间件内存会话与凭证存储。
/// </summary>
public sealed class DiagnosticSessionStore
{
    private readonly TemporaryDiagnosticsOptions _options;
    private readonly ConcurrentDictionary<string, SessionLifecycleStateMachine> _sessions = new();
    private readonly ConcurrentDictionary<string, (int Attempts, DateTime LockUntil)> _failedAttempts = new();
    private readonly object _lock = new();

    public DiagnosticSessionStore(IOptions<TemporaryDiagnosticsOptions> options)
    {
        _options = options.Value;
    }

    public bool IsRateLimited(string clientIp)
    {
        if (_failedAttempts.TryGetValue(clientIp, out var info))
        {
            if (DateTime.UtcNow < info.LockUntil)
            {
                return true;
            }
        }
        return false;
    }

    public void RecordFailure(string clientIp)
    {
        _failedAttempts.AddOrUpdate(clientIp,
            _ => (1, DateTime.MinValue),
            (_, old) =>
            {
                var attempts = old.Attempts + 1;
                var lockUntil = attempts >= 5 ? DateTime.UtcNow.AddSeconds(60) : DateTime.MinValue;
                return (attempts, lockUntil);
            });
    }

    public void RecordSuccess(string clientIp)
    {
        _failedAttempts.TryRemove(clientIp, out _);
    }

    public string? AuthenticateKey(string accessKey, string clientIp)
    {
        if (IsRateLimited(clientIp)) return null;

        if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(accessKey.Trim()),
                System.Text.Encoding.UTF8.GetBytes(_options.AccessKey.Trim())))
        {
            RecordSuccess(clientIp);
            var token = Guid.NewGuid().ToString("N") + RandomNumberGenerator.GetInt32(100000, 999999);
            var session = new SessionLifecycleStateMachine(token, clientIp, TimeSpan.FromMinutes(_options.SessionTimeoutMinutes));
            session.Activate();
            _sessions[token] = session;
            return token;
        }

        RecordFailure(clientIp);
        return null;
    }

    public bool ValidateToken(string token, string clientIp)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (_sessions.TryGetValue(token, out var session))
        {
            if (session.CurrentState == GatewaySessionState.Active)
            {
                session.Touch();
                return true;
            }
            _sessions.TryRemove(token, out _);
        }

        return false;
    }

    public bool ValidateKeyOrToken(string? keyOrToken, string clientIp)
    {
        if (string.IsNullOrWhiteSpace(keyOrToken)) return false;

        // 1. 直接匹配主 AccessKey
        if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(keyOrToken.Trim()),
                System.Text.Encoding.UTF8.GetBytes(_options.AccessKey.Trim())))
        {
            return true;
        }

        // 2. 匹配已登录会话 Token
        return ValidateToken(keyOrToken, clientIp);
    }

    public void RevokeToken(string token)
    {
        if (_sessions.TryRemove(token, out var session))
        {
            session.Revoke();
        }
    }
}
