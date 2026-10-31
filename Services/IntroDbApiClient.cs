using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace IntroDbApp.Services
{
    public class IntroDbApiClient
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.introdb.app/"),
            Timeout = TimeSpan.FromSeconds(12)
        };

        private readonly ILogger _logger;

        public IntroDbApiClient(ILogger logger)
        {
            _logger = logger;
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Emby-IntroDbApp-Plugin/4.0");
            }
        }

        public async Task<IntroDbSegmentsResponse?> GetEpisodeSegmentsAsync(string imdbId, int season, int episode, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"segments?imdb_id={imdbId}&season={season}&episode={episode}";
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<IntroDbSegmentsResponse>(json, options);
            }
            catch (Exception ex)
            {
                _logger.Warn($"IntroDB.app: Error en consulta API episodio ({imdbId} S{season}E{episode}): {ex.Message}");
                return null;
            }
        }

        public async Task<IntroDbSegmentsResponse?> GetMovieSegmentsAsync(string imdbId, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"segments?imdb_id={imdbId}&is_movie=true";
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<IntroDbSegmentsResponse>(json, options);
            }
            catch (Exception ex)
            {
                _logger.Warn($"IntroDB.app: Error en consulta API película ({imdbId}): {ex.Message}");
                return null;
            }
        }

        public async Task<(bool IsValid, string Message, int RateLimitRemaining, int RateLimitLimit)> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "No se ha ingresado una clave de API.", 0, 0);
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "submit");
                request.Headers.Add("X-API-Key", apiKey.Trim());
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                int limit = 100;
                int remaining = 100;
                if (response.Headers.TryGetValues("x-ratelimit-limit", out var limits) && int.TryParse(limits.FirstOrDefault(), out int lVal))
                {
                    limit = lVal;
                }
                if (response.Headers.TryGetValues("x-ratelimit-remaining", out var rems) && int.TryParse(rems.FirstOrDefault(), out int rVal))
                {
                    remaining = rVal;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return (false, "Clave de API no válida o revocada por IntroDB.", remaining, limit);
                }

                // Si responde 400 (Invalid payload) o 200, la autenticación fue aceptada
                return (true, "Clave de API verificada y activa en IntroDB.app.", remaining, limit);
            }
            catch (Exception ex)
            {
                _logger.Error($"IntroDB.app: Error validando clave de API: {ex.Message}");
                return (false, $"Error de conexión con IntroDB: {ex.Message}", 0, 0);
            }
        }

        public async Task<(bool Success, string Message)> SubmitSegmentAsync(string apiKey, SubmitSegmentPayload payload, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "No se ha configurado una clave de API de IntroDB.app.");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "submit");
                request.Headers.Add("X-API-Key", apiKey.Trim());

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Segmento remitido exitosamente a la base comunitaria de IntroDB.");
                }
                else
                {
                    return (false, $"Respuesta de IntroDB ({response.StatusCode}): {responseBody}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"IntroDB.app: Error enviando segmento a la API: {ex.Message}");
                return (false, $"Error de red: {ex.Message}");
            }
        }
    }

    public class SubmitSegmentPayload
    {
        [JsonPropertyName("imdb_id")]
        public string ImdbId { get; set; } = "";

        [JsonPropertyName("is_movie")]
        public bool? IsMovie { get; set; }

        [JsonPropertyName("segment_type")]
        public string SegmentType { get; set; } = "intro";

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("start_sec")]
        public double StartSec { get; set; }

        [JsonPropertyName("end_sec")]
        public double EndSec { get; set; }
    }

    public class IntroDbSegmentsResponse
    {
        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("is_movie")]
        public bool IsMovie { get; set; }

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("intro")]
        public SegmentInfo? Intro { get; set; }

        [JsonPropertyName("recap")]
        public SegmentInfo? Recap { get; set; }

        [JsonPropertyName("outro")]
        public SegmentInfo? Outro { get; set; }

        [JsonPropertyName("post_credits")]
        public SegmentInfo? PostCredits { get; set; }
    }

    public class SegmentInfo
    {
        [JsonPropertyName("start_sec")]
        public double StartSec { get; set; }

        [JsonPropertyName("end_sec")]
        public double EndSec { get; set; }

        [JsonPropertyName("start_ms")]
        public long StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public long EndMs { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("submission_count")]
        public int SubmissionCount { get; set; }
    }
}