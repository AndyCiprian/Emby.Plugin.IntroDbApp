using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Tasks;
using IntroDbApp.Configuration;
using IntroDbApp.EntryPoints;

namespace IntroDbApp.Services
{
    [Route("/IntroDbApp/Stats", "GET", Summary = "Obtiene las métricas de rendimiento y estadísticas de IntroDB")]
    public class GetStatsRequest : IReturn<object> { }

    [Route("/IntroDbApp/Logs", "GET", Summary = "Obtiene el registro cronológico de eventos de IntroDB")]
    public class GetLogsRequest : IReturn<object> { }

    [Route("/IntroDbApp/Logs/Clear", "POST", Summary = "Limpia el registro cronológico de eventos")]
    public class ClearLogsRequest : IReturn<object> { }

    [Route("/IntroDbApp/Cache/Clear", "POST", Summary = "Depura la memoria caché de elementos no indexados")]
    public class ClearCacheRequest : IReturn<object> { }

    [Route("/IntroDbApp/Inspect", "GET", Summary = "Consulta los metadatos en vivo desde la API de IntroDB")]
    public class InspectRequest : IReturn<object>
    {
        public string? ImdbId { get; set; }
        public int Season { get; set; } = 1;
        public int Episode { get; set; } = 1;
        public bool IsMovie { get; set; } = false;
    }

    [Route("/IntroDbApp/ScanItem", "POST", Summary = "Escanea e inyecta marcas para un título específico por IMDb ID")]
    public class ScanItemRequest : IReturn<object>
    {
        public string? ImdbId { get; set; }
    }

    [Route("/IntroDbApp/Submit", "POST", Summary = "Envía timestamps validados a la base comunitaria de IntroDB.app")]
    public class SubmitSegmentRequest : IReturn<object>
    {
        public string? ImdbId { get; set; }
        public bool IsMovie { get; set; } = false;
        public string? SegmentType { get; set; } = "intro";
        public int? Season { get; set; } = 1;
        public int? Episode { get; set; } = 1;
        public double StartSec { get; set; }
        public double EndSec { get; set; }
    }

    [Route("/IntroDbApp/ApiKey/Validate", "POST", Summary = "Verifica la validez y estado de la clave de API con IntroDB.app")]
    public class ValidateApiKeyRequest : IReturn<object>
    {
        public string? ApiKey { get; set; }
    }

    [Route("/IntroDbApp/SimulateSkip", "POST", Summary = "Simula un salto de intro o créditos para verificar el motor de métricas")]
    public class SimulateSkipRequest : IReturn<object>
    {
        public bool IsIntro { get; set; } = true;
        public long Seconds { get; set; } = 85;
        public string? ImdbId { get; set; }
    }

    [Route("/IntroDbApp/DiagnoseItem", "GET", Summary = "Diagnostica los marcadores de capítulos en Emby para un título")]
    public class DiagnoseItemRequest : IReturn<object>
    {
        public string? ImdbId { get; set; }
    }

    [Route("/IntroDbApp/Series", "GET", Summary = "Obtiene la lista de todas las series en la biblioteca")]
    public class GetSeriesRequest : IReturn<object> { }

    [Route("/IntroDbApp/Series/{SeriesId}/Episodes", "GET", Summary = "Obtiene los episodios y sus capítulos de una serie")]
    public class GetSeriesEpisodesRequest : IReturn<object>
    {
        public string SeriesId { get; set; } = "";
    }

