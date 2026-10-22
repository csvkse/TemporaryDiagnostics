using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.StateMachines;

namespace TemporaryDiagnostics.Infrastructure.Terminal;

/// <summary>
/// 跨平台标准流降级终端会话。
/// 当操作系统无原生 PTY 或 ConPTY 时，利用标准输入/输出流提供完备的命令交互。
/// </summary>
public sealed class ProcessStreamPtySession : IPtySession
{
    private readonly Process _process;
    private readonly Channel<byte[]> _outputChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleWriter = false,
        SingleReader = true
    });
    private readonly PtySessionStateMachine _stateMachine;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;
    private byte[]? _pendingBuffer;
    private int _pendingOffset;

    public string Id { get; }
    public int ProcessId => _process.HasExited ? -1 : _process.Id;
    public PtySessionState State => _stateMachine.CurrentState;
    public bool IsActive => _stateMachine.CurrentState == PtySessionState.Active || _stateMachine.CurrentState == PtySessionState.Detached;

    public ProcessStreamPtySession(string sessionId, Process process)
    {
        Id = sessionId;
        _process = process;
        _stateMachine = new PtySessionStateMachine(sessionId);
        _stateMachine.TryTransition(PtySessionState.Initializing);
        _stateMachine.TryTransition(PtySessionState.Active);

        // 启动后台读取任务
        _ = ReadStreamToChannelAsync(_process.StandardOutput.BaseStream, _outputChannel.Writer, _cts.Token);
        _ = ReadStreamToChannelAsync(_process.StandardError.BaseStream, _outputChannel.Writer, _cts.Token);
    }

    public void Touch() => _stateMachine.TouchActivity();

    public async ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!IsActive || _process.HasExited) return;
        Touch();
        try
        {
            await _process.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        }
        catch
        {
            // 忽略写入断开异常
        }
    }

    public async ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return 0;

        try
        {
            if (_pendingBuffer != null && _pendingOffset < _pendingBuffer.Length)
            {
                var available = _pendingBuffer.Length - _pendingOffset;
                var toCopy = Math.Min(available, count);
                Buffer.BlockCopy(_pendingBuffer, _pendingOffset, buffer, offset, toCopy);
                _pendingOffset += toCopy;
                if (_pendingOffset >= _pendingBuffer.Length)
                {
                    _pendingBuffer = null;
                    _pendingOffset = 0;
                }
                Touch();
                return toCopy;
            }

            var chunk = await _outputChannel.Reader.ReadAsync(cancellationToken);
            var copyLen = Math.Min(chunk.Length, count);
            Buffer.BlockCopy(chunk, 0, buffer, offset, copyLen);

            if (copyLen < chunk.Length)
            {
                _pendingBuffer = chunk;
                _pendingOffset = copyLen;
            }

            Touch();
            return copyLen;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        // 标准流无需窗口大小更新
        return Task.CompletedTask;
    }

    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return Task.CompletedTask;

        _stateMachine.TryTransition(PtySessionState.Terminated);

        try
        {
            _cts.Cancel();
            _outputChannel.Writer.TryComplete();

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            _process.Dispose();
        }
        catch
        {
            // 忽略销毁时的退出异常
        }
        finally
        {
            _stateMachine.TryTransition(PtySessionState.Disposed);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await TerminateAsync();
        GC.SuppressFinalize(this);
    }

    private static async Task ReadStreamToChannelAsync(Stream stream, ChannelWriter<byte[]> writer, CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read <= 0) break;

                var data = new byte[read];
                Buffer.BlockCopy(buffer, 0, data, 0, read);
                if (!writer.TryWrite(data))
                {
                    await writer.WriteAsync(data, ct);
                }
            }
        }
        catch
        {
            // 忽略正常读取中断
        }
    }
}
