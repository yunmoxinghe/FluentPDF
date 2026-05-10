using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace FluentPDF.Helpers;

/// <summary>
/// 错误日志记录工具类
/// </summary>
public static class ErrorLogger
{
    private const string LogFileName = "error_log.txt";
    private const int MaxLogSizeBytes = 1024 * 1024; // 1 MB

    /// <summary>
    /// 记录错误到日志文件
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="exception">异常对象（可选）</param>
    public static async Task LogErrorAsync(string message, Exception? exception = null)
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var logFile = await localFolder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);

            // 检查日志文件大小，如果超过限制则清空
            var properties = await logFile.GetBasicPropertiesAsync();
            if (properties.Size > MaxLogSizeBytes)
            {
                await FileIO.WriteTextAsync(logFile, string.Empty);
            }

            // 构建日志条目
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";

            if (exception != null)
            {
                logEntry += $"\nException: {exception.GetType().Name}";
                logEntry += $"\nMessage: {exception.Message}";
                logEntry += $"\nStackTrace: {exception.StackTrace}";
            }

            logEntry += "\n" + new string('-', 80) + "\n";

            // 追加到日志文件
            await FileIO.AppendTextAsync(logFile, logEntry);

            // 同时输出到调试控制台
            System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
            if (exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // 日志记录失败时静默处理，避免影响主流程
            System.Diagnostics.Debug.WriteLine($"Failed to log error: {message}");
        }
    }

    /// <summary>
    /// 记录警告到日志文件
    /// </summary>
    /// <param name="message">警告消息</param>
    public static async Task LogWarningAsync(string message)
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var logFile = await localFolder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"[{timestamp}] [WARNING] {message}\n";

            await FileIO.AppendTextAsync(logFile, logEntry);
            System.Diagnostics.Debug.WriteLine($"[WARNING] {message}");
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"Failed to log warning: {message}");
        }
    }

    /// <summary>
    /// 记录信息到日志文件
    /// </summary>
    /// <param name="message">信息消息</param>
    public static async Task LogInfoAsync(string message)
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var logFile = await localFolder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"[{timestamp}] [INFO] {message}\n";

            await FileIO.AppendTextAsync(logFile, logEntry);
            System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"Failed to log info: {message}");
        }
    }

    /// <summary>
    /// 获取日志文件路径
    /// </summary>
    public static string GetLogFilePath()
    {
        return Path.Combine(ApplicationData.Current.LocalFolder.Path, LogFileName);
    }

    /// <summary>
    /// 清空日志文件
    /// </summary>
    public static async Task ClearLogAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var logFile = await localFolder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);
            await FileIO.WriteTextAsync(logFile, string.Empty);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine("Failed to clear log file");
        }
    }
}
