// <copyright file="TvdbApi.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Api
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Plugin.MetaShark.Configuration;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Logging;

    public sealed class TvdbApi : IDisposable
    {
        private const string DefaultApiHost = "https://api4.thetvdb.com/v4";
        private const string TokenCacheKey = "tvdb_token";
        private const int MaxPageCount = 20;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ILogger<TvdbApi> logger;
        private readonly MemoryCache memoryCache;
        private readonly HttpClient httpClient;
        private readonly Action<ILogger, string, Exception?> logTvdbError;
        private readonly string apiKey;
        private readonly string pin;
        private readonly string apiHost;

        public TvdbApi(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<TvdbApi>();
            this.memoryCache = new MemoryCache(new MemoryCacheOptions());
            this.logTvdbError = LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(TvdbApi)), "TVDB request failed in {Operation}");

            var config = MetaSharkPlugin.Instance?.Configuration;
            this.apiKey = config?.TvdbApiKey ?? string.Empty;
            this.pin = config?.TvdbPin ?? string.Empty;
            this.apiHost = string.IsNullOrWhiteSpace(config?.TvdbHost) ? DefaultApiHost : config.TvdbHost;

            this.httpClient = new HttpClient { BaseAddress = new Uri(this.apiHost), Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<IReadOnlyList<TvdbEpisode>> GetSeriesEpisodesAsync(
            int seriesId,
            string seasonType,
            int seasonNumber,
            string? language,
            CancellationToken cancellationToken)
        {
            if (!this.IsEnabled())
            {
                return Array.Empty<TvdbEpisode>();
            }

            var episodes = new List<TvdbEpisode>();
            var lang = NormalizeLanguage(language);
            var basePath = string.IsNullOrWhiteSpace(lang)
                ? $"/series/{seriesId}/episodes/{seasonType}"
                : $"/series/{seriesId}/episodes/{seasonType}/{lang}";

            for (var page = 0; page < MaxPageCount; page++)
            {
                var url = $"{basePath}?page={page.ToString(CultureInfo.InvariantCulture)}&season={seasonNumber.ToString(CultureInfo.InvariantCulture)}";
                TvdbEpisodesResponse? responsePayload;
                try
                {
                    using var response = await this.SendWithTokenAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken)
                        .ConfigureAwait(false);
                    if (response == null)
                    {
                        return episodes;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        this.logTvdbError(this.logger, nameof(this.GetSeriesEpisodesAsync), new HttpRequestException(response.StatusCode.ToString()));
                        return episodes;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    responsePayload = JsonSerializer.Deserialize<TvdbEpisodesResponse>(json, JsonOptions);
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    this.logTvdbError(this.logger, nameof(this.GetSeriesEpisodesAsync), ex);
                    return episodes;
                }
                catch (HttpRequestException ex)
                {
                    this.logTvdbError(this.logger, nameof(this.GetSeriesEpisodesAsync), ex);
                    return episodes;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                if (responsePayload?.Data?.Episodes == null)
                {
                    return episodes;
                }

                foreach (var episode in responsePayload.Data.Episodes)
                {
                    episodes.Add(new TvdbEpisode
                    {
                        SeasonNumber = episode.SeasonNumber,
                        Number = episode.Number,
                        AirsBeforeSeason = episode.AirsBeforeSeason,
                        AirsBeforeEpisode = episode.AirsBeforeEpisode,
                        AirsAfterSeason = episode.AirsAfterSeason,
                        Aired = ParseAiredDate(episode.Aired),
                    });
                }

                if (string.IsNullOrWhiteSpace(responsePayload.Links?.Next))
                {
                    break;
                }
            }

            return episodes;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool IsEnabled()
        {
            return (MetaSharkPlugin.Instance?.Configuration?.EnableTvdbSpecialsWithinSeasons ?? false)
                && !string.IsNullOrWhiteSpace(this.apiKey);
        }

        private static DateTime? ParseAiredDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                return parsed.Date;
            }

            return null;
        }

        private static string? NormalizeLanguage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return "zho";
            }

            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "eng";
            }

            if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return "jpn";
            }

            if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            {
                return "kor";
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(value);
                if (!string.IsNullOrWhiteSpace(culture.ThreeLetterISOLanguageName))
                {
                    return culture.ThreeLetterISOLanguageName;
                }
            }
            catch (CultureNotFoundException)
            {
                return null;
            }

            return null;
        }

        private async Task<string?> EnsureTokenAsync(CancellationToken cancellationToken)
        {
            if (this.memoryCache.TryGetValue<string>(TokenCacheKey, out var token) && !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            if (string.IsNullOrWhiteSpace(this.apiKey))
            {
                return null;
            }

            try
            {
                var payload = new Dictionary<string, string>
                {
                    ["apikey"] = this.apiKey,
                };
                if (!string.IsNullOrWhiteSpace(this.pin))
                {
                    payload["pin"] = this.pin;
                }

                var request = new HttpRequestMessage(HttpMethod.Post, "/login")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
                };

                using var response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    this.logTvdbError(this.logger, nameof(this.EnsureTokenAsync), new HttpRequestException(response.StatusCode.ToString()));
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var login = JsonSerializer.Deserialize<TvdbLoginResponse>(json, JsonOptions);
                token = login?.Data?.Token;
                if (string.IsNullOrWhiteSpace(token))
                {
                    return null;
                }

                var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(20) };
                this.memoryCache.Set(TokenCacheKey, token, options);
                return token;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                this.logTvdbError(this.logger, nameof(this.EnsureTokenAsync), ex);
                return null;
            }
            catch (HttpRequestException ex)
            {
                this.logTvdbError(this.logger, nameof(this.EnsureTokenAsync), ex);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        private async Task<HttpResponseMessage?> SendWithTokenAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
        {
            var token = await this.EnsureTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            using var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.Dispose();
            this.memoryCache.Remove(TokenCacheKey);
            token = await this.EnsureTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            using var retryRequest = requestFactory();
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await this.httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.memoryCache.Dispose();
                this.httpClient.Dispose();
            }
        }

        private sealed class TvdbLoginResponse
        {
            [JsonPropertyName("data")]
            public TvdbLoginData? Data { get; set; }
        }

        private sealed class TvdbLoginData
        {
            [JsonPropertyName("token")]
            public string? Token { get; set; }
        }

        private sealed class TvdbEpisodesResponse
        {
            [JsonPropertyName("data")]
            public TvdbEpisodesData? Data { get; set; }

            [JsonPropertyName("links")]
            public TvdbLinks? Links { get; set; }
        }

        private sealed class TvdbEpisodesData
        {
            [JsonPropertyName("episodes")]
            public List<TvdbEpisodeBaseRecord>? Episodes { get; set; }
        }

        private sealed class TvdbLinks
        {
            [JsonPropertyName("next")]
            public string? Next { get; set; }
        }

        private sealed class TvdbEpisodeBaseRecord
        {
            [JsonPropertyName("seasonNumber")]
            public int? SeasonNumber { get; set; }

            [JsonPropertyName("number")]
            public int? Number { get; set; }

            [JsonPropertyName("airsBeforeSeason")]
            public int? AirsBeforeSeason { get; set; }

            [JsonPropertyName("airsBeforeEpisode")]
            public int? AirsBeforeEpisode { get; set; }

            [JsonPropertyName("airsAfterSeason")]
            public int? AirsAfterSeason { get; set; }

            [JsonPropertyName("aired")]
            public string? Aired { get; set; }
        }
    }

    public sealed class TvdbEpisode
    {
        public int? SeasonNumber { get; set; }

        public int? Number { get; set; }

        public int? AirsBeforeSeason { get; set; }

        public int? AirsBeforeEpisode { get; set; }

        public int? AirsAfterSeason { get; set; }

        public DateTime? Aired { get; set; }
    }
}
