using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly FileProcessor _processor;
    private FileSystemWatcher _watcher;

    private readonly ConcurrentQueue<string> _fileQueue = new();
    private readonly SemaphoreSlim _queueSemaphore = new(0);

    // Utilisation d'un dictionnaire pour un verrouillage per fichier
    private readonly ConcurrentDictionary<string, object> _fileLocks = new();

    public Worker(ILogger<Worker> logger, FileProcessor processor)
    {
        _logger = logger;
        _processor = processor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Service GGSN Extract - Démarrage ===");

        // Démarre le watcher
        StartWatcher();

        // Traite les fichiers existants lors du démarrage
        await ProcessInitialFilesAsync(stoppingToken);

        // Traite les nouveaux fichiers détectés
        await ProcessWatcherQueueAsync(stoppingToken);
    }

    private async Task ProcessInitialFilesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scan initial démarré...");

        var initialFiles = _processor.GetEligibleFilesFromInitialScan().ToList();
        _logger.LogInformation($"{initialFiles.Count} fichiers éligibles trouvés pour le scan initial.");

        foreach (var file in initialFiles)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                _logger.LogInformation($"Scan initial : traitement du fichier {Path.GetFileName(file)}");
                await _processor.ProcessFileIfEligibleAsync(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur traitement fichier initial : {file}");
            }
        }

        _logger.LogInformation("Scan initial terminé.");
    }

    private void StartWatcher()
    {
        string sourceDir = _processor.Config.SourceDirectory;

        if (!Directory.Exists(sourceDir))
        {
            _logger.LogWarning($"Dossier source introuvable pour le watcher : {sourceDir}");
            return;
        }

        // Crée et configure le watcher
        _watcher = new FileSystemWatcher(sourceDir, "*.gz")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024 // Augmenter la taille du tampon à 64 Ko
        };

        // Gestion de l'événement Created
        _watcher.Created += (s, e) =>
        {
            try
            {
                var fileInfo = new FileInfo(e.FullPath);

                // Si le fichier fait 0 Ko, on l'ignore
                if (fileInfo.Length == 0)
                {
                    _logger.LogInformation($"Fichier créé avec taille 0 Ko, ignoré pour le moment : {Path.GetFileName(e.FullPath)}");
                    return;
                }

                // Vérifie si le fichier est déjà en cours de traitement via un verrouillage
                if (_fileLocks.TryAdd(e.FullPath, new object()))
                {
                    _logger.LogInformation($"Fichier créé et prêt à être traité : {Path.GetFileName(e.FullPath)}");
                    _fileQueue.Enqueue(e.FullPath);
                    _queueSemaphore.Release();
                }
                else
                {
                    _logger.LogInformation($"Le fichier {Path.GetFileName(e.FullPath)} est déjà en cours de traitement, ignoré.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de l'ajout du fichier à la queue : {e.FullPath}");
            }
        };

        // Gestion de l'événement Changed
        _watcher.Changed += (s, e) =>
        {
            try
            {
                var fileInfo = new FileInfo(e.FullPath);

                // Si la taille du fichier est > 0 Ko et qu'il n'est pas déjà en traitement
                if (fileInfo.Length > 0 && !_fileLocks.ContainsKey(e.FullPath))
                {
                    // Vérifie si le fichier est déjà en cours de traitement via un verrouillage
                    if (_fileLocks.TryAdd(e.FullPath, new object()))
                    {
                        _logger.LogInformation($"Fichier modifié et prêt à être traité : {Path.GetFileName(e.FullPath)}");
                        _fileQueue.Enqueue(e.FullPath);
                        _queueSemaphore.Release();
                    }
                    else
                    {
                        _logger.LogInformation($"Le fichier {Path.GetFileName(e.FullPath)} est déjà en cours de traitement, ignoré.");
                    }
                }
                else
                {
                    _logger.LogInformation($"Fichier modifié mais avec taille 0 Ko ou déjà traité, ignoré : {Path.GetFileName(e.FullPath)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du traitement du fichier modifié : {e.FullPath}");
            }
        };

        // Gérer les erreurs de FileSystemWatcher
        _watcher.Error += (s, e) =>
        {
            _logger.LogError($"Erreur dans le FileSystemWatcher : {e.GetException().Message}");
            RestartWatcher();
        };

        _logger.LogInformation("FileSystemWatcher démarré pour les nouveaux fichiers.");
    }

    private async Task ProcessWatcherQueueAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _queueSemaphore.WaitAsync(stoppingToken);

                while (_fileQueue.TryDequeue(out var filePath))
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        for (int i = 0; i < 10; i++) // Réessaye jusqu'à 10 fois si le fichier est en cours d'utilisation
                        {
                            try
                            {
                                using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                                break;
                            }
                            catch
                            {
                                await Task.Delay(200); // Attends 200 ms avant de réessayer
                            }
                        }

                        await _processor.ProcessFileIfEligibleAsync(filePath);

                        // Libère le verrou après traitement
                        _fileLocks.TryRemove(filePath, out _);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erreur traitement fichier dans la queue : {filePath}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Dispose le watcher et log l'arrêt du service
        _watcher?.Dispose();
        _logger.LogInformation("Service GGSN Extract arrêté.");
        await base.StopAsync(cancellationToken);
    }

    private void RestartWatcher()
    {
        // Redémarre le FileSystemWatcher en cas d'erreur ou de problème
        _logger.LogWarning("Redémarrage du FileSystemWatcher...");
        _watcher?.Dispose();
        StartWatcher(); // Redémarre le watcher
    }
}