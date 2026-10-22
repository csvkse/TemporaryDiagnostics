using TemporaryDiagnostics.Core.StateMachines;

namespace TemporaryDiagnostics.Core.Contracts;

public sealed record PtyStartOptions(
    string? Executable = null,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null,
    int Columns = 120,
    int Rows = 32,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

/// <summary>
/// 交互式 PTY 终端会话契约。
/// </summary>
public interface IPtySession : IAsyncDisposable
{
    string Id { get; }
    int ProcessId { get; }
    PtySessionState State { get; }
    bool IsActive { get; }

    ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
    ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);
    Task TerminateAsync(CancellationToken cancellationToken = default);
    void Touch();
}

/// <summary>
/// 跨平台 PTY 引擎契约。
/// </summary>
public interface IPtyEngine
{
    bool IsSupported { get; }
    Task<IPtySession> StartSessionAsync(PtyStartOptions options, CancellationToken cancellationToken = default);
    IPtySession? GetSession(string sessionId);
    Task<bool> CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    IReadOnlyList<IPtySession> ListActiveSessions();
}
