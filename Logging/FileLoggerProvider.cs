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

        // SOLUTION CS8633 : Implémentation explicite de l'interface
        // Cela permet de matcher les contraintes de TState sans conflit de nullabilité
        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        // SOLUTION CS8604 : Ajout de '?' sur Exception et le formatter 
        // car un log n'a pas systématiquement d'exception associée.
        public void Log<TState>(LogLevel logLevel, EventId eventId,
                                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {message}";

            if (exception != null)
                logEntry += Environment.NewLine + exception;

            // Utilisation d'un verrouillage simple ou lock si plusieurs threads écrivent
            File.AppendAllText(_file, logEntry + Environment.NewLine);
        }

        // Classe interne pour gérer les scopes vides proprement
        private class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();
            public void Dispose() { }
        }
    }
}