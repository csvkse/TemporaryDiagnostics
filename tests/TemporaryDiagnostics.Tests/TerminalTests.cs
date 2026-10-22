using System.Text;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.StateMachines;
using TemporaryDiagnostics.Infrastructure.Terminal;
using Xunit;

namespace TemporaryDiagnostics.Tests;

public class TerminalTests
{
    [Fact]
    public void NormalizeLineEndings_Should_Strip_CarriageReturn_Before_LineFeed()
    {
        // 模拟前端发送的 Windows CRLF 命令："ls\r\n"
        var input = Encoding.UTF8.GetBytes("ls\r\n");
        var normalized = ProcessStreamPtySession.NormalizeLineEndings(input, 0, input.Length);
        var resultText = Encoding.UTF8.GetString(normalized.Span);

        Assert.Equal("ls\n", resultText);
        Assert.DoesNotContain("\r", resultText);
    }

    [Fact]
    public void NormalizeLineEndings_Should_Convert_Isolated_CarriageReturn_To_LineFeed()
    {
        // 模拟游离单独的 \r："dir\r"
        var input = Encoding.UTF8.GetBytes("dir\r");
        var normalized = ProcessStreamPtySession.NormalizeLineEndings(input, 0, input.Length);
        var resultText = Encoding.UTF8.GetString(normalized.Span);

        Assert.Equal("dir\n", resultText);
        Assert.DoesNotContain("\r", resultText);
    }

    [Fact]
    public void NormalizeLineEndings_Should_Preserve_Pure_LineFeed()
    {
        // 纯 \n 输入："uname -a\n"
        var input = Encoding.UTF8.GetBytes("uname -a\n");
        var normalized = ProcessStreamPtySession.NormalizeLineEndings(input, 0, input.Length);
        var resultText = Encoding.UTF8.GetString(normalized.Span);

        Assert.Equal("uname -a\n", resultText);
    }

    [Fact]
    public void PtySessionManager_Should_Eagerly_Purge_Inactive_Sessions()
    {
        using var manager = new PtySessionManager();

        var mockSession1 = new DummyPtySession("sess_active", isActive: true);
        var mockSession2 = new DummyPtySession("sess_dead", isActive: false);

        manager.RegisterSession(mockSession1);
        manager.RegisterSession(mockSession2);

        // 调用 ListActiveSessions 时，应惰性清除 inactive 会话
        var activeList = manager.ListActiveSessions();

        Assert.Single(activeList);
        Assert.Equal("sess_active", activeList[0].Id);
        Assert.Null(manager.GetSession("sess_dead"));
    }

    [Fact]
    public void PtySessionStateMachine_Should_Detect_Expired_Active_And_Detached()
    {
        var sm = new PtySessionStateMachine("test_sm");
        sm.TryTransition(PtySessionState.Initializing);
        sm.TryTransition(PtySessionState.Active);

        // 刚刚激活的会话不应超时
        Assert.False(sm.IsExpired(TimeSpan.FromSeconds(10)));

        // 挂起状态
        sm.TryTransition(PtySessionState.Detached);
        Assert.False(sm.IsExpired(TimeSpan.FromSeconds(10)));
    }

    private sealed class DummyPtySession : IPtySession
    {
        public string Id { get; }
        public int ProcessId => 1000;
        public PtySessionState State => IsActive ? PtySessionState.Active : PtySessionState.Terminated;
        public bool IsActive { get; }

        public DummyPtySession(string id, bool isActive)
        {
            Id = id;
            IsActive = isActive;
        }

        public void Touch() { }
        public ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TerminateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
