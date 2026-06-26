using System.Globalization;

namespace Ecommerce.Api.Infrastructure
{
    public interface IFileTextLogger
    {
        void LogError(string category, string message, Exception? exception = null);
        void LogInfo(string category, string message);
    }

    public class FileTextLogger : IFileTextLogger
    {
        private readonly string _logDir;
        private static readonly object _lock = new();

        public FileTextLogger(IConfiguration configuration)
        {
            _logDir = Environment.GetEnvironmentVariable("TEXT_LOG_DIR")
                ?? configuration["Logging:TextLogDirectory"]
                ?? "/data/apps/textlog";
        }

        public void LogError(string category, string message, Exception? exception = null)
            => Write("ERROR", category, message, exception);

        public void LogInfo(string category, string message)
            => Write("INFO", category, message);

        private void Write(string level, string category, string message, Exception? exception = null)
        {
            try
            {
                Directory.CreateDirectory(_logDir);
                var filePath = Path.Combine(_logDir, $"{DateTime.UtcNow:yyyy-MM-dd}.log");
                var time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var lines = new List<string>
                {
                    $"[{time} UTC] [{level}] [{category}] {message}"
                };

                if (exception != null)
                {
                    lines.Add(exception.ToString());
                }

                lock (_lock)
                {
                    File.AppendAllLines(filePath, lines);
                }
            }
            catch
            {
                // Never block the API because logging failed.
            }
        }
    }
}
