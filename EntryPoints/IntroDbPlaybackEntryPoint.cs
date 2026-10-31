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
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using IntroDbApp.Configuration;
using IntroDbApp.Services;

namespace IntroDbApp.EntryPoints
{
    public class IntroDbPlaybackEntryPoint : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;
        private readonly IntroDbApiClient _apiClient;

        // Caché de última posición reportada por sesión para detectar saltos (skips)
        private static readonly Dictionary<string, (long PositionTicks, string ItemId, string UserId, string UserName)> _sessionTracker = new();
        private static readonly object _lock = new();

        public IntroDbPlaybackEntryPoint(
            ISessionManager sessionManager,
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            ILogManager logManager)
        {
            _sessionManager = sessionManager;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logManager.GetLogger(GetType().Name);
            _apiClient = new IntroDbApiClient(_logger);
        }

        public void Run()
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _libraryManager.ItemAdded += OnItemAdded;
            _logger.Info("IntroDB.app v4.2: EntryPoint iniciado (Eventos Playback + Ingesta Biblioteca vinculados).");
        }

        public void Dispose()
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _libraryManager.ItemAdded -= OnItemAdded;
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled || !config.RealtimeScanOnLibraryChange)
            {
                return;
            }

            try
            {
                var item = e.Item;
                if (item == null) return;

                if (item is Episode ep)
                {
                    _ = Task.Run(async () =>
                    {
                        // Espera breve para dar tiempo a que los proveedores de metadatos completen el IMDb ID
                        await Task.Delay(4000).ConfigureAwait(false);
                        await ProcessEpisodeItem(ep, config, "Ingesta en Biblioteca (Auto-Scan)").ConfigureAwait(false);
                    });
                }
                else if (item is Movie movie && config.ScanMovies)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(4000).ConfigureAwait(false);
                        await ProcessMovieItem(movie, config, "Ingesta en Biblioteca (Auto-Scan)").ConfigureAwait(false);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"IntroDB.app: Error en detección ItemAdded: {ex.Message}");
            }
        }

        private void OnPlaybackStart(object? sender, EventArgs e)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled)
            {
                return;
            }

            try
            {
                var itemProp = e.GetType().GetProperty("Item");
                var item = itemProp?.GetValue(e) as BaseItem;
                if (item == null) return;

                var sessionProp = e.GetType().GetProperty("Session");
                var session = sessionProp?.GetValue(e);
                string userId = session?.GetType().GetProperty("UserId")?.GetValue(session)?.ToString() ?? "";
                string userName = session?.GetType().GetProperty("UserName")?.GetValue(session)?.ToString() ?? "";
                string sessionId = session?.GetType().GetProperty("Id")?.GetValue(session)?.ToString() ?? Guid.NewGuid().ToString();

                lock (_lock)
                {
                    _sessionTracker[sessionId] = (0, item.Id.ToString(), userId, userName);
                    RegisterOrUpdateUser(config, userId, userName);
                }

                if (config.EnableOnDemandFetch)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (item is Episode ep)
                            {
                                await ProcessEpisodeItem(ep, config, "Tiempo Real al Reproducir (On-Demand)").ConfigureAwait(false);
                            }
                            else if (item is Movie movie && config.ScanMovies)
                            {
                                await ProcessMovieItem(movie, config, "Tiempo Real al Reproducir (On-Demand)").ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"IntroDB.app: Error On-Demand en {item.Name}: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"IntroDB.app: Error en PlaybackStart: {ex.Message}");
            }
        }

        private void OnPlaybackProgress(object? sender, EventArgs e)
        {
            try
            {
                var sessionProp = e.GetType().GetProperty("Session");
                var session = sessionProp?.GetValue(e);
                if (session == null) return;

                string sessionId = session.GetType().GetProperty("Id")?.GetValue(session)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(sessionId)) return;

                var playStateProp = session.GetType().GetProperty("PlayState");
                var playState = playStateProp?.GetValue(session);
                long currentTicks = 0;
                if (playState != null)
                {
                    var posProp = playState.GetType().GetProperty("PositionTicks");
                    currentTicks = (long)(posProp?.GetValue(playState) ?? 0L);
                }

                var itemProp = session.GetType().GetProperty("NowPlayingItem");
                var item = itemProp?.GetValue(session) as BaseItem;
                if (item == null || currentTicks <= 0) return;

                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.Enabled) return;

                lock (_lock)
                {
                    if (_sessionTracker.TryGetValue(sessionId, out var previous))
                    {
                        if (previous.ItemId == item.Id.ToString())
                        {
                            long jumpTicks = currentTicks - previous.PositionTicks;
                            // Salto hacia adelante significativo (mínimo 15 segundos = 150_000_000 ticks)
                            if (jumpTicks > 150_000_000L && previous.PositionTicks > 0)
                            {
                                var chapters = _itemRepository.GetChapters(item)?.ToList();
                                
                                // 1. DETECCIÓN DE SALTO DE INTRO
                                var introStart = chapters?.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                                var introEnd = chapters?.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);
                                if (introStart != null && introEnd != null)
                                {
                                    if (previous.PositionTicks < introEnd.StartPositionTicks && currentTicks >= (introEnd.StartPositionTicks - 50_000_000L))
                                    {
                                        long savedSeconds = (introEnd.StartPositionTicks - Math.Max(previous.PositionTicks, introStart.StartPositionTicks)) / 10_000_000L;
                                        if (savedSeconds <= 0)
                                        {
                                            savedSeconds = (introEnd.StartPositionTicks - introStart.StartPositionTicks) / 10_000_000L;
                                        }

                                        if (savedSeconds > 0)
                                        {
                                            RecordUserSkip(config, previous.UserId, previous.UserName, true, savedSeconds);
                                            IntroDbActivityLogger.Log(item.Name, $"Salto de Intro detectado ({previous.UserName})", "success", $"Tiempo ahorrado: {savedSeconds}s");
                                        }
                                    }
                                }

                                // 2. DETECCIÓN DE SALTO DE CRÉDITOS / OUTRO
                                var creditsStart = chapters?.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                                if (creditsStart != null)
                                {
                                    long creditsEndTicks = chapters?.FirstOrDefault(c => c.Name == "Credits End")?.StartPositionTicks ?? (item.RunTimeTicks ?? 0L);
                                    if (creditsEndTicks <= creditsStart.StartPositionTicks && item.RunTimeTicks > creditsStart.StartPositionTicks)
                                    {
                                        creditsEndTicks = item.RunTimeTicks.Value;
                                    }

                                    if (previous.PositionTicks < creditsEndTicks && currentTicks >= (creditsStart.StartPositionTicks + 50_000_000L))
                                    {
                                        long savedCredits = (creditsEndTicks - Math.Max(previous.PositionTicks, creditsStart.StartPositionTicks)) / 10_000_000L;
                                        if (savedCredits <= 0)
                                        {
                                            savedCredits = 90;
                                        }

                                        if (savedCredits > 0)
                                        {
                                            RecordUserSkip(config, previous.UserId, previous.UserName, false, savedCredits);
                                            IntroDbActivityLogger.Log(item.Name, $"Salto de Créditos detectado ({previous.UserName})", "success", $"Tiempo ahorrado: {savedCredits}s");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    string userId = session.GetType().GetProperty("UserId")?.GetValue(session)?.ToString() ?? "";
                    string userName = session.GetType().GetProperty("UserName")?.GetValue(session)?.ToString() ?? "";
                    _sessionTracker[sessionId] = (currentTicks, item.Id.ToString(), userId, userName);
                }
            }
            catch
            {
                // Manejo silencioso en progreso frecuente de reproducción
            }
        }

        private void OnPlaybackStopped(object? sender, EventArgs e)
        {
            try
            {
                var sessionProp = e.GetType().GetProperty("Session");
                var session = sessionProp?.GetValue(e);
                string sessionId = session?.GetType().GetProperty("Id")?.GetValue(session)?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    lock (_lock)
                    {
                        if (_sessionTracker.TryGetValue(sessionId, out var previous) && previous.PositionTicks > 0)
                        {
                            var itemProp = e.GetType().GetProperty("Item") ?? session?.GetType().GetProperty("NowPlayingItem");
                            var item = itemProp?.GetValue(e ?? session) as BaseItem;
                            if (item != null)
                            {
                                var config = Plugin.Instance?.Configuration;
                                if (config != null && config.Enabled)
                                {
                                    var chapters = _itemRepository.GetChapters(item)?.ToList();
                                    var creditsStart = chapters?.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                                    if (creditsStart != null)
                                    {
                                        long creditsEndTicks = chapters?.FirstOrDefault(c => c.Name == "Credits End")?.StartPositionTicks ?? (item.RunTimeTicks ?? 0L);
                                        if (creditsEndTicks <= creditsStart.StartPositionTicks && item.RunTimeTicks > creditsStart.StartPositionTicks)
                                        {
                                            creditsEndTicks = item.RunTimeTicks.Value;
                                        }

                                        // Si la reproducción se detuvo dentro de los créditos (el usuario omitió ver el cierre)
                                        if (previous.PositionTicks >= creditsStart.StartPositionTicks && previous.PositionTicks < creditsEndTicks)
                                        {
                                            long savedCredits = (creditsEndTicks - previous.PositionTicks) / 10_000_000L;
                                            if (savedCredits >= 15)
                                            {
                                                RecordUserSkip(config, previous.UserId, previous.UserName, false, savedCredits);
                                                IntroDbActivityLogger.Log(item.Name, $"Fin de reproducción en créditos ({previous.UserName})", "info", $"Ahorro detectado: {savedCredits}s");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        _sessionTracker.Remove(sessionId);
                    }
                }
            }
            catch { }
        }

        private async Task ProcessEpisodeItem(Episode ep, PluginConfiguration config, string triggerSource)
        {
            var chapters = _itemRepository.GetChapters(ep)?.ToList() ?? new List<ChapterInfo>();
            bool hasIntro = chapters.Any(c => c.MarkerType == MarkerType.IntroStart);
            bool hasCredits = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);

            // Modo TheIntroDB Principal (Hybrid): Si ya tiene intro y outro, no hacemos nada
            if (config.OperationMode == "Hybrid" && hasIntro && hasCredits)
            {
                return;
            }

            var series = ep.Series;
            var imdbId = series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                return;
            }

            int season = ep.ParentIndexNumber ?? 1;
            int episodeNum = ep.IndexNumber ?? 1;

            var segments = await _apiClient.GetEpisodeSegmentsAsync(imdbId, season, episodeNum, CancellationToken.None).ConfigureAwait(false);
            if (segments == null)
            {
                return;
            }

            bool modified = false;

            // Lógica según modo de operación:
            // IntroDbPrimary: Si IntroDB tiene el segmento, lo inyecta. Si no, respeta lo existente (fallback a TheIntroDB).
            // Hybrid: TheIntroDB primero; solo llena vacíos de IntroDB.
            // Standalone: IntroDB gestiona todo.
            // Overwrite: IntroDB sobreescribe.

            bool shouldApplyIntro = false;
            if (config.FetchIntros && segments.Intro != null)
            {
                if (config.OperationMode == "IntroDbPrimary" || config.OperationMode == "Overwrite" || config.OperationMode == "Standalone")
                {
                    shouldApplyIntro = true;
                }
                else if (config.OperationMode == "Hybrid" && !hasIntro)
                {
                    shouldApplyIntro = true;
                }
            }

            if (shouldApplyIntro && segments.Intro != null)
            {
                chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd || (c.Name != null && c.Name.StartsWith("Intro", StringComparison.OrdinalIgnoreCase)));
                chapters.Add(new ChapterInfo
                {
                    Name = "Intro",
                    MarkerType = MarkerType.IntroStart,
                    StartPositionTicks = segments.Intro.StartMs * 10000
                });
                chapters.Add(new ChapterInfo
                {
                    Name = "Intro End",
                    MarkerType = MarkerType.IntroEnd,
                    StartPositionTicks = segments.Intro.EndMs * 10000
                });
                config.AddedIntrosCount++;
                modified = true;
            }

            bool shouldApplyCredits = false;
            if (config.FetchCredits && segments.Outro != null)
            {
                if (config.OperationMode == "IntroDbPrimary" || config.OperationMode == "Overwrite" || config.OperationMode == "Standalone")
                {
                    shouldApplyCredits = true;
                }
                else if (config.OperationMode == "Hybrid" && !hasCredits)
                {
                    shouldApplyCredits = true;
                }
            }

            if (shouldApplyCredits && segments.Outro != null)
            {
                chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                chapters.Add(new ChapterInfo
                {
                    Name = "Credits",
                    MarkerType = MarkerType.CreditsStart,
                    StartPositionTicks = segments.Outro.StartMs * 10000
                });
                chapters.Add(new ChapterInfo
                {
                    Name = "Credits End",
                    MarkerType = MarkerType.Chapter,
                    StartPositionTicks = segments.Outro.EndMs * 10000
                });
                config.AddedCreditsCount++;
                modified = true;
            }

            if (modified)
            {
                var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                for (int i = 0; i < ordered.Count; i++) ordered[i].ChapterIndex = i;
                _itemRepository.SaveChapters(ep.InternalId, ordered);

                IntroDbActivityLogger.Log(
                    $"{ep.SeriesName} S{season}E{episodeNum}",
                    triggerSource,
                    "success",
                    $"Modo: {config.OperationMode} | Intro: {segments.Intro != null}, Outro: {segments.Outro != null}"
                );
                Plugin.Instance?.SaveConfiguration();
            }
        }

        private async Task ProcessMovieItem(Movie movie, PluginConfiguration config, string triggerSource)
        {
            var chapters = _itemRepository.GetChapters(movie)?.ToList() ?? new List<ChapterInfo>();
            bool hasCredits = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);

            if (config.OperationMode == "Hybrid" && hasCredits)
            {
                return;
            }

            var imdbId = movie.GetProviderId(MetadataProviders.Imdb);
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                return;
            }

            var segments = await _apiClient.GetMovieSegmentsAsync(imdbId, CancellationToken.None).ConfigureAwait(false);
            if (segments == null)
            {
                return;
            }

            bool modified = false;

            if (config.FetchCredits && segments.Outro != null)
            {
                bool applyCredits = (config.OperationMode != "Hybrid" || !hasCredits);
                if (applyCredits)
                {
                    chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart || (c.Name != null && c.Name.StartsWith("Credits", StringComparison.OrdinalIgnoreCase)));
                    chapters.Add(new ChapterInfo
                    {
                        Name = "Credits",
                        MarkerType = MarkerType.CreditsStart,
                        StartPositionTicks = segments.Outro.StartMs * 10000
                    });
                    chapters.Add(new ChapterInfo
                    {
                        Name = "Credits End",
                        MarkerType = MarkerType.Chapter,
                        StartPositionTicks = segments.Outro.EndMs * 10000
                    });
                    config.AddedCreditsCount++;
                    modified = true;
                }
            }

            if (config.FetchPostCredits && segments.PostCredits != null)
            {
                chapters.RemoveAll(c => c.Name != null && c.Name.Contains("Post-Créditos"));
                chapters.Add(new ChapterInfo
                {
                    Name = "🎬 Escena Post-Créditos",
                    MarkerType = MarkerType.Chapter,
                    StartPositionTicks = segments.PostCredits.StartMs * 10000
                });
                config.AddedPostCreditsCount++;
                modified = true;
            }

            if (modified)
            {
                var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                for (int i = 0; i < ordered.Count; i++) ordered[i].ChapterIndex = i;
                _itemRepository.SaveChapters(movie.InternalId, ordered);

                IntroDbActivityLogger.Log(
                    movie.Name,
                    triggerSource,
                    "success",
                    $"Créditos: {segments.Outro != null}, Post-Créditos: {segments.PostCredits != null}"
                );
                Plugin.Instance?.SaveConfiguration();
            }
        }

        private static void RegisterOrUpdateUser(PluginConfiguration config, string userId, string userName)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            if (config.UserMetrics == null) config.UserMetrics = new List<UserMetric>();

            var user = config.UserMetrics.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
            {
                config.UserMetrics.Add(new UserMetric
                {
                    UserId = userId,
                    UserName = string.IsNullOrWhiteSpace(userName) ? "Usuario" : userName,
                    LastActivity = DateTime.UtcNow
                });
                Plugin.Instance?.SaveConfiguration();
            }
            else if (!string.IsNullOrWhiteSpace(userName) && user.UserName != userName)
            {
                user.UserName = userName;
                user.LastActivity = DateTime.UtcNow;
                Plugin.Instance?.SaveConfiguration();
            }
        }

        public static void RecordUserSkip(PluginConfiguration config, string userId, string userName, bool isIntro, long secondsSaved)
        {
            if (string.IsNullOrWhiteSpace(userId) || config == null) return;
            if (config.UserMetrics == null) config.UserMetrics = new List<UserMetric>();

            var user = config.UserMetrics.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
            {
                user = new UserMetric
                {
                    UserId = userId,
                    UserName = string.IsNullOrWhiteSpace(userName) ? "Usuario" : userName
                };
                config.UserMetrics.Add(user);
            }

            if (!string.IsNullOrWhiteSpace(userName)) user.UserName = userName;
            if (isIntro) user.IntrosSkipped++;
            else user.OutrosSkipped++;

            user.SecondsSaved += secondsSaved;
            user.LastActivity = DateTime.UtcNow;
            config.EstimatedSecondsSaved += secondsSaved;

            Plugin.Instance?.SaveConfiguration();
        }
    }
}