using System.ComponentModel;
using System.Diagnostics;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Windows.Speech;

/// <summary>
/// <see cref="ISpeechToolProcessController"/> 的 Windows 实现。
/// 启动：Process.Start（ShellExecute，支持任意可执行文件/快捷方式）；
/// 结束：先 CloseMainWindow 优雅关闭，宽限期（默认 1 秒）后仍未退出则 Kill 整棵进程树，
/// 保证“松开即结束”语义，不残留语音工具进程。
/// </summary>
public sealed class SpeechToolProcessController : ISpeechToolProcessController
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly Dictionary<Guid, Process> _processes = new();

    public ValueTask<SpeechToolSession?> StartAsync(
        string executablePath,
        string? arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动语音工具：{executablePath}");

        var session = new SpeechToolSession(Guid.NewGuid(), executablePath);
        lock (_sync)
        {
            _processes[session.Id] = process;
        }

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                // 进程自行退出时清理注册表，避免会话句柄泄漏。
                lock (_sync)
                {
                    if (_processes.Remove(session.Id))
                    {
                        process.Dispose();
                    }
                }
            };
        }
        catch (InvalidOperationException)
        {
            // 进程在注册前已退出：注册表由 StopAsync 或退出事件清理。
        }

        return ValueTask.FromResult<SpeechToolSession?>(session);
    }

    public async ValueTask StopAsync(
        SpeechToolSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        Process? process;
        lock (_sync)
        {
            if (!_processes.Remove(session.Id, out process))
            {
                return;
            }
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            // 先尝试优雅关闭主窗口；无窗口或宽限期内未退出则强制终止。
            if (!process.CloseMainWindow())
            {
                KillQuietly(process);
                return;
            }

            var exited = await WaitForExitWithGraceAsync(process, cancellationToken);
            if (!exited && !process.HasExited)
            {
                KillQuietly(process);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 宽限期已过，进程仍在运行：强制终止。
            KillQuietly(process);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方取消：仍尽力终止，避免语音工具进程残留。
            KillQuietly(process);
        }
        catch (InvalidOperationException)
        {
            // 进程已退出，句柄失效。
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<bool> WaitForExitWithGraceAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(GracePeriod);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出。
        }
        catch (Win32Exception)
        {
            // 无权限终止（极少见）：放弃，避免误伤。
        }
    }
}