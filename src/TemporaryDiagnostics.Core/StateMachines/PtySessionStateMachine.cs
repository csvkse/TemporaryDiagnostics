namespace TemporaryDiagnostics.Core.StateMachines;

/// <summary>
/// 终端 PTY 会话状态枚举。
/// </summary>
public enum PtySessionState
{
    Created,
    Initializing,
    Active,
    Detached,
    Terminated,
    Disposed
}

/// <summary>
/// 终端会话状态机，负责严格管控 OS 进程生命周期，支持断线无感挂起与自动超时回收。
/// </summary>
public sealed class PtySessionStateMachine
{
    private readonly object _syncRoot = new();
    private PtySessionState _currentState = PtySessionState.Created;
    private DateTime _lastActiveTime = DateTime.UtcNow;

    public string SessionId { get; }
    public PtySessionState CurrentState
    {
        get
        {
            lock (_syncRoot) return _currentState;
        }
    }

    public DateTime LastActiveTime
    {
        get
        {
            lock (_syncRoot) return _lastActiveTime;
        }
    }

    public PtySessionStateMachine(string sessionId)
    {
        SessionId = sessionId;
    }

    public bool TryTransition(PtySessionState targetState)
    {
        lock (_syncRoot)
        {
            var isValid = (_currentState, targetState) switch
            {
                (PtySessionState.Created, PtySessionState.Initializing) => true,
                (PtySessionState.Initializing, PtySessionState.Active) => true,
                (PtySessionState.Initializing, PtySessionState.Terminated) => true,
                (PtySessionState.Active, PtySessionState.Detached) => true,
                (PtySessionState.Active, PtySessionState.Terminated) => true,
                (PtySessionState.Detached, PtySessionState.Active) => true,
                (PtySessionState.Detached, PtySessionState.Terminated) => true,
                (PtySessionState.Terminated, PtySessionState.Disposed) => true,
                _ => false
            };

            if (isValid)
            {
                _currentState = targetState;
                _lastActiveTime = DateTime.UtcNow;
                return true;
            }

            return false;
        }
    }

    public void TouchActivity()
    {
        lock (_syncRoot)
        {
            _lastActiveTime = DateTime.UtcNow;
        }
    }

    public bool IsExpired(TimeSpan timeout)
    {
        lock (_syncRoot)
        {
            return (_currentState == PtySessionState.Detached || _currentState == PtySessionState.Active) && (DateTime.UtcNow - _lastActiveTime) > timeout;
        }
    }
}
