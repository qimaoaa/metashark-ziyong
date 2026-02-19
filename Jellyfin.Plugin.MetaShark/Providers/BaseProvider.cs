// <copyright file="BaseProvider.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Configuration;
    using Jellyfin.Plugin.MetaShark.Core;
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using TMDbLib.Objects.General;
    using TMDbLib.Objects.Languages;

    /// <summary>
    /// Base provider for MetaShark.
    /// </summary>
    public abstract class BaseProvider
    {
        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public const string DoubanProviderName = "Douban";

        /// <summary>
        /// Gets the provider id.
        /// </summary>
        public const string DoubanProviderId = "DoubanID";

        /// <summary>
        /// Name of the provider.
        /// </summary>
        public const string TmdbProviderName = "TheMovieDb";

        private static readonly Action<ILogger, string, Exception?> LogMetaSharkInfo =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(Log)), "[MetaShark] {Message}");

        private readonly ILogger logger;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly DoubanApi doubanApi;
        private readonly TmdbApi tmdbApi;
        private readonly TvdbApi tvdbApi;
        private readonly OmdbApi omdbApi;
        private readonly ImdbApi imdbApi;
        private readonly ILibraryManager libraryManager;
        private readonly IHttpContextAccessor httpContextAccessor;

        private readonly Regex regMetaSourcePrefix = new Regex(@"^\[.+\]", RegexOptions.Compiled);
        private readonly Regex regSeasonNameSuffix = new Regex(@"\s第[0-9一二三四五六七八九十]+?季$|\sSeason\s\d+?$|(?<![0-9a-zA-Z])\d$", RegexOptions.Compiled);
        private readonly Regex regDoubanIdAttribute = new Regex(@"\[(?:douban|doubanid)-(\d+?)\]", RegexOptions.Compiled);
        private readonly Regex regTmdbIdAttribute = new Regex(@"\[(?:tmdb|tmdbid)-(\d+?)\]", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseProvider"/> class.
        /// </summary>
        protected BaseProvider(IHttpClientFactory httpClientFactory, ILogger logger, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
        {
            this.doubanApi = doubanApi;
            this.tmdbApi = tmdbApi;
            this.tvdbApi = tvdbApi;
            this.omdbApi = omdbApi;
            this.imdbApi = imdbApi;
            this.libraryManager = libraryManager;
            this.logger = logger;
            this.httpClientFactory = httpClientFactory;
            this.httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        protected static PluginConfiguration Config => MetaSharkPlugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the logger.
        /// </summary>
        protected ILogger Logger => this.logger;

        /// <summary>
        /// Gets the http client factory.
        /// </summary>
        protected IHttpClientFactory HttpClientFactory => this.httpClientFactory;

        /// <summary>
        /// Gets the douban api.
        /// </summary>
        protected DoubanApi DoubanApi => this.doubanApi;

        /// <summary>
        /// Gets the tmdb api.
        /// </summary>
        protected TmdbApi TmdbApi => this.tmdbApi;

        /// <summary>
        /// Gets the tvdb api.
        /// </summary>
        protected TvdbApi TvdbApi => this.tvdbApi;

        /// <summary>
        /// Gets the omdb api.
        /// </summary>
        protected OmdbApi OmdbApi => this.omdbApi;

        /// <summary>
        /// Gets the imdb api.
        /// </summary>
        protected ImdbApi ImdbApi => this.imdbApi;

        /// <summary>
        /// Gets the library manager.
        /// </summary>
        protected ILibraryManager LibraryManager => this.libraryManager;

        /// <summary>
        /// Gets the http context accessor.
        /// </summary>
        protected IHttpContextAccessor HttpContextAccessor => this.httpContextAccessor;

        /// <summary>
        /// Gets the meta source prefix regex.
        /// </summary>
        protected Regex RegMetaSourcePrefix => this.regMetaSourcePrefix;

        /// <summary>
        /// Gets the season name suffix regex.
        /// </summary>
        protected Regex RegSeasonNameSuffix => this.regSeasonNameSuffix;

        /// <summary>
        /// Gets the douban id attribute regex.
        /// </summary>
        protected Regex RegDoubanIdAttribute => this.regDoubanIdAttribute;

        /// <summary>
        /// Gets the tmdb id attribute regex.
        /// </summary>
        protected Regex RegTmdbIdAttribute => this.regTmdbIdAttribute;

        /// <summary>
        /// Gets the image response.
        /// </summary>
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(url);
            return this.GetImageResponse(new Uri(url, UriKind.RelativeOrAbsolute), cancellationToken);
        }

        /// <summary>
        /// Gets the image response.
        /// </summary>
        public async Task<HttpResponseMessage> GetImageResponse(Uri url, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(url);
            var urlString = url.ToString();
            if (urlString.Contains("doubanio.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!urlString.StartsWith("http", StringComparison.OrdinalIgnoreCase) && MetaSharkPlugin.Instance != null)
                {
                    urlString = MetaSharkPlugin.Instance.GetLocalApiBaseUrl().ToString().TrimEnd('/') + urlString;
                }

                if (urlString.Contains("/proxy/image", StringComparison.Ordinal))
                {
                    var uri = new UriBuilder(urlString);
                    var originalUrl = HttpUtility.ParseQueryString(uri.Query).Get("url");
                    if (!string.IsNullOrEmpty(originalUrl))
                    {
                        urlString = originalUrl;
                    }
                }

                this.Log("GetImageResponse url: {0}", urlString);

                using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, urlString))
                {
                    requestMessage.Headers.Add("User-Agent", DoubanApi.HTTPUSERAGENT);
                    requestMessage.Headers.Add("Referer", DoubanApi.HTTPREFERER);
                    using var client = this.HttpClientFactory.CreateClient();
                    return await client.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                this.Log("GetImageResponse url: {0}", urlString);
                using var client = this.HttpClientFactory.CreateClient();
                return await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Guesses the douban season by year.
        /// </summary>
        public async Task<string?> GuestDoubanSeasonByYearAsync(string seriesName, int? year, int? seasonNumber, CancellationToken cancellationToken)
        {
            if (year == null || year == 0)
            {
                return null;
            }

            this.Log($"GuestDoubanSeasonByYear of [name]: {seriesName} [year]: {year}");

            if (Config.EnableDoubanAvoidRiskControl)
            {
                var suggestResult = await this.DoubanApi.SearchBySuggestAsync(seriesName, cancellationToken).ConfigureAwait(false);
                var suggestItem = suggestResult.Where(x => x.Year == year && x.Name == seriesName).FirstOrDefault();
                if (suggestItem != null)
                {
                    this.Log($"Found douban [id]: {suggestItem.Name}({suggestItem.Sid}) (suggest)");
                    return suggestItem.Sid;
                }

                suggestItem = suggestResult.Where(x => x.Year == year).FirstOrDefault();
                if (suggestItem != null)
                {
                    this.Log($"Found douban [id]: {suggestItem.Name}({suggestItem.Sid}) (suggest)");
                    return suggestItem.Sid;
                }
            }

            var result = await this.DoubanApi.SearchAsync(seriesName, cancellationToken).ConfigureAwait(false);
            var item = result.Where(x => x.Category == "电视剧" && x.Year == year).FirstOrDefault();
            if (item != null && !string.IsNullOrEmpty(item.Sid))
            {
                var nameIndexNumber = ParseChineseSeasonNumberByName(item.Name);
                if (nameIndexNumber.HasValue && seasonNumber.HasValue && nameIndexNumber != seasonNumber)
                {
                    this.Log("GuestDoubanSeasonByYear not found!");
                    return null;
                }

                this.Log($"Found douban [id]: {item.Name}({item.Sid})");
                return item.Sid;
            }

            this.Log("GuestDoubanSeasonByYear not found!");
            return null;
        }

        /// <summary>
        /// Guesses the douban season by season name.
        /// </summary>
        public async Task<string?> GuestDoubanSeasonBySeasonNameAsync(string name, int? seasonNumber, CancellationToken cancellationToken)
        {
            if (seasonNumber is null or 0)
            {
                return null;
            }

            var chineseSeasonNumber = Utils.ToChineseNumber(seasonNumber);
            if (string.IsNullOrEmpty(chineseSeasonNumber))
            {
                return null;
            }

            var seasonName = (seasonNumber == 1) ? name : $"{name}{seasonNumber}";
            var chineseSeasonName = $"{name} 第{chineseSeasonNumber}季";

            this.Log($"GuestDoubanSeasonBySeasonNameAsync of [name]: {seasonName} 或 {chineseSeasonName}");

            var result = await this.DoubanApi.SearchAsync(name, cancellationToken).ConfigureAwait(false);
            var item = result.Where(x => x.Category == "电视剧" && x.Rating > 0 && (x.Name == seasonName || x.Name == chineseSeasonName)).FirstOrDefault();
            if (item != null && !string.IsNullOrEmpty(item.Sid))
            {
                this.Log($"Found douban [id]: {item.Name}({item.Sid})");
                return item.Sid;
            }

            this.Log("GuestDoubanSeasonBySeasonNameAsync not found!");
            return null;
        }

        /// <summary>
        /// Guesses the season number by directory name.
        /// </summary>
        public int? GuessSeasonNumberByDirectoryName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                this.Log($"Season path is empty!");
                return null;
            }

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            var regSeason = new Regex(@"第([0-9零一二三四五六七八九]+?)(季|部)", RegexOptions.Compiled);
            var match = regSeason.Match(fileName);
            if (match.Success && match.Groups.Count > 1)
            {
                var seasonNumber = match.Groups[1].Value.ToInt();
                if (seasonNumber <= 0)
                {
                    seasonNumber = Utils.ChineseNumberToInt(match.Groups[1].Value) ?? 0;
                }

                if (seasonNumber > 0)
                {
                    this.Log($"Found season number of filename: {fileName} seasonNumber: {seasonNumber}");
                    return seasonNumber;
                }
            }

            regSeason = new Regex(@"(?<![a-z])S(\d\d?)(?![0-9a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = regSeason.Match(fileName);
            if (match.Success && match.Groups.Count > 1)
            {
                var seasonNumber = match.Groups[1].Value.ToInt();
                if (seasonNumber > 0)
                {
                    this.Log($"Found season number of filename: {fileName} seasonNumber: {seasonNumber}");
                    return seasonNumber;
                }
            }

            var seasonNameMap = new Dictionary<string, int>()
            {
                { @"[ ._](I|1st)[ ._]", 1 },
                { @"[ ._](II|2nd)[ ._]", 2 },
                { @"[ ._](III|3rd)[ ._]", 3 },
                { @"[ ._](IIII|4th)[ ._]", 3 },
            };

            foreach (var entry in seasonNameMap)
            {
                if (Regex.IsMatch(fileName, entry.Key))
                {
                    this.Log($"Found season number of filename: {fileName} seasonNumber: {entry.Value}");
                    return entry.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Guesses the tvdb id.
        /// </summary>
        protected async Task<string?> GuessByTvdbAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var fileName = GetOriginalFileName(info);

            var parseResult = NameParser.Parse(fileName);
            var searchName = !string.IsNullOrEmpty(parseResult.ChineseName) ? parseResult.ChineseName : parseResult.Name;

            this.Log($"GuessByTvdb of [name]: {info.Name} [file_name]: {fileName} [search name]: {searchName}");

            var results = await this.TvdbApi.SearchSeriesAsync(searchName, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);

            if (info.Year.HasValue && info.Year > 0)
            {
                var item = results.FirstOrDefault(x => x.Year == info.Year.Value.ToString(CultureInfo.InvariantCulture) && (x.Name == searchName || x.Name == info.Name));
                if (item != null)
                {
                    var finalId = !string.IsNullOrEmpty(item.TvdbId) ? item.TvdbId : item.Id;
                    this.Log($"Found tvdb [id]: {item.Name}({finalId}) by year match");
                    return finalId;
                }
            }

            var nameMatch = results.FirstOrDefault(x => x.Name == searchName || x.Name == info.Name);
            if (nameMatch != null)
            {
                var finalId = !string.IsNullOrEmpty(nameMatch.TvdbId) ? nameMatch.TvdbId : nameMatch.Id;
                this.Log($"Found tvdb [id]: {nameMatch.Name}({finalId}) by name match");
                return finalId;
            }

            if (results.Any())
            {
                var item = results[0];
                var finalId = !string.IsNullOrEmpty(item.TvdbId) ? item.TvdbId : item.Id;
                this.Log($"Found tvdb [id]: {item.Name}({finalId}) by first match");
                return finalId;
            }

            return null;
        }

        /// <summary>
        /// Guesses the douban sid.
        /// </summary>
        protected async Task<string?> GuessByDoubanAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var fileName = GetOriginalFileName(info);

            var doubanId = this.RegDoubanIdAttribute.FirstMatchGroup(fileName);
            if (!string.IsNullOrWhiteSpace(doubanId))
            {
                this.Log($"Found douban [id] by attr: {doubanId}");
                return doubanId;
            }

            var parseResult = NameParser.Parse(fileName);
            var searchName = !string.IsNullOrEmpty(parseResult.ChineseName) ? parseResult.ChineseName : parseResult.Name;
            info.Year = parseResult.Year;

            this.Log($"GuessByDouban of [name]: {info.Name} [file_name]: {fileName} [year]: {info.Year} [search name]: {searchName}");
            List<DoubanSubject> result;

            if (Config.EnableDoubanAvoidRiskControl)
            {
                if (info.Year != null && info.Year > 0)
                {
                    result = await this.DoubanApi.SearchBySuggestAsync(searchName, cancellationToken).ConfigureAwait(false);
                    var item = result.Where(x => x.Year == info.Year && x.Name == searchName).FirstOrDefault();
                    if (item != null)
                    {
                        this.Log($"Found douban [id]: {item.Name}({item.Sid}) (suggest)");
                        return item.Sid;
                    }

                    item = result.Where(x => x.Year == info.Year).FirstOrDefault();
                    if (item != null)
                    {
                        this.Log($"Found douban [id]: {item.Name}({item.Sid}) (suggest)");
                        return item.Sid;
                    }
                }
            }

            result = await this.DoubanApi.SearchAsync(searchName, cancellationToken).ConfigureAwait(false);
            var cat = info is MovieInfo ? "电影" : "电视剧";

            if (info.Year != null && info.Year > 0)
            {
                var item = result.Where(x => x.Category == cat && x.Year == info.Year).FirstOrDefault();
                if (item != null)
                {
                    this.Log($"Found douban [id]: {item.Name}({item.Sid})");
                    return item.Sid;
                }
                else
                {
                    return null;
                }
            }

            var first = result.Where(x => x.Category == cat).FirstOrDefault();
            if (first != null)
            {
                this.Log($"Found douban [id] by first match: {first.Name}({first.Sid})");
                return first.Sid;
            }

            return null;
        }

        /// <summary>
        /// Guesses the tmdb id.
        /// </summary>
        protected async Task<string?> GuestByTmdbAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var fileName = GetOriginalFileName(info);

            var tmdbId = this.RegTmdbIdAttribute.FirstMatchGroup(fileName);
            if (!string.IsNullOrWhiteSpace(tmdbId))
            {
                this.Log($"Found tmdb [id] by attr: {tmdbId}");
                return tmdbId;
            }

            var parseResult = NameParser.Parse(fileName);
            var searchName = !string.IsNullOrEmpty(parseResult.ChineseName) ? parseResult.ChineseName : parseResult.Name;
            info.Year = parseResult.Year;

            return await this.GuestByTmdbAsync(searchName, info.Year, info, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Guesses the tmdb id by name and year.
        /// </summary>
        protected async Task<string?> GuestByTmdbAsync(string name, int? year, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            this.Log($"GuestByTmdb of [name]: {name} [year]: {year}");
            switch (info)
            {
                case MovieInfo:
                    var movieResults = await this.TmdbApi.SearchMovieAsync(name, year ?? 0, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                    var movieItem = movieResults.FirstOrDefault(x => x.Title == name || x.OriginalTitle == name) ?? (movieResults.Count > 0 ? movieResults[0] : null);
                    if (movieItem != null)
                    {
                        this.Log($"Found tmdb [id]: {movieItem.Title}({movieItem.Id})");
                        return movieItem.Id.ToString(CultureInfo.InvariantCulture);
                    }
                    break;
                case SeriesInfo:
                    var seriesResults = await this.TmdbApi.SearchSeriesAsync(name, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                    var seriesItem = seriesResults.FirstOrDefault(x => (x.Name == name || x.OriginalName == name) && x.FirstAirDate?.Year == year) ?? seriesResults.FirstOrDefault(x => x.FirstAirDate?.Year == year) ?? seriesResults.FirstOrDefault(x => x.Name == name || x.OriginalName == name) ?? (seriesResults.Count > 0 ? seriesResults[0] : null);
                    if (seriesItem != null)
                    {
                        this.Log($"Found tmdb [id]: -> {seriesItem.Name}({seriesItem.Id})");
                        return seriesItem.Id.ToString(CultureInfo.InvariantCulture);
                    }
                    break;
            }

            this.Log($"Not found tmdb id by [name]: {name} [year]: {year}");
            return null;
        }

        /// <summary>
        /// Gets the tmdb id by imdb id.
        /// </summary>
        protected async Task<string?> GetTmdbIdByImdbAsync(string imdb, string language, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(imdb))
            {
                return null;
            }

            var findResult = await this.TmdbApi.FindByExternalIdAsync(imdb, TMDbLib.Objects.Find.FindExternalSource.Imdb, language, cancellationToken).ConfigureAwait(false);

            if (info is MovieInfo && findResult?.MovieResults != null && findResult.MovieResults.Count > 0)
            {
                var tmdbId = findResult.MovieResults[0].Id;
                this.Log($"Found tmdb [id]: {tmdbId} by imdb id: {imdb}");
                return $"{tmdbId}";
            }

            if (info is SeriesInfo)
            {
                if (findResult?.TvResults != null && findResult.TvResults.Count > 0)
                {
                    var tmdbId = findResult.TvResults[0].Id;
                    this.Log($"Found tmdb [id]: {tmdbId} by imdb id: {imdb}");
                    return $"{tmdbId}";
                }

                if (findResult?.TvEpisode != null && findResult.TvEpisode.Count > 0)
                {
                    var tmdbId = findResult.TvEpisode[0].ShowId;
                    this.Log($"Found tmdb [id]: {tmdbId} by imdb id: {imdb}");
                    return $"{tmdbId}";
                }

                if (findResult?.TvSeason != null && findResult.TvSeason.Count > 0)
                {
                    var tmdbId = findResult.TvSeason[0].ShowId;
                    this.Log($"Found tmdb [id]: {tmdbId} by imdb id: {imdb}");
                    return $"{tmdbId}";
                }
            }

            this.Log($"Not found tmdb id by imdb id: {imdb}");
            return null;
        }

        /// <summary>
        /// Checks for a new imdb id via omdb.
        /// </summary>
        protected async Task<string> CheckNewImdbID(string imdb, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(imdb))
            {
                return imdb;
            }

            var omdbItem = await this.OmdbApi.GetByImdbID(imdb, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(omdbItem?.ImdbID))
            {
                imdb = omdbItem.ImdbID;
            }

            return imdb;
        }

        /// <summary>
        /// Gets the proxy image url.
        /// </summary>
        protected Uri GetProxyImageUrl(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);
            var baseUrl = this.GetBaseUrl();
            var encodedUrl = HttpUtility.UrlEncode(url.ToString());
            return new Uri($"{baseUrl}/plugin/metashark/proxy/image/?url={encodedUrl}", UriKind.Absolute);
        }

        /// <summary>
        /// Logs a message.
        /// </summary>
        protected void Log(string? message, params object?[] args)
        {
            var format = message ?? string.Empty;
            var formatted = string.Format(CultureInfo.InvariantCulture, format, args);
            LogMetaSharkInfo(this.Logger, formatted, null);
        }

        /// <summary>
        /// Gets the douban poster url.
        /// </summary>
        protected string GetDoubanPoster(DoubanSubject subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            if (string.IsNullOrEmpty(subject.Img))
            {
                return string.Empty;
            }

            var url = Config.EnableDoubanLargePoster ? subject.ImgLarge : subject.ImgMiddle;
            return this.GetProxyImageUrl(new Uri(url, UriKind.Absolute)).ToString();
        }

        /// <summary>
        /// Gets the original season path.
        /// </summary>
        protected string? GetOriginalSeasonPath(EpisodeInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.Path == null)
            {
                return null;
            }

            var seasonPath = Path.GetDirectoryName(info.Path);
            if (string.IsNullOrEmpty(seasonPath))
            {
                return null;
            }

            var item = this.LibraryManager.FindByPath(seasonPath, true);

            if (item is Series)
            {
                return null;
            }

            return seasonPath;
        }

        /// <summary>
        /// Checks if it is a virtual season.
        /// </summary>
        protected bool IsVirtualSeason(EpisodeInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.Path == null)
            {
                return false;
            }

            var seasonPath = Path.GetDirectoryName(info.Path);
            if (string.IsNullOrEmpty(seasonPath))
            {
                return false;
            }

            var parent = this.LibraryManager.FindByPath(seasonPath, true);

            if (parent is Series)
            {
                return true;
            }

            var seriesPath = Path.GetDirectoryName(seasonPath);
            if (string.IsNullOrEmpty(seriesPath))
            {
                return false;
            }

            var series = this.LibraryManager.FindByPath(seriesPath, true);

            if (series is Series && parent is not Season)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes the season suffix from name.
        /// </summary>
        protected string RemoveSeasonSuffix(string name)
        {
            return this.RegSeasonNameSuffix.Replace(name, string.Empty);
        }

        /// <summary>
        /// Gets the episode from tmdb.
        /// </summary>
        protected async Task<TMDbLib.Objects.Search.TvSeasonEpisode?> GetEpisodeAsync(int seriesTmdbId, int? seasonNumber, int? episodeNumber, string displayOrder, string? language, string? imageLanguages, CancellationToken cancellationToken)
        {
            if (!seasonNumber.HasValue || !episodeNumber.HasValue)
            {
                return null;
            }

            var normalizedLanguage = language ?? string.Empty;
            var normalizedImageLanguages = imageLanguages ?? string.Empty;
            var seriesIdStr = seriesTmdbId.ToString(CultureInfo.InvariantCulture);
            if (TmdbEpisodeGroupMapping.TryGetGroupId(Config.TmdbEpisodeGroupMap, seriesIdStr, out var groupId))
            {
                var group = await this.TmdbApi
                    .GetEpisodeGroupByIdAsync(groupId, normalizedLanguage, cancellationToken)
                    .ConfigureAwait(false);
                if (group != null)
                {
                    var season = group.Groups.Find(s => s.Order == seasonNumber);

                    var ep = season?.Episodes.Find(e => e.Order == episodeNumber - 1);
                    if (ep is not null)
                    {
                        var result = await this.TmdbApi
                            .GetSeasonAsync(seriesTmdbId, ep.SeasonNumber, normalizedLanguage, normalizedImageLanguages, cancellationToken)
                            .ConfigureAwait(false);
                        if (result?.Episodes != null && ep.EpisodeNumber <= result.Episodes.Count)
                        {
                            return result.Episodes[ep.EpisodeNumber - 1];
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(displayOrder))
            {
                var group = await this.TmdbApi
                    .GetSeriesGroupAsync(seriesTmdbId, displayOrder, normalizedLanguage, normalizedImageLanguages, cancellationToken)
                    .ConfigureAwait(false);
                if (group != null)
                {
                    var season = group.Groups.Find(s => s.Order == seasonNumber);

                    var ep = season?.Episodes.Find(e => e.Order == episodeNumber - 1);
                    if (ep is not null)
                    {
                        var result = await this.TmdbApi
                            .GetSeasonAsync(seriesTmdbId, ep.SeasonNumber, normalizedLanguage, normalizedImageLanguages, cancellationToken)
                            .ConfigureAwait(false);
                        if (result?.Episodes != null && ep.EpisodeNumber <= result.Episodes.Count)
                        {
                            return result.Episodes[ep.EpisodeNumber - 1];
                        }
                    }
                }
            }

            var seasonResult = await this.TmdbApi
                .GetSeasonAsync(seriesTmdbId, seasonNumber.Value, normalizedLanguage, normalizedImageLanguages, cancellationToken)
                .ConfigureAwait(false);
            if (seasonResult?.Episodes != null && episodeNumber.Value <= seasonResult.Episodes.Count)
            {
                return seasonResult.Episodes[episodeNumber.Value - 1];
            }

            return null;
        }

        private static int? ParseChineseSeasonNumberByName(string name)
        {
            var regSeason = new Regex(@"\s第([0-9零一二三四五六七八九]+?)(季|部)", RegexOptions.Compiled);
            var match = regSeason.Match(name);
            if (match.Success && match.Groups.Count > 1)
            {
                var sn = match.Groups[1].Value.ToInt();
                if (sn <= 0)
                {
                    sn = Utils.ChineseNumberToInt(match.Groups[1].Value) ?? 0;
                }

                if (sn > 0)
                {
                    return sn;
                }
            }

            return null;
        }

        private static T? FindFirst<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (predicate(item))
                {
                    return item;
                }
            }

            return default;
        }

        private string GetBaseUrl()
        {
            var proxyBaseUrl = Config.DoubanImageProxyBaseUrl;
            if (!string.IsNullOrWhiteSpace(proxyBaseUrl))
            {
                return proxyBaseUrl.TrimEnd('/');
            }

            if (MetaSharkPlugin.Instance != null && this.HttpContextAccessor.HttpContext != null)
            {
                return MetaSharkPlugin.Instance.GetApiBaseUrl(this.HttpContextAccessor.HttpContext.Request).ToString();
            }

            return MetaSharkPlugin.Instance?.GetLocalApiBaseUrl().ToString() ?? string.Empty;
        }
    }
}
