using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace GgsnExtractService.Logging
{
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _logFile;

        public FileLoggerProvider(IConfiguration config)
        {
            string folder = config["ExtractConfig:LogFolder"] ?? "logs";
            Directory.CreateDirectory(folder);
            _logFile = Path.Combine(folder, "service.log");
        }

        public ILogger CreateLogger(string categoryName)
            => new FileLogger(_logFile);

        public void Dispose() { }
    }

    public class FileLogger : ILogger
    {
        private readonly string _file;

        public FileLogger(string file) => _file = file;

        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId,
                                TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            string msg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {formatter(state, exception)}";
            if (exception != null) msg += "\n" + exception;

            File.AppendAllText(_file, msg + Environment.NewLine);
        }
    }
}
