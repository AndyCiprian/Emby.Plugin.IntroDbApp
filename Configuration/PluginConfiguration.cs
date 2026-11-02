using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace IntroDbApp.Configuration
{
    public class UserMetric
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public int IntrosSkipped { get; set; } = 0;
        public int OutrosSkipped { get; set; } = 0;
        public long SecondsSaved { get; set; } = 0;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        // Interruptor maestro del complemento
        public bool Enabled { get; set; } = true;

        // Modos de sincronización:
        // "IntroDbPrimary" (IntroDB principal, TheIntroDB fallback)
        // "Hybrid" (TheIntroDB principal, IntroDB fallback)
        // "Standalone" (IntroDB 100% independiente)
        // "Overwrite" (Sobrescribir todo con IntroDB)
        public string OperationMode { get; set; } = "IntroDbPrimary";

        public bool FetchIntros { get; set; } = true;
        public bool FetchCredits { get; set; } = true;
        public bool FetchPostCredits { get; set; } = true;
        public bool ScanMovies { get; set; } = true;
        public bool EnableOnDemandFetch { get; set; } = true;
        public bool RealtimeScanOnLibraryChange { get; set; } = true;

        // Credenciales de IntroDB.app
        public string ApiKey { get; set; } = "";
        public bool IsApiKeyVerified { get; set; } = false;
        public string ApiKeyStatusMessage { get; set; } = "No verificada";
        public int ApiSubmissionsTotal { get; set; } = 0;
        public int ApiSubmissionsLive { get; set; } = 0;
        public int ApiSubmissionsPending { get; set; } = 0;
        public int ApiSubmissionsRejected { get; set; } = 0;
        public int ApiRateLimitRemaining { get; set; } = 100;
        public int ApiRateLimitLimit { get; set; } = 100;
        public DateTime ApiKeyLastChecked { get; set; } = DateTime.MinValue;

        // Estadísticas y Métricas Globales de la Biblioteca
        public string LastScanStatus { get; set; } = "No ejecutado";
        public int ScannedItemsCount { get; set; } = 0;
        public int AddedIntrosCount { get; set; } = 0;
        public int AddedCreditsCount { get; set; } = 0;
        public int AddedPostCreditsCount { get; set; } = 0;
        public int TotalLibraryEpisodes { get; set; } = 0;
        public int TotalEpisodesWithIntro { get; set; } = 0;
        public int TotalEpisodesWithCredits { get; set; } = 0;
        public long EstimatedSecondsSaved { get; set; } = 0;

        // Caché de elementos sin registros en la API con auto-expiración progresiva (TTL)
        public int CacheTtlDays { get; set; } = 14;
        public int MaxDailyRechecks { get; set; } = 30;
        public List<string> NotFoundIds { get; set; } = new List<string>();

        public void NormalizeNotFoundCache()
        {
            if (NotFoundIds == null || NotFoundIds.Count == 0) return;

            var updated = new List<string>(NotFoundIds.Count);
            var random = new Random(42);
            int ttl = Math.Max(1, CacheTtlDays);

            for (int i = 0; i < NotFoundIds.Count; i++)
            {
                string raw = NotFoundIds[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;

                int pipeIdx = raw.IndexOf('|');
                if (pipeIdx > 0)
                {
                    updated.Add(raw);
                }
                else
                {
                    // Entrada previa sin fecha: distribuimos escalonadamente en los últimos 'ttl' días
                    int daysAgo = random.Next(1, ttl + 1);
                    DateTime staggeredDate = DateTime.UtcNow.AddDays(-daysAgo);
                    updated.Add($"{raw}|{staggeredDate:o}");
                }
            }

            NotFoundIds = updated;
        }

        public bool TryGetNotFoundEntry(string key, out DateTime cachedDate, out int entryIndex)
        {
            cachedDate = DateTime.MinValue;
            entryIndex = -1;

            if (NotFoundIds == null || NotFoundIds.Count == 0) return false;

            for (int i = 0; i < NotFoundIds.Count; i++)
            {
                string raw = NotFoundIds[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;

                int pipeIdx = raw.IndexOf('|');
                string entryKey = pipeIdx > 0 ? raw.Substring(0, pipeIdx) : raw;

                if (string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    entryIndex = i;
                    if (pipeIdx > 0 && DateTime.TryParse(raw.Substring(pipeIdx + 1), out var dt))
                    {
                        cachedDate = dt;
                    }
                    else
                    {
                        cachedDate = DateTime.UtcNow;
                    }
                    return true;
                }
            }

            return false;
        }

        public void AddOrUpdateNotFound(string key)
        {
            if (NotFoundIds == null) NotFoundIds = new List<string>();

            string newEntry = $"{key}|{DateTime.UtcNow:o}";

            for (int i = 0; i < NotFoundIds.Count; i++)
            {
                string raw = NotFoundIds[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;

                int pipeIdx = raw.IndexOf('|');
                string entryKey = pipeIdx > 0 ? raw.Substring(0, pipeIdx) : raw;

                if (string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    NotFoundIds[i] = newEntry;
                    return;
                }
            }

            NotFoundIds.Add(newEntry);
        }

        public bool RemoveNotFound(string key)
        {
            if (NotFoundIds == null || NotFoundIds.Count == 0) return false;

            for (int i = 0; i < NotFoundIds.Count; i++)
            {
                string raw = NotFoundIds[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;

                int pipeIdx = raw.IndexOf('|');
                string entryKey = pipeIdx > 0 ? raw.Substring(0, pipeIdx) : raw;

                if (string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    NotFoundIds.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public int GetNotFoundCount()
        {
            return NotFoundIds?.Count ?? 0;
        }

        // Métricas por usuario / perfiles
        public List<UserMetric> UserMetrics { get; set; } = new List<UserMetric>();

        // Lista de IDs de series excluidas del salto automático y telemetría
        public List<string> AutoSkipExcludedSeriesIds { get; set; } = new List<string>();

        // Ruta configurada para exportación de respaldos JSON (vacía por defecto)
        public string BackupPath { get; set; } = string.Empty;
        public DateTime LastBackupDate { get; set; } = DateTime.MinValue;
        public int LastBackupCount { get; set; } = 0;
    }
}
