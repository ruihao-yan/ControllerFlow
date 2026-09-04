namespace ControllerFlow.Windows.Logging;

/// <summary>
/// 极简文件日志：追加写入 %LocalAppData%\ControllerFlow\logs\app.log，
/// 单文件超过 1 MB 时轮转为 app.log.1。日志失败绝不影响主流程。
/// </summary>
public static class FileLog
{
    private const long MaxFileLength = 1_048_576;

    private static readonly object Sync = new();

    public static string LogFilePath { get; } = BuildLogFilePath();

    public static string LogDirectory { get; } = Path.GetDirectoryName(LogFilePath)!;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // 磁盘满 / 文件被占用：写日志失败不应影响主流程。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFilePath))
            {
                return;
            }

            if (new FileInfo(LogFilePath).Length < MaxFileLength)
            {
                return;
            }

            var rotated = LogFilePath + ".1";
            File.Delete(rotated);
            File.Move(LogFilePath, rotated);
        }
        catch (IOException)
        {
        }
    }

    private static string BuildLogFilePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "ControllerFlow", "logs", "app.log");
    }
}