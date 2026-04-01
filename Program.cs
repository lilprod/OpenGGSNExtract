using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO;

internal class Program
{
    public static void Main(string[] args)
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        IConfiguration configuration = configBuilder.Build();

        string logFolder = configuration["ExtractConfig:LogFolder"] ?? @"E:\Prodige\LOGS";
        if (string.IsNullOrWhiteSpace(logFolder))
            logFolder = Path.Combine(AppContext.BaseDirectory, "Logs");

        Directory.CreateDirectory(logFolder);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logFolder, "OpenGGSNExtract_.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("=== SERVICE GGSN EXTRACT - START ===");

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Injection des configurations
                    services.Configure<SftpSettings>(configuration.GetSection("ExtractConfig:SftpSettings"));
                    services.Configure<ExtractConfig>(configuration.GetSection("ExtractConfig"));
                    services.AddSingleton<FtpHelper>();
                    services.AddSingleton<FileProcessor>();
                    services.AddHostedService<Worker>();
                })
                .UseSerilog()
                .Build();

            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Erreur critique au démarrage du service.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
