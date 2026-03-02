public class ExtractConfig
{
    public string SourceDirectory { get; set; }
    public string OutputFolder { get; set; }
    public string LogFolder { get; set; }
    public string OpenFilePath { get; set; }

    public string Prefix { get; set; }

    public string StartDateHourMin { get; set; }  // Renommé pour respecter la convention PascalCase
    public string EndDateHourMin { get; set; }    // Renommé pour respecter la convention PascalCase

    public string LastProcessedPath { get; set; }
    public string SentFtpRecordsSuccess { get; set; }  // Renommé pour respecter la convention PascalCase
    public string SentFtpRecordsFailed { get; set; }   // Renommé pour respecter la convention PascalCase

    public string SendFileFTP { get; set; }

    // Ajout de la nouvelle propriété pour l'intervalle du scan périodique
    public int PeriodicScanIntervalMinutes { get; set; } // En minutes, exemple : 5

    public SftpSettings SftpSettings { get; set; }
}

public class SftpSettings
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string UploadPath { get; set; }
}