    [Route("/IntroDbApp/Chapters/Update", "POST", Summary = "Actualiza o inserta marcas de capítulos en un episodio")]
    public class UpdateEpisodeChapterRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = "";
        public double? IntroStartSec { get; set; }
        public double? IntroEndSec { get; set; }
        public double? CreditsStartSec { get; set; }
        public double? PostCreditsStartSec { get; set; }
    }

    [Route("/IntroDbApp/Exclusions", "POST", Summary = "Actualiza la lista de series excluidas de saltos")]
    public class UpdateExclusionsRequest : IReturn<object>
    {
        public List<string> ExcludedSeriesIds { get; set; } = new List<string>();
    }

    [Route("/IntroDbApp/Backup/Export", "POST", Summary = "Exporta todas las marcas de capítulos a un archivo JSON de respaldo")]
    public class ExportBackupRequest : IReturn<object>
    {
        public string? TargetDirectory { get; set; }
    }

    [Route("/IntroDbApp/Backup/Import", "POST", Summary = "Restaura marcas de capítulos desde un archivo JSON de respaldo")]
    public class ImportBackupRequest : IReturn<object>
    {
        public string FilePath { get; set; } = "";
    }

    public class BackupItemEntry
    {
        public string? ImdbId { get; set; }
        public string? SeriesName { get; set; }
        public string? Title { get; set; }
        public int Season { get; set; } = 1;
        public int Episode { get; set; } = 1;
        public bool IsMovie { get; set; } = false;
        public double? IntroStartSec { get; set; }
        public double? IntroEndSec { get; set; }
        public double? CreditsStartSec { get; set; }
        public double? PostCreditsStartSec { get; set; }
    }

    public class BackupFilePayload
    {
        public string ExportDate { get; set; } = "";
        public string PluginVersion { get; set; } = "4.3.0";
        public int TotalItems { get; set; }
        public List<BackupItemEntry> Items { get; set; } = new List<BackupItemEntry>();
    }

    public class IntroDbApiService : IService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly IntroDbApiClient _apiClient;

        public IntroDbApiService(ILibraryManager libraryManager, IItemRepository itemRepository, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logManager.GetLogger(GetType().Name);
            IntroDbActivityLogger.SystemLogger = _logger;
            _apiClient = new IntroDbApiClient(_logger);
        }

        public object Get(GetStatsRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            
            // Extracción estructurada del estado del último escaneo
            string lastScanRaw = config?.LastScanStatus ?? "No ejecutado";
            string scanState = "No ejecutado";
            string scanDate = "Nunca";
            string scanDuration = "0 s";

            if (lastScanRaw.StartsWith("Completado el "))
            {
                scanState = "Completado";
                try
                {
                    // Formato esperado: "Completado el 2026-10-28 22:09:12 (Duración: 144s)"
                    var parts = lastScanRaw.Replace("Completado el ", "").Split(new[] { " (Duración: " }, StringSplitOptions.None);
                    if (parts.Length > 0) scanDate = parts[0].Trim();
                    if (parts.Length > 1) scanDuration = parts[1].Replace(")", "").Trim();
                }
                catch
                {
                    scanDate = lastScanRaw;
                }
            }
            else
            {
                scanState = lastScanRaw;
            }

            return new
            {
                Version = Plugin.Instance?.Version.ToString(3) ?? "4.3.0",
                LastScanStatus = lastScanRaw,
                LastScanState = scanState,
                LastScanDate = scanDate,
                LastScanDuration = scanDuration,
                ScannedItemsCount = config?.ScannedItemsCount ?? 0,
                AddedIntrosCount = config?.AddedIntrosCount ?? 0,
                AddedCreditsCount = config?.AddedCreditsCount ?? 0,
                AddedPostCreditsCount = config?.AddedPostCreditsCount ?? 0,
                TotalLibraryEpisodes = config?.TotalLibraryEpisodes ?? 0,
                TotalEpisodesWithIntro = config?.TotalEpisodesWithIntro ?? 0,
                TotalEpisodesWithCredits = config?.TotalEpisodesWithCredits ?? 0,
                EstimatedSecondsSaved = config?.EstimatedSecondsSaved ?? 0,
                Cached404Count = config?.GetNotFoundCount() ?? 0,
                CacheTtlDays = config?.CacheTtlDays ?? 14,
                OperationMode = config?.OperationMode ?? "IntroDbPrimary",
                RealtimeScanOnLibraryChange = config?.RealtimeScanOnLibraryChange ?? true,
                UserMetrics = config?.UserMetrics ?? new List<UserMetric>(),

                // Respaldo y Exclusiones
                BackupPath = config?.BackupPath ?? string.Empty,
                DefaultServerBackupPath = Plugin.Instance?.AppPaths != null ? Path.Combine(Plugin.Instance.AppPaths.ProgramDataPath, "backups") : string.Empty,
                LastBackupDate = config?.LastBackupDate != null && config.LastBackupDate != DateTime.MinValue ? config.LastBackupDate.ToString("yyyy-MM-dd HH:mm:ss") : "Nunca",
                LastBackupCount = config?.LastBackupCount ?? 0,
                ExcludedSeriesCount = config?.AutoSkipExcludedSeriesIds?.Count ?? 0,
                AutoSkipExcludedSeriesIds = config?.AutoSkipExcludedSeriesIds ?? new List<string>(),

                // Estadísticas de API Key Comunitaria (Persistencia y Validación)
                ApiKey = config?.ApiKey ?? "",
                ApiKeyConfigured = !string.IsNullOrWhiteSpace(config?.ApiKey),
                IsApiKeyVerified = config?.IsApiKeyVerified ?? false,
                ApiKeyStatusMessage = config?.ApiKeyStatusMessage ?? "No configurada",
                ApiSubmissionsTotal = config?.ApiSubmissionsTotal ?? 0,
                ApiSubmissionsLive = config?.ApiSubmissionsLive ?? 0,
                ApiSubmissionsPending = config?.ApiSubmissionsPending ?? 0,
                ApiSubmissionsRejected = config?.ApiSubmissionsRejected ?? 0,
                ApiRateLimitRemaining = config?.ApiRateLimitRemaining ?? 100,
                ApiRateLimitLimit = config?.ApiRateLimitLimit ?? 100,
                ApiKeyLastChecked = config?.ApiKeyLastChecked != DateTime.MinValue 
                    ? config?.ApiKeyLastChecked.ToString("yyyy-MM-dd HH:mm:ss") 
                    : "Nunca"
            };
        }

        public object Get(GetLogsRequest request)
        {
            return IntroDbActivityLogger.GetRecentLogs();
        }

        public object Post(ClearLogsRequest request)
        {
            IntroDbActivityLogger.Clear();
            IntroDbActivityLogger.Log("Sistema", "Registro de eventos depurado", "info");
            return new { Success = true };
        }

        public object Post(ClearCacheRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            int count = config?.GetNotFoundCount() ?? 0;
            if (config != null)
            {
                config.NotFoundIds.Clear();
                Plugin.Instance?.SaveConfiguration();
            }
            IntroDbActivityLogger.Log("Memoria Caché", count > 0 ? $"Depuración completada. Se liberaron {count} registros del índice de ausencias (404)." : "La memoria caché de no encontrados (404) ya estaba vacía (0 elementos).", "info");
            return new { ClearedCount = count, AlreadyEmpty = count == 0 };
        }

        public async Task<object> Get(InspectRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ImdbId))
            {
                return new { Error = "Campo obligatorio: Debe ingresar un identificador IMDb válido (ejemplo: tt0903747)." };
            }

            if (request.IsMovie)
            {
                var result = await _apiClient.GetMovieSegmentsAsync(request.ImdbId.Trim(), CancellationToken.None).ConfigureAwait(false);
                return result ?? (object)new { Message = "No existen registros para esta película en la base comunitaria de IntroDB." };
            }
            else
            {
                var result = await _apiClient.GetEpisodeSegmentsAsync(request.ImdbId.Trim(), request.Season, request.Episode, CancellationToken.None).ConfigureAwait(false);
                return result ?? (object)new { Message = $"No existen registros en IntroDB para la temporada {request.Season}, episodio {request.Episode}." };
            }
        }

        public async Task<object> Post(ScanItemRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ImdbId))
            {
                return new { Success = false, Message = "Campo obligatorio: Debe ingresar un identificador IMDb (ejemplo: tt0903747)." };
            }

            string cleanImdb = request.ImdbId.Trim();
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled)
            {
                return new { Success = false, Message = "El complemento IntroDB se encuentra deshabilitado en Configuración." };
            }

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Episode>().Where(ep =>
            {
                var series = ep.Series;
                var id = series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);
                return string.Equals(id, cleanImdb, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Movie) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().Where(m =>
            {
                var id = m.GetProviderId(MetadataProviders.Imdb);
                return string.Equals(id, cleanImdb, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (episodes.Count == 0 && movies.Count == 0)
            {
                return new { Success = false, Message = $"No se encontraron medios en tu biblioteca de Emby con el identificador IMDb '{cleanImdb}'." };
            }

            int updatedCount = 0;

            foreach (var ep in episodes)
            {
                int season = ep.ParentIndexNumber ?? 1;
                int episodeNum = ep.IndexNumber ?? 1;
                var segments = await _apiClient.GetEpisodeSegmentsAsync(cleanImdb, season, episodeNum, CancellationToken.None).ConfigureAwait(false);
                if (segments == null) continue;

                var chapters = _itemRepository.GetChapters(ep)?.ToList() ?? new List<ChapterInfo>();
                bool modified = false;

                if (config.FetchIntros && segments.Intro != null)
                {
                    chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || (c.Name != null && c.Name.StartsWith("Intro", StringComparison.OrdinalIgnoreCase)));
                    chapters.Add(new ChapterInfo { Name = "Intro", MarkerType = MarkerType.IntroStart, StartPositionTicks = segments.Intro.StartMs * 10000 });
                    chapters.Add(new ChapterInfo { Name = "Intro End", MarkerType = MarkerType.IntroEnd, StartPositionTicks = segments.Intro.EndMs * 10000 });
                    config.AddedIntrosCount++;
                    modified = true;
                }

                if (config.FetchCredits && segments.Outro != null)
                {
                    chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                    chapters.Add(new ChapterInfo { Name = "Credits", MarkerType = MarkerType.CreditsStart, StartPositionTicks = segments.Outro.StartMs * 10000 });
                    chapters.Add(new ChapterInfo { Name = "Credits End", MarkerType = MarkerType.Chapter, StartPositionTicks = segments.Outro.EndMs * 10000 });
                    config.AddedCreditsCount++;
                    modified = true;
                }

                if (modified)
                {
                    var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                    for (int cIdx = 0; cIdx < ordered.Count; cIdx++) ordered[cIdx].ChapterIndex = cIdx;
                    _itemRepository.SaveChapters(ep.InternalId, ordered);
                    config.RemoveNotFound($"{cleanImdb}_S{season}E{episodeNum}");
                    updatedCount++;
                }
            }

            foreach (var movie in movies)
            {
                var segments = await _apiClient.GetMovieSegmentsAsync(cleanImdb, CancellationToken.None).ConfigureAwait(false);
                if (segments == null) continue;

                var chapters = _itemRepository.GetChapters(movie)?.ToList() ?? new List<ChapterInfo>();
                bool modified = false;

                if (config.FetchCredits && segments.Outro != null)
                {
                    chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                    chapters.Add(new ChapterInfo { Name = "Credits", MarkerType = MarkerType.CreditsStart, StartPositionTicks = segments.Outro.StartMs * 10000 });
                    chapters.Add(new ChapterInfo { Name = "Credits End", MarkerType = MarkerType.Chapter, StartPositionTicks = segments.Outro.EndMs * 10000 });
                    config.AddedCreditsCount++;
                    modified = true;
                }

                if (modified)
                {
                    var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                    for (int cIdx = 0; cIdx < ordered.Count; cIdx++) ordered[cIdx].ChapterIndex = cIdx;
                    _itemRepository.SaveChapters(movie.InternalId, ordered);
                    config.RemoveNotFound($"movie_{cleanImdb}");
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                Plugin.Instance?.SaveConfiguration();
                IntroDbActivityLogger.Log(cleanImdb, "Escaneo Específico Completado", "success", $"Se inyectaron marcadores en {updatedCount} elementos.");
            }

            return new
            {
                Success = true,
                UpdatedCount = updatedCount,
                TotalFound = episodes.Count + movies.Count,
                Message = $"Se encontraron {episodes.Count + movies.Count} elementos en la biblioteca y se inyectaron marcadores en {updatedCount} de ellos."
            };
        }

        public async Task<object> Post(ValidateApiKeyRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            string keyToTest = request.ApiKey != null 
                ? request.ApiKey.Trim() 
                : (config?.ApiKey ?? "");

            if (string.IsNullOrWhiteSpace(keyToTest))
            {
                if (config != null)
                {
                    config.ApiKey = "";
                    config.IsApiKeyVerified = false;
                    config.ApiKeyStatusMessage = "No configurada";
                    Plugin.Instance?.SaveConfiguration();
                }
                return new 
                { 
                    IsValid = false, 
                    Message = "No se ha ingresado una clave de API.",
                    RateLimitRemaining = 100,
                    RateLimitLimit = 100,
                    LastChecked = "Nunca"
                };
            }

            var (isValid, message, remaining, limit) = await _apiClient.ValidateApiKeyAsync(keyToTest, CancellationToken.None).ConfigureAwait(false);

            if (config != null)
            {
                config.ApiKey = keyToTest;
                config.IsApiKeyVerified = isValid;
                config.ApiKeyStatusMessage = message;
                config.ApiRateLimitRemaining = remaining;
                config.ApiRateLimitLimit = limit;
                config.ApiKeyLastChecked = DateTime.UtcNow;
                Plugin.Instance?.SaveConfiguration();
            }

            IntroDbActivityLogger.Log(
                "Autenticación IntroDB",
                isValid ? "Clave de API Verificada" : "Fallo en Verificación de API",
                isValid ? "success" : "warn",
                message
            );

            return new
            {
                IsValid = isValid,
                Message = message,
                RateLimitRemaining = remaining,
                RateLimitLimit = limit,
                LastChecked = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public object Post(SimulateSkipRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return new { Success = false, Message = "Configuración no disponible." };

            if (string.IsNullOrWhiteSpace(request.ImdbId))
            {
                return new 
                { 
                    Success = false, 
                    Message = "Campo obligatorio: Debes ingresar un identificador IMDb válido en el campo superior antes de simular el salto para asociar la reproducción a un medio real." 
                };
            }

            string cleanImdb = request.ImdbId.Trim();

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Episode>().Where(ep =>
            {
                var series = ep.Series;
                var id = series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);
                return string.Equals(id, cleanImdb, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Movie) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().Where(m =>
            {
                var id = m.GetProviderId(MetadataProviders.Imdb);
                return string.Equals(id, cleanImdb, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (episodes.Count == 0 && movies.Count == 0)
            {
                return new 
                { 
                    Success = false, 
                    Message = $"El identificador IMDb '{cleanImdb}' no se encuentra en tu biblioteca de Emby. Ingresa un título existente para simular su reproducción." 
                };
            }

            var targetItem = (BaseItem?)episodes.FirstOrDefault() ?? movies.FirstOrDefault();
            var chapters = _itemRepository.GetChapters(targetItem!)?.ToList() ?? new List<ChapterInfo>();

            long secs = request.Seconds;
            if (request.IsIntro)
            {
                var introStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                var introEnd = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);
                if (introStart != null && introEnd != null && introEnd.StartPositionTicks > introStart.StartPositionTicks)
                {
                    secs = (introEnd.StartPositionTicks - introStart.StartPositionTicks) / 10_000_000L;
                }
                else if (secs <= 0)
                {
                    secs = 85;
                }
            }
            else
            {
                var creditsStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                if (creditsStart != null)
                {
                    long creditsEnd = chapters.FirstOrDefault(c => c.Name == "Credits End")?.StartPositionTicks ?? (targetItem?.RunTimeTicks ?? 0L);
                    if (creditsEnd > creditsStart.StartPositionTicks)
                    {
                        secs = (creditsEnd - creditsStart.StartPositionTicks) / 10_000_000L;
                    }
                }
                if (secs <= 0) secs = 90;
            }

            if (config.UserMetrics == null || config.UserMetrics.Count == 0)
            {
                config.UserMetrics = new List<UserMetric>
                {
                    new UserMetric
                    {
                        UserId = "default_user",
                        UserName = "MiNubeDebianOS",
                        LastActivity = DateTime.UtcNow
                    }
                };
            }

            var targetUser = config.UserMetrics.First();
            IntroDbPlaybackEntryPoint.RecordUserSkip(config, targetUser.UserId, targetUser.UserName, request.IsIntro, secs);

            IntroDbActivityLogger.Log(
                $"{targetItem?.Name ?? cleanImdb} ({cleanImdb})",
                request.IsIntro ? "Salto de Intro Simulado" : "Salto de Créditos Simulado",
                "success",
                $"Usuario: {targetUser.UserName} | Ahorro medido: {secs}s"
            );

            return new
            {
                Success = true,
                ItemName = targetItem?.Name,
                ImdbId = cleanImdb,
                Message = $"Simulación exitosa en '{targetItem?.Name}': Se registraron {secs}s ahorrados en {(request.IsIntro ? "Intro" : "Créditos")} para {targetUser.UserName}.",
                TotalSaved = config.EstimatedSecondsSaved,
                UserIntros = targetUser.IntrosSkipped,
                UserOutros = targetUser.OutrosSkipped,
                UserSeconds = targetUser.SecondsSaved
            };
        }

        public async Task<object> Get(DiagnoseItemRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ImdbId))
            {
                return new { Success = false, Message = "Campo obligatorio: Identificador IMDb requerido." };
            }

            string cleanImdb = request.ImdbId.Trim();
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Episode>().Where(ep =>
            {
                var series = ep.Series;
                var id = series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);
                return string.Equals(id, cleanImdb, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Movie) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().Where(m =>
            {
                var id = m.GetProviderId(MetadataProviders.Imdb);
                return string.Equals(id, cleanImdb, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (episodes.Count == 0 && movies.Count == 0)
            {
                return new
                {
                    Success = false,
                    Message = $"No se encontró ningún elemento en la biblioteca de Emby con IMDb '{cleanImdb}'."
                };
            }

            var sampleItem = (BaseItem?)episodes.FirstOrDefault() ?? movies.FirstOrDefault();
            var chapters = _itemRepository.GetChapters(sampleItem!)?.ToList() ?? new List<ChapterInfo>();
            
            var introStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
            var introEnd = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);
            var creditsStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));

            var apiSegments = sampleItem is Movie
                ? await _apiClient.GetMovieSegmentsAsync(cleanImdb, CancellationToken.None).ConfigureAwait(false)
                : await _apiClient.GetEpisodeSegmentsAsync(cleanImdb, 1, 1, CancellationToken.None).ConfigureAwait(false);

            return new
            {
                Success = true,
                Title = sampleItem?.Name,
                ImdbId = cleanImdb,
                Type = sampleItem is Movie ? "Película" : "Serie de TV",
                TotalLibraryItems = episodes.Count + movies.Count,
                HasIntroMarker = introStart != null && introEnd != null,
                IntroStartSec = introStart != null ? Math.Round((double)introStart.StartPositionTicks / 10_000_000.0, 1) : (double?)null,
                IntroEndSec = introEnd != null ? Math.Round((double)introEnd.StartPositionTicks / 10_000_000.0, 1) : (double?)null,
                HasCreditsMarker = creditsStart != null,
                CreditsStartSec = creditsStart != null ? Math.Round((double)creditsStart.StartPositionTicks / 10_000_000.0, 1) : (double?)null,
                Chapters = chapters.Select(c => new
                {
                    c.Name,
                    Marker = c.MarkerType.ToString(),
                    StartSeconds = Math.Round((double)c.StartPositionTicks / 10_000_000.0, 1)
                }),
                IntroDbApiHasData = apiSegments != null && (apiSegments.Intro != null || apiSegments.Outro != null)
            };
        }

        public async Task<object> Post(SubmitSegmentRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            string apiKey = config?.ApiKey ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new { Success = false, Message = "Debe configurar su clave de API de IntroDB en la pestaña de Configuración." };
            }

            if (string.IsNullOrWhiteSpace(request.ImdbId))
            {
                return new { Success = false, Message = "Campo obligatorio: Identificador IMDb requerido." };
            }

            if (request.StartSec < 0)
            {
                return new { Success = false, Message = "El tiempo de inicio no puede ser un valor negativo." };
            }

            if (request.EndSec <= request.StartSec)
            {
                return new { Success = false, Message = "El tiempo de finalización debe ser estrictamente mayor al de inicio." };
            }

            if (!request.IsMovie && ((request.Season ?? 0) <= 0 || (request.Episode ?? 0) <= 0))
            {
                return new { Success = false, Message = "Para series de TV, la temporada y el episodio deben ser números mayores a cero." };
            }

            var payload = new SubmitSegmentPayload
            {
                ImdbId = request.ImdbId.Trim(),
                IsMovie = request.IsMovie ? true : null,
                SegmentType = request.SegmentType ?? "intro",
                Season = request.IsMovie ? null : request.Season,
                Episode = request.IsMovie ? null : request.Episode,
                StartSec = request.StartSec,
                EndSec = request.EndSec
            };

            var (success, message) = await _apiClient.SubmitSegmentAsync(apiKey, payload, CancellationToken.None).ConfigureAwait(false);
            if (success && config != null)
            {
                config.ApiSubmissionsTotal++;
                config.ApiSubmissionsLive++;
                Plugin.Instance?.SaveConfiguration();

                IntroDbActivityLogger.Log(
                    $"{request.ImdbId} ({(request.IsMovie ? "Película" : $"T{request.Season}E{request.Episode}")})",
                    "Aporte Comunitario Enviado",
                    "success",
                    $"{request.SegmentType?.ToUpperInvariant()}: {request.StartSec:F1}s a {request.EndSec:F1}s"
                );
            }
            return new { Success = success, Message = message };
        }

        public object Get(GetSeriesRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            var excluded = config?.AutoSkipExcludedSeriesIds ?? new List<string>();

            var seriesList = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Series) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Series>().OrderBy(s => s.Name).Select(s =>
            {
                var imdbId = s.GetProviderId(MetadataProviders.Imdb) ?? "";
                var seasons = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Parent = s,
                    IncludeItemTypes = new[] { nameof(Season) },
                    Recursive = false
                });
                var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    AncestorIds = new[] { s.InternalId },
                    IncludeItemTypes = new[] { nameof(Episode) },
                    Recursive = true
                });

                return new
                {
                    Id = s.Id.ToString(),
                    Name = s.Name,
                    ImdbId = imdbId,
                    Year = s.ProductionYear,
                    SeasonCount = seasons.Length > 0 ? seasons.Length : 1,
                    EpisodeCount = episodes.Length,
                    IsExcluded = excluded.Contains(s.Id.ToString())
                };
            }).ToList();

            return new
            {
                Success = true,
                TotalCount = seriesList.Count,
                Series = seriesList,
                ExcludedSeriesIds = excluded
            };
        }

        public object Get(GetSeriesEpisodesRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SeriesId))
            {
                return new { Success = false, Message = "SeriesId es requerido." };
            }

            Series? series = null;
            if (Guid.TryParse(request.SeriesId, out var seriesGuid))
            {
                series = _libraryManager.GetItemById(seriesGuid) as Series;
            }

            if (series == null)
            {
                series = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Series) },
                    Recursive = true
                }).OfType<Series>().FirstOrDefault(s => s.Id.ToString().Equals(request.SeriesId, StringComparison.OrdinalIgnoreCase));
            }

            if (series == null)
            {
                return new { Success = false, Message = $"No se encontró la serie con ID '{request.SeriesId}'." };
            }

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = new[] { series.InternalId },
                IncludeItemTypes = new[] { nameof(Episode) },
                Recursive = true
            }).OfType<Episode>().OrderBy(e => e.ParentIndexNumber ?? 1).ThenBy(e => e.IndexNumber ?? 1).Select(ep =>
            {
                var chapters = _itemRepository.GetChapters(ep)?.ToList() ?? new List<ChapterInfo>();
                var introStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                var introEnd = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);
                var creditsStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                var postCredits = chapters.FirstOrDefault(c => c.Name != null && c.Name.Equals("Post Credits", StringComparison.OrdinalIgnoreCase));

                double durSec = Math.Round((double)(ep.RunTimeTicks ?? 0L) / 10_000_000.0, 1);

                return new
                {
                    Id = ep.Id.ToString(),
                    Name = ep.Name,
                    Season = ep.ParentIndexNumber ?? 1,
                    EpisodeNumber = ep.IndexNumber ?? 1,
                    DurationSec = durSec,
                    HasIntro = introStart != null && introEnd != null,
                    IntroStartSec = introStart != null ? Math.Round((double)introStart.StartPositionTicks / 10_000_000.0, 1) : (double?)null,
                    IntroEndSec = introEnd != null ? Math.Round((double)introEnd.StartPositionTicks / 10_000_000.0, 1) : (double?)null,
                    HasCredits = creditsStart != null,
                    CreditsStartSec = creditsStart != null ? Math.Round((double)creditsStart.StartPositionTicks / 10_000_000.0, 1) : (double?)null,
                    HasPostCredits = postCredits != null,
                    PostCreditsStartSec = postCredits != null ? Math.Round((double)postCredits.StartPositionTicks / 10_000_000.0, 1) : (double?)null
                };
            }).ToList();

            return new
            {
                Success = true,
                SeriesId = series.Id.ToString(),
                SeriesName = series.Name,
                TotalEpisodes = episodes.Count,
                Episodes = episodes
            };
        }

        public object Post(UpdateEpisodeChapterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EpisodeId) || !Guid.TryParse(request.EpisodeId, out var epGuid))
            {
                return new { Success = false, Message = "EpisodeId válido requerido." };
            }

            var ep = _libraryManager.GetItemById(epGuid) as Episode;
            if (ep == null)
            {
                return new { Success = false, Message = $"No se encontró el episodio con ID '{request.EpisodeId}'." };
            }

            double durationSeconds = (ep.RunTimeTicks ?? 0L) / 10_000_000.0;
            if (durationSeconds <= 0) durationSeconds = 3600.0;
            double safeMax = Math.Max(durationSeconds - 1.0, 1.0);

            var chapters = _itemRepository.GetChapters(ep)?.ToList() ?? new List<ChapterInfo>();

            if (request.IntroStartSec.HasValue && request.IntroEndSec.HasValue)
            {
                double start = Math.Max(0.0, Math.Min(request.IntroStartSec.Value, safeMax));
                double end = Math.Max(start + 1.0, Math.Min(request.IntroEndSec.Value, safeMax));

                chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd || (c.Name != null && c.Name.StartsWith("Intro", StringComparison.OrdinalIgnoreCase)));
                chapters.Add(new ChapterInfo
                {
                    Name = "Intro",
                    MarkerType = MarkerType.IntroStart,
                    StartPositionTicks = (long)(start * 10_000_000L)
                });
                chapters.Add(new ChapterInfo
                {
                    Name = "Intro End",
                    MarkerType = MarkerType.IntroEnd,
                    StartPositionTicks = (long)(end * 10_000_000L)
                });
            }

            if (request.CreditsStartSec.HasValue)
            {
                double creditsStart = Math.Max(0.0, Math.Min(request.CreditsStartSec.Value, safeMax));
                chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                chapters.Add(new ChapterInfo
                {
                    Name = "Credits",
                    MarkerType = MarkerType.CreditsStart,
                    StartPositionTicks = (long)(creditsStart * 10_000_000L)
                });
                chapters.Add(new ChapterInfo
                {
                    Name = "Credits End",
                    MarkerType = MarkerType.Chapter,
                    StartPositionTicks = (long)(durationSeconds * 10_000_000L)
                });
            }

            if (request.PostCreditsStartSec.HasValue)
            {
                double postStart = Math.Max(0.0, Math.Min(request.PostCreditsStartSec.Value, safeMax));
                chapters.RemoveAll(c => c.Name != null && c.Name.Equals("Post Credits", StringComparison.OrdinalIgnoreCase));
                chapters.Add(new ChapterInfo
                {
                    Name = "Post Credits",
                    MarkerType = MarkerType.Chapter,
                    StartPositionTicks = (long)(postStart * 10_000_000L)
                });
            }

            var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].ChapterIndex = i;
            _itemRepository.SaveChapters(ep.InternalId, ordered);

            IntroDbActivityLogger.Log(
                $"{ep.SeriesName} S{ep.ParentIndexNumber}E{ep.IndexNumber}",
                "Capítulos Editados Manualmente",
                "info",
                $"Intro: {request.IntroStartSec}s-{request.IntroEndSec}s | Créditos: {request.CreditsStartSec}s | Post-Créditos: {request.PostCreditsStartSec}s"
            );

            return new { Success = true, Message = $"Capítulos actualizados exitosamente para '{ep.Name}'." };
        }

        public object Post(UpdateExclusionsRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            int count = 0;
            if (config != null)
            {
                config.AutoSkipExcludedSeriesIds = request.ExcludedSeriesIds ?? new List<string>();
                count = config.AutoSkipExcludedSeriesIds.Count;
                Plugin.Instance?.SaveConfiguration();
            }

            IntroDbActivityLogger.Log("Lista de Exclusiones", "Exclusiones Actualizadas", "info", $"{count} series configuradas para omisión de saltos automáticos.");

            return new
            {
                Success = true,
                ExcludedCount = count,
                Message = $"Se guardaron {count} series excluidas de saltos automáticos."
            };
        }

        public object Post(ExportBackupRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            string defaultServerDir = Plugin.Instance?.AppPaths != null ? Path.Combine(Plugin.Instance.AppPaths.ProgramDataPath, "backups") : "";
            string targetDir = !string.IsNullOrWhiteSpace(request.TargetDirectory)
                ? request.TargetDirectory.Trim()
                : (!string.IsNullOrWhiteSpace(config?.BackupPath) ? config.BackupPath : defaultServerDir);

            if (string.IsNullOrWhiteSpace(targetDir))
            {
                return new { Success = false, Message = "Debes especificar o seleccionar una carpeta de destino válida en el servidor." };
            }

            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    IsVirtualItem = false,
                    Recursive = true
                }).OfType<Episode>().ToList();

                var movies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Movie) },
                    IsVirtualItem = false,
                    Recursive = true
                }).OfType<Movie>().ToList();

                var backupItems = new List<BackupItemEntry>();

                foreach (var ep in episodes)
                {
                    var chapters = _itemRepository.GetChapters(ep)?.ToList();
                    if (chapters == null || chapters.Count == 0) continue;

                    var introStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                    var introEnd = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);
                    var creditsStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                    var postCredits = chapters.FirstOrDefault(c => c.Name != null && c.Name.Equals("Post Credits", StringComparison.OrdinalIgnoreCase));

                    if (introStart == null && creditsStart == null && postCredits == null) continue;

                    string? imdb = ep.Series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);

                    backupItems.Add(new BackupItemEntry
                    {
                        ImdbId = imdb,
                        SeriesName = ep.SeriesName,
                        Title = ep.Name,
                        Season = ep.ParentIndexNumber ?? 1,
                        Episode = ep.IndexNumber ?? 1,
                        IsMovie = false,
                        IntroStartSec = introStart != null ? Math.Round((double)introStart.StartPositionTicks / 10_000_000.0, 1) : null,
                        IntroEndSec = introEnd != null ? Math.Round((double)introEnd.StartPositionTicks / 10_000_000.0, 1) : null,
                        CreditsStartSec = creditsStart != null ? Math.Round((double)creditsStart.StartPositionTicks / 10_000_000.0, 1) : null,
                        PostCreditsStartSec = postCredits != null ? Math.Round((double)postCredits.StartPositionTicks / 10_000_000.0, 1) : null
                    });
                }

                foreach (var movie in movies)
                {
                    var chapters = _itemRepository.GetChapters(movie)?.ToList();
                    if (chapters == null || chapters.Count == 0) continue;

                    var creditsStart = chapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                    var postCredits = chapters.FirstOrDefault(c => c.Name != null && c.Name.Equals("Post Credits", StringComparison.OrdinalIgnoreCase));

                    if (creditsStart == null && postCredits == null) continue;

                    string? imdb = movie.GetProviderId(MetadataProviders.Imdb);

                    backupItems.Add(new BackupItemEntry
                    {
                        ImdbId = imdb,
                        SeriesName = null,
                        Title = movie.Name,
                        Season = 0,
                        Episode = 0,
                        IsMovie = true,
                        IntroStartSec = null,
                        IntroEndSec = null,
                        CreditsStartSec = creditsStart != null ? Math.Round((double)creditsStart.StartPositionTicks / 10_000_000.0, 1) : null,
                        PostCreditsStartSec = postCredits != null ? Math.Round((double)postCredits.StartPositionTicks / 10_000_000.0, 1) : null
                    });
                }

                var payload = new BackupFilePayload
                {
                    ExportDate = DateTime.UtcNow.ToString("o"),
                    PluginVersion = Plugin.Instance?.Version.ToString(3) ?? "4.3.0",
                    TotalItems = backupItems.Count,
                    Items = backupItems
                };

                string fileName = $"introdb_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                string fullPath = Path.Combine(targetDir, fileName);

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(payload, jsonOptions);
                File.WriteAllText(fullPath, jsonString);

                if (config != null)
                {
                    config.BackupPath = targetDir;
                    config.LastBackupDate = DateTime.UtcNow;
                    config.LastBackupCount = backupItems.Count;
                    Plugin.Instance?.SaveConfiguration();
                }

                IntroDbActivityLogger.Log("Sistema de Respaldo", "Respaldo JSON Exportado", "success", $"{backupItems.Count} títulos guardados en {fileName}");

                return new
                {
                    Success = true,
                    FilePath = fullPath,
                    FileName = fileName,
                    TotalItems = backupItems.Count,
                    Message = $"Respaldo generado exitosamente con {backupItems.Count} elementos en '{fullPath}'."
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"IntroDB.app: Error exportando respaldo JSON: {ex.Message}");
                return new { Success = false, Message = $"Error exportando respaldo: {ex.Message}" };
            }
        }

        public object Post(ImportBackupRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
            {
                return new { Success = false, Message = "Archivo de respaldo no encontrado en la ruta especificada." };
            }

            try
            {
                string jsonString = File.ReadAllText(request.FilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var payload = JsonSerializer.Deserialize<BackupFilePayload>(jsonString, options);

                if (payload == null || payload.Items == null || payload.Items.Count == 0)
                {
                    return new { Success = false, Message = "El archivo de respaldo no contiene elementos válidos." };
                }

                int restoredCount = 0;

                foreach (var item in payload.Items)
                {
                    if (item.IsMovie)
                    {
                        var movies = _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { nameof(Movie) },
                            Recursive = true
                        }).OfType<Movie>().Where(m =>
                        {
                            if (!string.IsNullOrEmpty(item.ImdbId))
                            {
                                return string.Equals(m.GetProviderId(MetadataProviders.Imdb), item.ImdbId, StringComparison.OrdinalIgnoreCase);
                            }
                            return string.Equals(m.Name, item.Title, StringComparison.OrdinalIgnoreCase);
                        }).ToList();

                        foreach (var m in movies)
                        {
                            var chapters = _itemRepository.GetChapters(m)?.ToList() ?? new List<ChapterInfo>();
                            bool mod = false;

                            if (item.CreditsStartSec.HasValue)
                            {
                                chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                                chapters.Add(new ChapterInfo
                                {
                                    Name = "Credits",
                                    MarkerType = MarkerType.CreditsStart,
                                    StartPositionTicks = (long)(item.CreditsStartSec.Value * 10_000_000L)
                                });
                                mod = true;
                            }

                            if (item.PostCreditsStartSec.HasValue)
                            {
                                chapters.RemoveAll(c => c.Name != null && c.Name.Equals("Post Credits", StringComparison.OrdinalIgnoreCase));
                                chapters.Add(new ChapterInfo
                                {
                                    Name = "Post Credits",
                                    MarkerType = MarkerType.Chapter,
                                    StartPositionTicks = (long)(item.PostCreditsStartSec.Value * 10_000_000L)
                                });
                                mod = true;
                            }

                            if (mod)
                            {
                                var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                                for (int i = 0; i < ordered.Count; i++) ordered[i].ChapterIndex = i;
                                _itemRepository.SaveChapters(m.InternalId, ordered);
                                restoredCount++;
                            }
                        }
                    }
                    else
                    {
                        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { nameof(Episode) },
                            Recursive = true
                        }).OfType<Episode>().Where(ep =>
                        {
                            if (!string.IsNullOrEmpty(item.ImdbId))
                            {
                                var sImdb = ep.Series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);
                                if (!string.Equals(sImdb, item.ImdbId, StringComparison.OrdinalIgnoreCase)) return false;
                            }
                            else if (!string.IsNullOrEmpty(item.SeriesName))
                            {
                                if (!string.Equals(ep.SeriesName, item.SeriesName, StringComparison.OrdinalIgnoreCase)) return false;
                            }

                            return (ep.ParentIndexNumber ?? 1) == item.Season && (ep.IndexNumber ?? 1) == item.Episode;
                        }).ToList();

                        foreach (var ep in episodes)
                        {
                            var chapters = _itemRepository.GetChapters(ep)?.ToList() ?? new List<ChapterInfo>();
                            bool mod = false;

                            if (item.IntroStartSec.HasValue && item.IntroEndSec.HasValue)
                            {
                                chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd || (c.Name != null && c.Name.StartsWith("Intro", StringComparison.OrdinalIgnoreCase)));
                                chapters.Add(new ChapterInfo
                                {
                                    Name = "Intro",
                                    MarkerType = MarkerType.IntroStart,
                                    StartPositionTicks = (long)(item.IntroStartSec.Value * 10_000_000L)
                                });
                                chapters.Add(new ChapterInfo
                                {
                                    Name = "Intro End",
                                    MarkerType = MarkerType.IntroEnd,
                                    StartPositionTicks = (long)(item.IntroEndSec.Value * 10_000_000L)
                                });
                                mod = true;
                            }

                            if (item.CreditsStartSec.HasValue)
                            {
                                chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                                chapters.Add(new ChapterInfo
                                {
                                    Name = "Credits",
                                    MarkerType = MarkerType.CreditsStart,
                                    StartPositionTicks = (long)(item.CreditsStartSec.Value * 10_000_000L)
                                });
                                mod = true;
                            }

                            if (item.PostCreditsStartSec.HasValue)
                            {
                                chapters.RemoveAll(c => c.Name != null && c.Name.Equals("Post Credits", StringComparison.OrdinalIgnoreCase));
                                chapters.Add(new ChapterInfo
                                {
                                    Name = "Post Credits",
                                    MarkerType = MarkerType.Chapter,
                                    StartPositionTicks = (long)(item.PostCreditsStartSec.Value * 10_000_000L)
                                });
                                mod = true;
                            }

                            if (mod)
                            {
                                var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                                for (int i = 0; i < ordered.Count; i++) ordered[i].ChapterIndex = i;
                                _itemRepository.SaveChapters(ep.InternalId, ordered);
                                restoredCount++;
                            }
                        }
                    }
                }

                IntroDbActivityLogger.Log("Sistema de Respaldo", "Respaldo JSON Importado", "success", $"Se restauraron {restoredCount} elementos desde {Path.GetFileName(request.FilePath)}");

                return new
                {
                    Success = true,
                    RestoredCount = restoredCount,
                    TotalInBackup = payload.Items.Count,
                    Message = $"Se restauraron marcas de capítulos en {restoredCount} elementos de tu biblioteca con éxito."
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"IntroDB.app: Error importando respaldo JSON: {ex.Message}");
                return new { Success = false, Message = $"Error al procesar archivo de respaldo: {ex.Message}" };
            }
        }
    }
}