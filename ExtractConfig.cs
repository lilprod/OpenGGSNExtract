public class ExtractConfig
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public string LogFolder { get; set; } = string.Empty;
    public string OpenFilePath { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string StartDateHourMin { get; set; } = string.Empty; // Renommé pour respecter la convention PascalCase
    public string EndDateHourMin { get; set; } = string.Empty; // Renommé pour respecter la convention PascalCase

    public string LastProcessedPath { get; set; } = string.Empty;
    public string SentFtpRecordsSuccess { get; set; } = string.Empty; // Renommé pour respecter la convention PascalCase
    public string SentFtpRecordsFailed { get; set; } = string.Empty;  // Renommé pour respecter la convention PascalCase

    public string SendFileFTP { get; set; } = string.Empty;

    // Nombre de tentatives de réessai FTP en cas d'échec
    public int FtpRetryCount { get; set; }

    // Délai en millisecondes entre chaque tentative de réessai FTP
    public int FtpRetryDelayMs { get; set; }

    // Ajout de la nouvelle propriété pour l'intervalle du scan périodique
    public int PeriodicScanIntervalMinutes { get; set; } // En minutes, exemple : 5

    public SftpSettings SftpSettings { get; set; } = new();
}

public class SftpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string UploadPath { get; set; } = string.Empty;
}
