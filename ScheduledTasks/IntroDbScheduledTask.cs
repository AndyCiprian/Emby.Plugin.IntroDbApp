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
using MediaBrowser.Model.Tasks;
using IntroDbApp.Configuration;
using IntroDbApp.Services;

namespace IntroDbApp.ScheduledTasks
{
    public class IntroDbScheduledTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;
        private readonly IntroDbApiClient _apiClient;

        public string Name => "IntroDB.app Media Segment Scan";
        public string Key => "IntroDbAppScan";
        public string Description => "Escanea episodios y películas, detectando timestamps universales para Intros, Créditos y Post-Créditos mediante IntroDB.app.";
        public string Category => "Library";

        public IntroDbScheduledTask(ILibraryManager libraryManager, IItemRepository itemRepository, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logManager.GetLogger(GetType().Name);
            _apiClient = new IntroDbApiClient(_logger);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(2.5).Ticks
                }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = Plugin.Instance?.Configuration;
            if (config != null && !config.Enabled)
            {
                _logger.Info("IntroDB.app: Tarea omitida porque el plugin está deshabilitado.");
                return;
            }

            var startTime = DateTime.Now;
            string mode = config?.OperationMode ?? "IntroDbPrimary";
            _logger.Info($"IntroDB.app: Iniciando escaneo en Modo '{mode}'...");
            IntroDbActivityLogger.Log("Escaneo Iniciado", $"Modo: {mode}", "info");

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Episode>().ToList();

            int totalEpisodes = episodes.Count;
            int scannedEpisodes = 0;
            int newIntros = 0;
            int newCredits = 0;
            int newPostCredits = 0;

            int episodesWithIntro = 0;
            int episodesWithCredits = 0;

            if (config != null && config.NotFoundIds == null)
            {
                config.NotFoundIds = new List<string>();
            }

            for (int i = 0; i < totalEpisodes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ep = episodes[i];

                try
                {
                    var chapters = _itemRepository.GetChapters(ep)?.ToList() ?? new List<ChapterInfo>();
                    bool hasIntro = chapters.Any(c => c.MarkerType == MarkerType.IntroStart);
                    bool hasCredits = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);

                    if (hasIntro) episodesWithIntro++;
                    if (hasCredits) episodesWithCredits++;

                    // En modo TheIntroDB Primero (Hybrid), si ya tiene ambos, no se consulta
                    if (mode == "Hybrid" && hasIntro && hasCredits)
                    {
                        continue;
                    }

                    var series = ep.Series;
                    var imdbId = series?.GetProviderId(MetadataProviders.Imdb) ?? ep.GetProviderId(MetadataProviders.Imdb);

                    if (string.IsNullOrWhiteSpace(imdbId))
                    {
                        continue;
                    }

                    int season = ep.ParentIndexNumber ?? 1;
                    int episodeNum = ep.IndexNumber ?? 1;
                    string cacheKey = $"{imdbId}_S{season}E{episodeNum}";

                    if (config?.NotFoundIds.Contains(cacheKey) == true && mode != "Overwrite")
                    {
                        continue;
                    }

                    var segments = await _apiClient.GetEpisodeSegmentsAsync(imdbId, season, episodeNum, cancellationToken).ConfigureAwait(false);
                    if (segments == null || (segments.Intro == null && segments.Outro == null))
                    {
                        if (config != null && !config.NotFoundIds.Contains(cacheKey))
                        {
                            config.NotFoundIds.Add(cacheKey);
                        }
                        continue;
                    }

                    bool modified = false;

                    // Decidir si aplicar Intro
                    bool applyIntro = false;
                    if (config?.FetchIntros != false && segments.Intro != null)
                    {
                        if (mode == "IntroDbPrimary" || mode == "Overwrite" || mode == "Standalone")
                        {
                            applyIntro = true;
                        }
                        else if (mode == "Hybrid" && !hasIntro)
                        {
                            applyIntro = true;
                        }
                    }

                    if (applyIntro && segments.Intro != null)
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

                        newIntros++;
                        if (!hasIntro) episodesWithIntro++;
                        modified = true;
                    }

                    // Decidir si aplicar Credits/Outro
                    bool applyCredits = false;
                    if (config?.FetchCredits != false && segments.Outro != null)
                    {
                        if (mode == "IntroDbPrimary" || mode == "Overwrite" || mode == "Standalone")
                        {
                            applyCredits = true;
                        }
                        else if (mode == "Hybrid" && !hasCredits)
                        {
                            applyCredits = true;
                        }
                    }

                    if (applyCredits && segments.Outro != null)
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

