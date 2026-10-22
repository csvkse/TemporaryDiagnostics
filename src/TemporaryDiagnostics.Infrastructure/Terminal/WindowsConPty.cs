using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.StateMachines;

namespace TemporaryDiagnostics.Infrastructure.Terminal;

internal static class WindowsConPtyNative
{
    public const int S_OK = 0;
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        [In] ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
}

public sealed class WindowsConPtySession : IPtySession
{
    private readonly IntPtr _hPC;
    private readonly IntPtr _hProcess;
    private readonly SafeFileHandle _inPipeWriteHandle;
    private readonly SafeFileHandle _outPipeReadHandle;
    private readonly FileStream _inStream;
    private readonly FileStream _outStream;
    private readonly PtySessionStateMachine _stateMachine;
    private int _disposed;

    public string Id { get; }
    public int ProcessId { get; }
    public PtySessionState State => _stateMachine.CurrentState;
    public bool IsActive => _stateMachine.CurrentState == PtySessionState.Active || _stateMachine.CurrentState == PtySessionState.Detached;

    public WindowsConPtySession(
        string sessionId,
        int processId,
        IntPtr hProcess,
        IntPtr hPC,
        SafeFileHandle inPipeWriteHandle,
        SafeFileHandle outPipeReadHandle)
    {
        Id = sessionId;
        ProcessId = processId;
        _hProcess = hProcess;
        _hPC = hPC;
        _inPipeWriteHandle = inPipeWriteHandle;
        _outPipeReadHandle = outPipeReadHandle;
        _inStream = new FileStream(_inPipeWriteHandle, FileAccess.Write, 4096, isAsync: true);
        _outStream = new FileStream(_outPipeReadHandle, FileAccess.Read, 4096, isAsync: true);

        _stateMachine = new PtySessionStateMachine(sessionId);
        _stateMachine.TryTransition(PtySessionState.Initializing);
        _stateMachine.TryTransition(PtySessionState.Active);
    }

    public void Touch() => _stateMachine.TouchActivity();

    public async ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return;
        Touch();
        await _inStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        await _inStream.FlushAsync(cancellationToken);
    }

    public async ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return 0;
        try
        {
            var read = await _outStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (read > 0) Touch();
            return read;
        }
        catch
        {
            return 0;
        }
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (_hPC != IntPtr.Zero)
        {
            var size = new WindowsConPtyNative.COORD
            {
                X = (short)Math.Clamp(columns, 10, 500),
                Y = (short)Math.Clamp(rows, 5, 200)
            };
            WindowsConPtyNative.ResizePseudoConsole(_hPC, size);
        }
        return Task.CompletedTask;
    }

    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return Task.CompletedTask;

        _stateMachine.TryTransition(PtySessionState.Terminated);

        try
        {
            if (_hProcess != IntPtr.Zero)
            {
                WindowsConPtyNative.TerminateProcess(_hProcess, 1);
                WindowsConPtyNative.CloseHandle(_hProcess);
            }

            if (_hPC != IntPtr.Zero)
            {
                WindowsConPtyNative.ClosePseudoConsole(_hPC);
            }

            _inStream.Dispose();
            _outStream.Dispose();
            _inPipeWriteHandle.Dispose();
            _outPipeReadHandle.Dispose();
        }
        catch
        {
            // 忽略销毁时的清理异常
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
}

