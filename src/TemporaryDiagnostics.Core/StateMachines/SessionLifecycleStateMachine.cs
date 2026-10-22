namespace TemporaryDiagnostics.Core.StateMachines;

/// <summary>
/// 诊断网关会话状态枚举。
/// </summary>
public enum GatewaySessionState
{
    Unauthenticated,
    Active,
    Expired,
    Revoked
}

/// <summary>
/// 临时诊断会话生命周期状态机。
/// 支持自动 TTL 过期检测与一键手动撤销，保证会话终结后即时阻断一切操作。
/// </summary>
public sealed class SessionLifecycleStateMachine
{
    private readonly object _syncRoot = new();
    private GatewaySessionState _state = GatewaySessionState.Unauthenticated;
    private DateTime _authenticatedAt = DateTime.MinValue;
    private DateTime _lastActivityAt = DateTime.MinValue;
    private readonly TimeSpan _timeout;

    public string Token { get; }
    public string ClientIp { get; }

    public GatewaySessionState CurrentState
    {
        get
        {
            lock (_syncRoot)
            {
                if (_state == GatewaySessionState.Active && DateTime.UtcNow - _lastActivityAt > _timeout)
                {
                    _state = GatewaySessionState.Expired;
                }
                return _state;
            }
        }
    }

    public SessionLifecycleStateMachine(string token, string clientIp, TimeSpan timeout)
    {
        Token = token;
        ClientIp = clientIp;
        _timeout = timeout;
    }

    public void Activate()
    {
        lock (_syncRoot)
        {
            _state = GatewaySessionState.Active;
            _authenticatedAt = DateTime.UtcNow;
            _lastActivityAt = DateTime.UtcNow;
        }
    }

    public bool Touch()
    {
        lock (_syncRoot)
        {
            if (CurrentState == GatewaySessionState.Active)
            {
                _lastActivityAt = DateTime.UtcNow;
                return true;
            }
            return false;
        }
    }

    public void Revoke()
    {
        lock (_syncRoot)
        {
            _state = GatewaySessionState.Revoked;
        }
    }

    public TimeSpan RemainingTime
    {
        get
        {
            lock (_syncRoot)
            {
                if (CurrentState != GatewaySessionState.Active) return TimeSpan.Zero;
                var elapsed = DateTime.UtcNow - _lastActivityAt;
                var remaining = _timeout - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}