                        newCredits++;
                        if (!hasCredits) episodesWithCredits++;
                        modified = true;
                    }

                    if (modified)
                    {
                        var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                        for (int cIdx = 0; cIdx < ordered.Count; cIdx++) ordered[cIdx].ChapterIndex = cIdx;

                        _itemRepository.SaveChapters(ep.InternalId, ordered);
                        IntroDbActivityLogger.Log($"{ep.SeriesName} S{season}E{episodeNum}", "Marcas actualizadas", "success");
                    }

                    scannedEpisodes++;
                }
                catch (Exception ex)
                {
                    _logger.Error($"IntroDB.app: Error en episodio {ep.Name}: {ex.Message}");
                }

                progress?.Report((double)(i + 1) / totalEpisodes * (config?.ScanMovies == true ? 80.0 : 100.0));
            }

            // Escaneo de Películas
            if (config?.ScanMovies == true)
            {
                var movies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Movie) },
                    IsVirtualItem = false,
                    Recursive = true
                }).OfType<Movie>().ToList();

                int totalMovies = movies.Count;

                for (int m = 0; m < totalMovies; m++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var movie = movies[m];

                    try
                    {
                        var chapters = _itemRepository.GetChapters(movie)?.ToList() ?? new List<ChapterInfo>();
                        bool hasCredits = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);

                        if (mode == "Hybrid" && hasCredits)
                        {
                            continue;
                        }

                        var imdbId = movie.GetProviderId(MetadataProviders.Imdb);
                        if (string.IsNullOrWhiteSpace(imdbId))
                        {
                            continue;
                        }

                        string cacheKey = $"movie_{imdbId}";
                        if (config?.NotFoundIds.Contains(cacheKey) == true && mode != "Overwrite")
                        {
                            continue;
                        }

                        var segments = await _apiClient.GetMovieSegmentsAsync(imdbId, cancellationToken).ConfigureAwait(false);
                        if (segments == null || (segments.Outro == null && segments.PostCredits == null))
                        {
                            if (config != null && !config.NotFoundIds.Contains(cacheKey))
                            {
                                config.NotFoundIds.Add(cacheKey);
                            }
                            continue;
                        }

                        bool modified = false;

                        if (config?.FetchCredits != false && segments.Outro != null)
                        {
                            bool applyCredits = (mode != "Hybrid" || !hasCredits);
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

                                newCredits++;
                                modified = true;
                            }
                        }

                        if (config?.FetchPostCredits != false && segments.PostCredits != null)
                        {
                            chapters.RemoveAll(c => c.Name != null && c.Name.Contains("Post-Créditos"));

                            chapters.Add(new ChapterInfo
                            {
                                Name = "🎬 Escena Post-Créditos",
                                MarkerType = MarkerType.Chapter,
                                StartPositionTicks = segments.PostCredits.StartMs * 10000
                            });

                            newPostCredits++;
                            modified = true;
                        }

                        if (modified)
                        {
                            var ordered = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                            for (int cIdx = 0; cIdx < ordered.Count; cIdx++) ordered[cIdx].ChapterIndex = cIdx;

                            _itemRepository.SaveChapters(movie.InternalId, ordered);
                            IntroDbActivityLogger.Log(movie.Name, "Película actualizada con créditos/post-créditos", "success");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"IntroDB.app: Error en película {movie.Name}: {ex.Message}");
                    }

                    progress?.Report(80.0 + ((double)(m + 1) / totalMovies * 20.0));
                }
            }

            var elapsed = DateTime.Now - startTime;

            if (config != null)
            {
                config.LastScanStatus = $"Completado el {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Duración: {elapsed.TotalSeconds:F0}s)";
                config.ScannedItemsCount = scannedEpisodes;
                config.AddedIntrosCount += newIntros;
                config.AddedCreditsCount += newCredits;
                config.AddedPostCreditsCount += newPostCredits;

                config.TotalLibraryEpisodes = totalEpisodes;
                config.TotalEpisodesWithIntro = episodesWithIntro;
                config.TotalEpisodesWithCredits = episodesWithCredits;

                config.EstimatedSecondsSaved = (long)(episodesWithIntro * 85 + episodesWithCredits * 90);

                Plugin.Instance?.SaveConfiguration();
            }

            IntroDbActivityLogger.Log(
                "Escaneo Finalizado",
                $"Nuevos intros: {newIntros}, Nuevos créditos: {newCredits}, Post-créditos: {newPostCredits} ({elapsed.TotalSeconds:F0}s)",
                "success"
            );

            _logger.Info($"IntroDB.app: Escaneo finalizado en {elapsed.TotalSeconds:F0}s. Intros: +{newIntros}, Créditos: +{newCredits}, Post-Créditos: +{newPostCredits}");
            progress?.Report(100.0);
        }
    }
}