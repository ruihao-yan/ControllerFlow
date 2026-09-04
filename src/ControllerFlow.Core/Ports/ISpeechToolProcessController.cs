namespace ControllerFlow.Core.Ports;

/// <summary>一次语音工具进程会话的句柄。</summary>
public sealed record SpeechToolSession(Guid Id, string ExecutablePath);

/// <summary>
/// 语音转文字工具的进程会话端口（“按住启动工具、松开结束工具”模式）。
/// Windows 实现负责启动进程并在会话结束时优雅终止；Core 层只感知会话句柄。
/// </summary>
public interface ISpeechToolProcessController
{
    /// <summary>
    /// 启动语音工具进程。成功返回会话句柄；启动失败（例如路径不存在）时抛出异常，
    /// 由引擎转换为执行失败反馈。
    /// </summary>
    ValueTask<SpeechToolSession?> StartAsync(
        string executablePath,
        string? arguments,
        CancellationToken cancellationToken);

    /// <summary>结束指定会话：先请求优雅关闭，超时后强制终止。</summary>
    ValueTask StopAsync(
        SpeechToolSession session,
        CancellationToken cancellationToken);
}