public static class WindowsConPtyFactory
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static WindowsConPtySession? TryCreateSession(
        string sessionId,
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        int columns,
        int rows,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var hInRead = IntPtr.Zero;
        var hInWrite = IntPtr.Zero;
        var hOutRead = IntPtr.Zero;
        var hOutWrite = IntPtr.Zero;
        var hPC = IntPtr.Zero;
        var lpAttributeList = IntPtr.Zero;
        var environment = IntPtr.Zero;

        try
        {
            if (!WindowsConPtyNative.CreatePipe(out hInRead, out hInWrite, IntPtr.Zero, 0) ||
                !WindowsConPtyNative.CreatePipe(out hOutRead, out hOutWrite, IntPtr.Zero, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 ConPTY 管道");
            }

            var size = new WindowsConPtyNative.COORD
            {
                X = (short)Math.Clamp(columns, 10, 500),
                Y = (short)Math.Clamp(rows, 5, 200)
            };

            var hr = WindowsConPtyNative.CreatePseudoConsole(size, hInRead, hOutWrite, 0, out hPC);
            if (hr != WindowsConPtyNative.S_OK)
            {
                throw new Win32Exception(hr, "创建 PseudoConsole 失败");
            }

            var lpSize = IntPtr.Zero;
            WindowsConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
            lpAttributeList = Marshal.AllocHGlobal(lpSize);

            if (!WindowsConPtyNative.InitializeProcThreadAttributeList(lpAttributeList, 1, 0, ref lpSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "初始化属性列表失败");
            }

            if (!WindowsConPtyNative.UpdateProcThreadAttribute(
                lpAttributeList,
                0,
                (IntPtr)WindowsConPtyNative.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "更新 ConPTY 属性失败");
            }

            var siex = new WindowsConPtyNative.STARTUPINFOEX();
            siex.StartupInfo.cb = Marshal.SizeOf<WindowsConPtyNative.STARTUPINFOEX>();
            siex.StartupInfo.dwFlags = 0x00000100; // STARTF_USESTDHANDLES
            siex.lpAttributeList = lpAttributeList;

            var cmdLine = new StringBuilder(BuildCommandLine(executable, arguments));
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key != null && entry.Value != null)
                    variables[(string)entry.Key] = (string)entry.Value;
            }
            variables["TERM"] = "xterm-256color";
            variables["COLORTERM"] = "truecolor";
            if (environmentVariables != null)
            {
                foreach (var (k, v) in environmentVariables) variables[k] = v;
            }

            environment = Marshal.StringToHGlobalUni(string.Join('\0', variables.Select(e => e.Key + "=" + e.Value)) + "\0\0");

            var cwd = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                ? workingDirectory
                : null;

            if (!WindowsConPtyNative.CreateProcess(
                null,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                WindowsConPtyNative.EXTENDED_STARTUPINFO_PRESENT | WindowsConPtyNative.CREATE_UNICODE_ENVIRONMENT,
                environment,
                cwd,
                ref siex,
                out var pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"启动进程失败: {cmdLine}");
            }

            WindowsConPtyNative.CloseHandle(hInRead);
            hInRead = IntPtr.Zero;
            WindowsConPtyNative.CloseHandle(hOutWrite);
            hOutWrite = IntPtr.Zero;

            if (pi.hThread != IntPtr.Zero)
            {
                WindowsConPtyNative.CloseHandle(pi.hThread);
            }

            var inSafe = new SafeFileHandle(hInWrite, ownsHandle: true);
            var outSafe = new SafeFileHandle(hOutRead, ownsHandle: true);

            return new WindowsConPtySession(sessionId, pi.dwProcessId, pi.hProcess, hPC, inSafe, outSafe);
        }
        catch
        {
            if (hInRead != IntPtr.Zero) WindowsConPtyNative.CloseHandle(hInRead);
            if (hInWrite != IntPtr.Zero) WindowsConPtyNative.CloseHandle(hInWrite);
            if (hOutRead != IntPtr.Zero) WindowsConPtyNative.CloseHandle(hOutRead);
            if (hOutWrite != IntPtr.Zero) WindowsConPtyNative.CloseHandle(hOutWrite);
            if (hPC != IntPtr.Zero) WindowsConPtyNative.ClosePseudoConsole(hPC);
            return null;
        }
        finally
        {
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            if (lpAttributeList != IntPtr.Zero)
            {
                WindowsConPtyNative.DeleteProcThreadAttributeList(lpAttributeList);
                Marshal.FreeHGlobal(lpAttributeList);
            }
        }
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        return string.Join(' ', new[] { executable }.Concat(arguments).Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        var quoted = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(character);
            slashes = 0;
        }
        quoted.Append('\\', slashes * 2);
        return quoted.Append('"').ToString();
    }
}
