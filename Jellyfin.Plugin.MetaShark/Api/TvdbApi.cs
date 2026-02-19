// <copyright file="TvdbApi.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Api
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
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
    using Jellyfin.Plugin.MetaShark.Model;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// TheTVDB API client.
    /// </summary>
    public sealed class TvdbApi : IDisposable
    {
        private const string DefaultApiHost = "https://api4.thetvdb.com/v4/";
        private const string TokenCacheKey = "tvdb_token";
        private const int MaxPageCount = 20;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ILogger<TvdbApi> logger;
        private readonly MemoryCache memoryCache;
        private readonly HttpClient httpClient;
        private readonly string apiKey;
        private readonly string pin;
        private readonly string apiHost;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvdbApi"/> class.
        /// </summary>
        public TvdbApi(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<TvdbApi>();
            this.memoryCache = new MemoryCache(new MemoryCacheOptions());

            var config = MetaSharkPlugin.Instance?.Configuration;
            this.apiKey = config?.TvdbApiKey ?? string.Empty;
            this.pin = config?.TvdbPin ?? string.Empty;
            this.apiHost = NormalizeApiHost(config?.TvdbHost);

            this.httpClient = new HttpClient { BaseAddress = new Uri(this.apiHost), Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>
        /// Searches for series.
        /// </summary>
        public async Task<IReadOnlyList<TvdbSearchResult>> SearchSeriesAsync(string name, string? language, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(this.apiKey))
            {
                return Array.Empty<TvdbSearchResult>();
            }

            var lang = NormalizeLanguage(language);
            var url = $"search?query={WebUtility.UrlEncode(name)}&type=series";
            if (!string.IsNullOrEmpty(lang))
            {
                url += $"&language={lang}";
            }

            try
            {
                using var response = await this.SendWithTokenAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
                if (response == null || !response.IsSuccessStatusCode)
                {
                    return Array.Empty<TvdbSearchResult>();
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TvdbSearchResponse>(json, JsonOptions);
                var list = result?.Data ?? new List<TvdbSearchResult>();
                foreach (var item in list)
                {
                    item.ImageUrl = FixImageUrl(item.ImageUrl?.ToString());
                }

                return list;
            }
            catch (JsonException) { return Array.Empty<TvdbSearchResult>(); }
            catch (HttpRequestException) { return Array.Empty<TvdbSearchResult>(); }
            catch (TaskCanceledException) { return Array.Empty<TvdbSearchResult>(); }
        }

        /// <summary>
        /// Gets series metadata.
        /// </summary>
        public async Task<TvdbSeries?> GetSeriesAsync(int id, string? language, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(this.apiKey))
            {
                return null;
            }

            var url = $"series/{id}/extended?short=false&meta=translations";
            try
            {
                using var response = await this.SendWithTokenAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
                if (response == null || !response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TvdbSeriesResponse>(json, JsonOptions);
                var series = result?.Data;
                if (series != null)
                {
                    series.Image = FixImageUrl(series.Image)?.ToString();
                    if (series.Artworks != null)
                    {
                        foreach (var art in series.Artworks)
                        {
                            art.Image = FixImageUrl(art.Image)?.ToString();
                        }
                    }

                    if (series.Seasons != null)
                    {
                        foreach (var season in series.Seasons)
                        {
                            season.Image = FixImageUrl(season.Image)?.ToString();
                        }
                    }
                }

                return series;
            }
            catch (JsonException) { return null; }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
        }

        /// <summary>
        /// Gets series episodes.
        /// </summary>
        public async Task<IReadOnlyList<TvdbEpisode>> GetSeriesEpisodesAsync(int seriesId, string seasonType, int seasonNumber, string? language, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(this.apiKey))
            {
                return Array.Empty<TvdbEpisode>();
            }

            var episodes = new List<TvdbEpisode>();
            var lang = NormalizeLanguage(language);
            var basePath = string.IsNullOrWhiteSpace(lang) ? $"series/{seriesId}/episodes/{seasonType}" : $"series/{seriesId}/episodes/{seasonType}/{lang}";
            for (var page = 0; page < MaxPageCount; page++)
            {
                var url = $"{basePath}?page={page.ToString(CultureInfo.InvariantCulture)}&season={seasonNumber.ToString(CultureInfo.InvariantCulture)}";
                try
                {
                    using var response = await this.SendWithTokenAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
                    if (response == null || !response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var responsePayload = JsonSerializer.Deserialize<TvdbEpisodesResponse>(json, JsonOptions);
                    if (responsePayload?.Data?.Episodes == null)
                    {
                        break;
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
                            Name = episode.Name,
                            Overview = episode.Overview,
                            Image = FixImageUrl(episode.Image)?.ToString(),
                        });
                    }

                    if (string.IsNullOrWhiteSpace(responsePayload.Links?.Next))
                    {
                        break;
                    }
                }
                catch (JsonException) { break; }
                catch (HttpRequestException) { break; }
                catch (TaskCanceledException) { break; }
            }

            return episodes;
        }

        /// <summary>
        /// Gets episode group data.
        /// </summary>
        public async Task<TvdbEpisodeGroup?> GetEpisodeGroupAsync(int groupId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(this.apiKey))
            {
                return null;
            }

            var url = $"episode-groups/{groupId}";
            try
            {
                using var response = await this.SendWithTokenAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
                if (response == null || !response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TvdbEpisodeGroupResponse>(json, JsonOptions);
                return result?.Data;
            }
            catch (JsonException) { return null; }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.memoryCache.Dispose();
            this.httpClient.Dispose();
        }

        private static DateTime? ParseAiredDate(string? v) => DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var p) ? p.Date : null;

        private static string NormalizeApiHost(string? v)
        {
            if (string.IsNullOrWhiteSpace(v))
            {
                return DefaultApiHost;
            }

            var n = v.Trim();
            if (!n.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                n = "https://" + n;
            }

            n = n.TrimEnd('/');
            if (!n.EndsWith("/v4", StringComparison.OrdinalIgnoreCase))
            {
                n += "/v4";
            }

            return n + "/";
        }

        private static string? NormalizeLanguage(string? v)
        {
            if (string.IsNullOrWhiteSpace(v))
            {
                return null;
            }

            var n = v.Trim().ToUpperInvariant();
            if (n.StartsWith("ZH", StringComparison.Ordinal))
            {
                return "zho";
            }

            if (n.StartsWith("EN", StringComparison.Ordinal))
            {
                return "eng";
            }

            if (n.StartsWith("JA", StringComparison.Ordinal))
            {
                return "jpn";
            }

            if (n.StartsWith("KO", StringComparison.Ordinal))
            {
                return "kor";
            }

            return null;
        }

        private static Uri? FixImageUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(url, UriKind.Absolute);
            }

            return new Uri("https://artworks.thetvdb.com" + (url.StartsWith('/') ? url : "/" + url), UriKind.Absolute);
        }

        private static bool IsEnabled()
        {
            var config = MetaSharkPlugin.Instance?.Configuration;
            return !string.IsNullOrWhiteSpace(config?.TvdbApiKey);
        }

        private async Task<string?> EnsureTokenAsync(CancellationToken cancellationToken)
        {
            if (this.memoryCache.TryGetValue<string>(TokenCacheKey, out var t) && !string.IsNullOrWhiteSpace(t))
            {
                return t;
            }

            if (string.IsNullOrWhiteSpace(this.apiKey))
            {
                return null;
            }

            try
            {
                var payload = new Dictionary<string, string> { ["apikey"] = this.apiKey };
                if (!string.IsNullOrWhiteSpace(this.pin))
                {
                    payload["pin"] = this.pin;
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, "login") { Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json") };
                using var response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var login = JsonSerializer.Deserialize<TvdbLoginResponse>(json, JsonOptions);
                t = login?.Data?.Token;
                if (string.IsNullOrWhiteSpace(t))
                {
                    return null;
                }

                this.memoryCache.Set(TokenCacheKey, t, TimeSpan.FromDays(20));
                return t;
            }
            catch (JsonException) { return null; }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
        }

        private async Task<HttpResponseMessage?> SendWithTokenAsync(Func<HttpRequestMessage> factory, CancellationToken token)
        {
            try
            {
                var t = await this.EnsureTokenAsync(token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(t))
                {
                    return null;
                }

                using var req = factory();
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                var res = await this.httpClient.SendAsync(req, token).ConfigureAwait(false);
                if (res.StatusCode == HttpStatusCode.Unauthorized)
                {
                    res.Dispose();
                    this.memoryCache.Remove(TokenCacheKey);
                    t = await this.EnsureTokenAsync(token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(t))
                    {
                        return null;
                    }

                    using var rreq = factory();
                    rreq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await this.httpClient.SendAsync(rreq, token).ConfigureAwait(false);
                }

                return res;
            }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbSearchResponse
        {
            [JsonPropertyName("data")]
            public List<TvdbSearchResult>? Data { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbSeriesResponse
        {
            [JsonPropertyName("data")]
            public TvdbSeries? Data { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbLoginResponse
        {
            [JsonPropertyName("data")]
            public TvdbLoginData? Data { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbLoginData
        {
            [JsonPropertyName("token")]
            public string? Token { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbEpisodesResponse
        {
            [JsonPropertyName("data")]
            public TvdbEpisodesData? Data { get; set; }

            [JsonPropertyName("links")]
            public TvdbLinks? Links { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbEpisodesData
        {
            [JsonPropertyName("episodes")]
            public List<TvdbEpisodeBaseRecord>? Episodes { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbLinks
        {
            [JsonPropertyName("next")]
            public string? Next { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
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

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("overview")]
            public string? Overview { get; set; }

            [JsonPropertyName("image")]
            public string? Image { get; set; }
        }

        [SuppressMessage("Performance", "CA1812", Justification = "Deserialized by System.Text.Json")]
        private sealed class TvdbEpisodeGroupResponse
        {
            [JsonPropertyName("data")]
            public TvdbEpisodeGroup? Data { get; set; }
        }
    }
}
