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

        // Caché de elementos sin registros en la API
        public List<string> NotFoundIds { get; set; } = new List<string>();

        // Métricas por usuario / perfiles
        public List<UserMetric> UserMetrics { get; set; } = new List<UserMetric>();
    }
}