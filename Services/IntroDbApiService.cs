using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    [Route("/IntroDbApp/DiagnoseItem", "GET", Summary = "Diagnostica los marcadores de capítulos en Emby para un título")]
    public class DiagnoseItemRequest : IReturn<object>
    {
        public string? ImdbId { get; set; }
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
                Version = "4.2.0",
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
                Cached404Count = config?.NotFoundIds?.Count ?? 0,
                OperationMode = config?.OperationMode ?? "IntroDbPrimary",
                RealtimeScanOnLibraryChange = config?.RealtimeScanOnLibraryChange ?? true,
                UserMetrics = config?.UserMetrics ?? new List<UserMetric>(),

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
            int count = config?.NotFoundIds?.Count ?? 0;
            if (config != null)
            {
                config.NotFoundIds.Clear();
                Plugin.Instance?.SaveConfiguration();
            }
            IntroDbActivityLogger.Log("Memoria Caché", count > 0 ? $"Depuración completada. Se liberaron {count} registros del índice de ausencias." : "La memoria caché de no encontrados (404) ya estaba vacía (0 elementos).", "info");
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

            long secs = request.Seconds > 0 ? request.Seconds : (request.IsIntro ? 85 : 90);
            
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
                "🧪 Prueba de Simulación",
                request.IsIntro ? "Salto de Intro Simulado" : "Salto de Créditos Simulado",
                "success",
                $"Usuario: {targetUser.UserName} | Ahorro: {secs}s"
            );

            return new
            {
                Success = true,
                Message = $"Simulación exitosa: Se registraron {secs}s ahorrados en {(request.IsIntro ? "Intro" : "Créditos")} para {targetUser.UserName}.",
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

            if (request.EndSec <= request.StartSec)
            {
                return new { Success = false, Message = "El tiempo de finalización debe ser estrictamente mayor al de inicio." };
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
    }
}