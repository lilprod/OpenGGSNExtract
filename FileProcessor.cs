using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class FileProcessor
{
    private readonly ExtractConfig _config;
    private readonly ILogger<FileProcessor> _logger;
    private readonly FtpHelper _ftp;
    private readonly HashSet<string> _openRecords = new();
    private readonly Dictionary<string, DailyStats> _dailyStats = new();
    private string _lastProcessed = "";

    public ExtractConfig Config => _config;

    public FileProcessor(IOptions<ExtractConfig> config, ILogger<FileProcessor> logger, FtpHelper ftp)
    {
        _config = config.Value;
        _logger = logger;
        _ftp = ftp;

        EnsureDirectoriesExist();
        LoadOpenFile();
        LoadLastProcessed();
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_config.OutputFolder);
        Directory.CreateDirectory(_config.LogFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(_config.OpenFilePath));
        Directory.CreateDirectory(Path.GetDirectoryName(_config.LastProcessedPath));
    }

    private void LoadOpenFile()
    {
        if (!File.Exists(_config.OpenFilePath))
            File.WriteAllText(_config.OpenFilePath, "");

        foreach (var line in File.ReadAllLines(_config.OpenFilePath))
            _openRecords.Add(line.Trim());

        _logger.LogInformation($"OPEN chargé : {_openRecords.Count} lignes");
    }

    private void LoadLastProcessed()
    {
        if (File.Exists(_config.LastProcessedPath))
        {
            _lastProcessed = File.ReadAllText(_config.LastProcessedPath).Trim();
            _logger.LogInformation($"Dernier fichier traité : '{_lastProcessed}'");
        }
    }

    private void SaveLastProcessed(string date)
    {
        try
        {
            File.WriteAllText(_config.LastProcessedPath, date);
            _lastProcessed = date;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur écriture LastProcessed : {date}");
        }
    }

    public IEnumerable<string> GetEligibleFilesFromInitialScan()
    {
        if (string.IsNullOrWhiteSpace(_config.SourceDirectory) || !Directory.Exists(_config.SourceDirectory))
        {
            _logger.LogWarning($"Dossier source introuvable : {_config.SourceDirectory}");
            return Enumerable.Empty<string>();
        }

        var allFiles = Directory.EnumerateFiles(_config.SourceDirectory)
                                .Where(f => f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                                .ToList();

        _logger.LogInformation($"Fichiers trouvés dans le dossier source ({_config.SourceDirectory}) : {allFiles.Count}");
        foreach (var f in allFiles)
            _logger.LogInformation($" - {Path.GetFileName(f)}");

        string lastDate = string.IsNullOrWhiteSpace(_lastProcessed) ? null : ExtractDateFromFilename(_lastProcessed);
        _logger.LogInformation($"LastProcessed extrait : {lastDate}");

        List<string> eligibleFiles = new();

        foreach (var file in allFiles)
        {
            string fileName = Path.GetFileName(file);
            string fileDate = ExtractDateFromFilename(fileName);

            if (fileDate == null)
            {
                _logger.LogInformation($"IGNORÉ (format date invalide) : {fileName}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(lastDate) &&
                string.Compare(fileDate, lastDate, StringComparison.Ordinal) <= 0)
            {
                _logger.LogInformation($"IGNORÉ (déjà traité) : {fileName} | date fichier={fileDate}, lastProcessed={lastDate}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(_config.StartDateHourMin) &&
                (string.IsNullOrWhiteSpace(lastDate) || string.Compare(lastDate, _config.StartDateHourMin, StringComparison.Ordinal) < 0) &&
                string.Compare(fileDate, _config.StartDateHourMin, StringComparison.Ordinal) < 0)
            {
                _logger.LogInformation($"IGNORÉ (avant startDatehourMin) : {fileName} | date fichier={fileDate}, startDate={_config.StartDateHourMin}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(_config.Prefix) && !fileName.StartsWith(_config.Prefix))
            {
                _logger.LogInformation($"IGNORÉ (préfixe non correspondant) : {fileName} | attendu={_config.Prefix}");
                continue;
            }

            _logger.LogInformation($"FICHIER ÉLIGIBLE : {fileName} | date fichier={fileDate}");
            eligibleFiles.Add(file);
        }

        _logger.LogInformation($"Total fichiers éligibles pour le scan initial : {eligibleFiles.Count}");
        return eligibleFiles.OrderBy(f => f);
    }

    private string ExtractDateFromFilename(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string[] parts = fileName.Split('_');
        if (parts.Length < 2)
            return null;

        string dateblock = parts[1];

        // Extraction correcte des 14 caractères AAAAMMJJHHMMSS
        return dateblock.Length < 14 ? null : dateblock.Substring(0, 14);
    }

    public async Task ProcessFileIfEligibleAsync(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string fileDate = ExtractDateFromFilename(fileName);

        if (fileDate == null)
        {
            _logger.LogWarning($"Format incorrect : {fileName}");
            return;
        }

        // PATCH MINIMAL : suppression de la comparaison sur les dates
        if (!string.IsNullOrWhiteSpace(_lastProcessed) &&
            string.Equals(fileName, _lastProcessed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation($"Ignoré (déjà traité) : {fileName}");
            return;
        }

        // On conserve le startDatehourMin (contrôle logique et sain)
        if (!string.IsNullOrWhiteSpace(_config.StartDateHourMin) &&
            string.Compare(fileDate, _config.StartDateHourMin, StringComparison.Ordinal) < 0)
        {
            _logger.LogInformation($"Ignoré (avant startDatehourMin) : {fileName} | date fichier={fileDate}, startDate={_config.StartDateHourMin}");
            return;
        }

        _logger.LogInformation($"=== Début traitement : {fileName} ===");

        try
        {
            await ProcessFileAsync(filePath, fileName, fileDate);

            // IMPORTANT : on enregistre le NOM complet pour éviter les doublons réels
            SaveLastProcessed(fileName);

            _logger.LogInformation($"=== Fin traitement : {fileName} ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur complète lors du traitement : {fileName}");
        }
    }


    private async Task ProcessFileAsync(string fullPath, string fileName, string fileDate)
    {
        string yyyy = fileDate.Substring(0, 4);
        string mm = fileDate.Substring(4, 2);
        string dd = fileDate.Substring(6, 2);

        if (!_dailyStats.ContainsKey(yyyy + mm + dd))
            _dailyStats[yyyy + mm + dd] = new DailyStats();

        _dailyStats[yyyy + mm + dd].Detected++;

        // === NOUVEAU : dossier temporaire isolé ===
        string tempDir = Path.Combine(Path.GetTempPath(), "GGSN_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Fichiers temporaires
        string tempTxt = Path.Combine(tempDir, fileName.Replace(".gz", ""));     // txt extrait
        string tempFiltered = tempTxt;                                           // txt filtré

        try
        {
            // Extraction .gz → .txt
            DecompressGz(fullPath, tempTxt);

            // Filtrage
            bool hasResults = await FilterFileAsync(tempTxt);

            if (!hasResults)
            {
                _dailyStats[yyyy + mm + dd].Deleted++;
                _logger.LogInformation($"Aucun match → supprimé {fileName}");
                return;
            }

            _dailyStats[yyyy + mm + dd].Processed++;

            // Construction du fichier final
            string finalFile = $"{fileName.Replace(".txt.gz", "").Replace(".gz", "")}.txt.gz";
            string outDir = Path.Combine(_config.OutputFolder, "GGSN", yyyy, mm, dd);
            Directory.CreateDirectory(outDir);
            string finalPath = Path.Combine(outDir, finalFile);

            // Compression du fichier filtré
            CompressGz(tempFiltered, finalPath);

            _logger.LogInformation($"Fichier final écrit : {finalPath}");

            // FTP
            if (_config.SendFileFTP?.ToLower() == "oui")
                HandleFtpSending(finalPath, yyyy, mm, dd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur traitement complet : {fileName}");
        }
        finally
        {
            // === SUPPRESSION SYSTÉMATIQUE DU DOSSIER TEMP ===
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    _logger.LogInformation($"Dossier temporaire supprimé : {tempDir}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Impossible de supprimer le dossier temporaire : {tempDir}");
            }
        }
    }


    private void DecompressGz(string gzPath, string outputTxt)
    {
        using var fs = new FileStream(gzPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var writer = new FileStream(outputTxt, FileMode.Create, FileAccess.Write);
        gzip.CopyTo(writer);
    }

    private void CompressGz(string inputTxt, string outputGz)
    {
        using var fs = new FileStream(outputGz, FileMode.Create);
        using var gzip = new GZipStream(fs, CompressionLevel.Optimal);
        using var reader = new FileStream(inputTxt, FileMode.Open);
        reader.CopyTo(gzip);
    }

    private async Task<bool> FilterFileAsync(string txtFile)
    {
        bool hasMatches = false;
        string tempOut = txtFile + "_filtered.tmp";

        _logger.LogInformation($"Filtrage du fichier : {txtFile}");

        try
        {
            using (var reader = new StreamReader(txtFile, Encoding.UTF8))
            using (var writer = new StreamWriter(tempOut, false, Encoding.UTF8))
            {
                string header = await reader.ReadLineAsync();
                if (header == null)
                {
                    _logger.LogWarning($"Fichier vide ou header manquant : {txtFile}");
                    return false;
                }

                await writer.WriteLineAsync(header);

                string? line;
                int matchingCount = 0;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] cols = line.Split('|');
                    if (cols.Length < 222) continue;

                    string charging = cols[212].Trim();
                    string location = cols[221].Trim();
                    string ims = cols[211].Trim();

                    if (charging == "0800" &&
                        (location.StartsWith("061503") || location.StartsWith("161503") || location.StartsWith("61503")) &&
                        _openRecords.Contains(ims))
                    {
                        await writer.WriteLineAsync(line);
                        hasMatches = true;
                        matchingCount++;
                    }
                }

                _logger.LogInformation($"Nombre de lignes correspondantes dans {txtFile} : {matchingCount}");
            }

            if (hasMatches)
            {
                File.Delete(txtFile);
                File.Move(tempOut, txtFile);
                _logger.LogInformation($"Fichier filtré sauvegardé : {txtFile}");
            }
            else
            {
                File.Delete(tempOut);
                _logger.LogInformation($"Aucune correspondance trouvée, fichier supprimé : {txtFile}");
            }

            return hasMatches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur lors du filtrage du fichier : {txtFile}");
            if (File.Exists(tempOut)) File.Delete(tempOut);
            return false;
        }
    }

    private void HandleFtpSending(string finalFile, string yyyy, string mm, string dd)
    {
        string remoteDir = $"{_config.SftpSettings.UploadPath}/DATA/{yyyy}/{mm}";
        string successFile = Path.Combine(_config.SentFtpRecordsSuccess, $"sent_ftp_records_success_{yyyy}{mm}{dd}.txt");
        string failedFile = Path.Combine(_config.SentFtpRecordsFailed, $"sent_ftp_records_failed_{yyyy}{mm}{dd}.txt");

        try
        {
            bool sent = _ftp.SendFile(finalFile, remoteDir);

            if (sent)
            {
                _dailyStats[yyyy + mm + dd].FtpSuccess++;
                File.AppendAllText(successFile, Path.GetFileName(finalFile) + "\n");
                _logger.LogInformation($"FTP OK : {finalFile}");
            }
            else
            {
                _dailyStats[yyyy + mm + dd].FtpFailed++;
                File.AppendAllText(failedFile, Path.GetFileName(finalFile) + "\n");
                _logger.LogError($"FTP ÉCHEC : {finalFile}");
            }
        }
        catch (Exception ex)
        {
            _dailyStats[yyyy + mm + dd].FtpFailed++;
            File.AppendAllText(failedFile, Path.GetFileName(finalFile) + "\n");
            _logger.LogError(ex, $"FTP Crash pour {finalFile}");
        }
    }

    public void DumpStats()
    {
        foreach (var kv in _dailyStats)
        {
            string day = kv.Key;
            var stats = kv.Value;

            _logger.LogInformation($"=== Stats {day} ===");
            _logger.LogInformation($"Détectés : {stats.Detected}");
            _logger.LogInformation($"Traités : {stats.Processed}");
            _logger.LogInformation($"Supprimés : {stats.Deleted}");
            _logger.LogInformation($"FTP OK : {stats.FtpSuccess}");
            _logger.LogInformation($"FTP KO : {stats.FtpFailed}");
        }
    }
}

public class DailyStats
{
    public int Detected { get; set; }
    public int Processed { get; set; }
    public int Deleted { get; set; }
    public int FtpSuccess { get; set; }
    public int FtpFailed { get; set; }
}