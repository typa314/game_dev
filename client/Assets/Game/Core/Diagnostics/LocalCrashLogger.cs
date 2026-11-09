using System;
using System.IO;
using System.Linq;
using Game.Domain;

namespace Game.Core.Diagnostics
{
    /// <summary>
    /// 純本機結構化當機與例外日誌紀錄器 (M3-7)。
    /// 依據專案資安原則：只寫入本機 persistentDataPath/logs/，絕不發送外部網路請求。
    /// 支援檔案數量輪替限制（預設最多保留最近 10 份），超過時自動刪除最舊記錄。
    /// 遵循專案時間規範：所有時間查詢皆透過 IClock。
    /// </summary>
    public sealed class LocalCrashLogger
    {
        private readonly string _logDirectory;
        private readonly int _maxLogFiles;
        private readonly IClock _clock;
        private readonly object _lock = new object();

        public string LogDirectory => _logDirectory;
        public int MaxLogFiles => _maxLogFiles;

        public LocalCrashLogger(string logDirectory, int maxLogFiles = 10, IClock clock = null)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
                throw new ArgumentException("logDirectory cannot be empty", nameof(logDirectory));
            if (maxLogFiles <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLogFiles), "maxLogFiles must be positive");

            _logDirectory = logDirectory;
            _maxLogFiles = maxLogFiles;
            _clock = clock ?? new SystemClock();

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        public string LogCrash(Exception ex, string contextInfo = null)
        {
            if (ex == null) return null;
            return LogCrash(ex.GetType().FullName, ex.Message, ex.StackTrace, contextInfo);
        }

        public string LogCrash(string errorType, string message, string stackTrace, string contextInfo = null)
        {
            lock (_lock)
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                DateTime utcNow = _clock.UtcNow;
                string timestamp = utcNow.ToString("yyyyMMdd_HHmmss_fff");
                string fileName = $"crash_{timestamp}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.log";
                string fullPath = Path.Combine(_logDirectory, fileName);

                string content = FormatCrashReport(utcNow, errorType, message, stackTrace, contextInfo);
                File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);

                EnforceFileLimit();
                return fullPath;
            }
        }

        public void EnforceFileLimit()
        {
            if (!Directory.Exists(_logDirectory)) return;

            var dir = new DirectoryInfo(_logDirectory);
            var files = dir.GetFiles("crash_*.log")
                           .OrderBy(f => f.CreationTimeUtc)
                           .ToList();

            while (files.Count > _maxLogFiles)
            {
                try
                {
                    files[0].Delete();
                    files.RemoveAt(0);
                }
                catch
                {
                    break;
                }
            }
        }

        public int GetLogFileCount()
        {
            if (!Directory.Exists(_logDirectory)) return 0;
            return Directory.GetFiles(_logDirectory, "crash_*.log").Length;
        }

        private static string FormatCrashReport(DateTime timeUtc, string errorType, string message, string stackTrace, string contextInfo)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== LOCAL CRASH REPORT (PROJECT TOWER) ===");
            sb.AppendLine($"TimestampUtc: {timeUtc:yyyy-MM-ddTHH:mm:ss.fffZ}");
            sb.AppendLine($"ErrorType: {errorType}");
            sb.AppendLine($"Message: {message}");
            if (!string.IsNullOrEmpty(contextInfo))
            {
                sb.AppendLine($"Context: {contextInfo}");
            }
            sb.AppendLine("--- STACK TRACE ---");
            sb.AppendLine(stackTrace ?? "(No stack trace provided)");
            sb.AppendLine("==========================================");
            return sb.ToString();
        }
    }
}
