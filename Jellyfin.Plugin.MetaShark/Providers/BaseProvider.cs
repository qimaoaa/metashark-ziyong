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

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(url);
            return this.GetImageResponse(new Uri(url, UriKind.RelativeOrAbsolute), cancellationToken);
        }

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetImageResponse(Uri url, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(url);
            var urlString = url.ToString();
            if (urlString.Contains("doubanio.com", StringComparison.OrdinalIgnoreCase))
            {
                // 相对链接补全
                if (!urlString.StartsWith("http", StringComparison.OrdinalIgnoreCase) && MetaSharkPlugin.Instance != null)
                {
                    urlString = MetaSharkPlugin.Instance.GetLocalApiBaseUrl().ToString().TrimEnd('/') + urlString;
                }

                // 包含了代理地址的话，从url解析出原始豆瓣图片地址
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

                // 豆瓣图，带referer下载
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

        protected static PluginConfiguration Config => MetaSharkPlugin.Instance?.Configuration ?? new PluginConfiguration();

        protected static string MapDisplayOrderToTvdbType(string? displayOrder)
        {
            if (string.IsNullOrEmpty(displayOrder))
            {
                return "official";
            }

            return displayOrder.ToLowerInvariant() switch
            {
                "aired" => "official",
                "dvd" => "dvd",
                "absolute" => "absolute",
                _ => "official",
            };
        }

        protected static string GetOriginalFileName(ItemLookupInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (string.IsNullOrEmpty(info.Path))
            {
                return info.Name;
            }

            switch (info)
            {
                case MovieInfo:
                    var directoryName = Path.GetFileName(Path.GetDirectoryName(info.Path));
                    if (!string.IsNullOrEmpty(directoryName) && !string.IsNullOrEmpty(info.Name) && directoryName.Contains(info.Name, StringComparison.Ordinal))
                    {
                        return directoryName;
                    }

                    return Path.GetFileNameWithoutExtension(info.Path) ?? info.Name;
                case EpisodeInfo:
                    return Path.GetFileNameWithoutExtension(info.Path) ?? info.Name;
                default:
                    return Path.GetFileName(info.Path) ?? info.Name;
            }
        }

        protected static Uri GetLocalProxyImageUrl(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);
            var baseUrl = MetaSharkPlugin.Instance?.GetLocalApiBaseUrl().ToString() ?? string.Empty;
            var proxyBaseUrl = Config.DoubanImageProxyBaseUrl;
            if (!string.IsNullOrWhiteSpace(proxyBaseUrl))
            {
                baseUrl = proxyBaseUrl.TrimEnd('/');
            }

            var encodedUrl = HttpUtility.UrlEncode(url.ToString());
            return new Uri($"{baseUrl}/plugin/metashark/proxy/image/?url={encodedUrl}", UriKind.Absolute);
        }

        protected static string AdjustImageLanguage(string imageLanguage, string requestLanguage)
        {
            if (!string.IsNullOrEmpty(imageLanguage)
                && !string.IsNullOrEmpty(requestLanguage)
                && requestLanguage.Length > 2
                && imageLanguage.Length == 2
                && requestLanguage.StartsWith(imageLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return requestLanguage;
            }

            return imageLanguage;
        }

        protected static Collection<RemoteImageInfo> AdjustImageLanguagePriority(IList<RemoteImageInfo> images, string preferLanguage, string alternativeLanguage)
        {
            var imagesOrdered = images.OrderByLanguageDescending(preferLanguage, alternativeLanguage).ToList();
            if (alternativeLanguage == "ja" && !imagesOrdered.Any(x => x.Language == preferLanguage))
            {
                var idx = imagesOrdered.FindIndex(x => x.Language == alternativeLanguage);
                if (idx >= 0)
                {
                    imagesOrdered[idx].Language = null;
                }
            }

            return new Collection<RemoteImageInfo>(imagesOrdered);
        }

        protected static string MapCrewToPersonType(Crew crew)
        {
            ArgumentNullException.ThrowIfNull(crew);
            if (crew.Department.Equals("production", StringComparison.InvariantCultureIgnoreCase)
                && crew.Job.Contains("director", StringComparison.InvariantCultureIgnoreCase))
            {
                return PersonType.Director;
            }

            if (crew.Department.Equals("production", StringComparison.InvariantCultureIgnoreCase)
                && crew.Job.Contains("producer", StringComparison.InvariantCultureIgnoreCase))
            {
                return PersonType.Producer;
            }

            if (crew.Department.Equals("writing", StringComparison.InvariantCultureIgnoreCase))
            {
                return PersonType.Writer;
            }

            return string.Empty;
        }

        protected ILogger Logger => this.logger;

        protected IHttpClientFactory HttpClientFactory => this.httpClientFactory;

        protected DoubanApi DoubanApi => this.doubanApi;

        protected TmdbApi TmdbApi => this.tmdbApi;

        protected TvdbApi TvdbApi => this.tvdbApi;

        protected OmdbApi OmdbApi => this.omdbApi;

        protected ImdbApi ImdbApi => this.imdbApi;

        protected ILibraryManager LibraryManager => this.libraryManager;

        protected IHttpContextAccessor HttpContextAccessor => this.httpContextAccessor;

        protected Regex RegMetaSourcePrefix => this.regMetaSourcePrefix;

        protected Regex RegSeasonNameSuffix => this.regSeasonNameSuffix;

        protected Regex RegDoubanIdAttribute => this.regDoubanIdAttribute;

        protected Regex RegTmdbIdAttribute => this.regTmdbIdAttribute;

        protected async Task<string?> GuessByTvdbAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var fileName = GetOriginalFileName(info);
            var parseResult = NameParser.Parse(fileName);
            var searchName = !string.IsNullOrEmpty(parseResult.ChineseName) ? parseResult.ChineseName : parseResult.Name;
            var results = await this.TvdbApi.SearchSeriesAsync(searchName, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (info.Year.HasValue && info.Year > 0)
            {
                var item = results.FirstOrDefault(x => x.Year == info.Year.Value.ToString(CultureInfo.InvariantCulture) && (x.Name == searchName || x.Name == info.Name));
                if (item != null)
                {
                    var finalId = !string.IsNullOrEmpty(item.TvdbId) ? item.TvdbId : item.Id;
                    return finalId;
                }
            }

            var nameMatch = results.FirstOrDefault(x => x.Name == searchName || x.Name == info.Name);
            if (nameMatch != null)
            {
                var finalId = !string.IsNullOrEmpty(nameMatch.TvdbId) ? nameMatch.TvdbId : nameMatch.Id;
                return finalId;
            }

            if (results.Any())
            {
                var item = results[0];
                var finalId = !string.IsNullOrEmpty(item.TvdbId) ? item.TvdbId : item.Id;
                return finalId;
            }

            return null;
        }

        protected async Task<string?> GuessByDoubanAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var fileName = GetOriginalFileName(info);
            var doubanId = this.RegDoubanIdAttribute.FirstMatchGroup(fileName);
            if (!string.IsNullOrWhiteSpace(doubanId)) return doubanId;
            var parseResult = NameParser.Parse(fileName);
            var searchName = !string.IsNullOrEmpty(parseResult.ChineseName) ? parseResult.ChineseName : parseResult.Name;
            info.Year = parseResult.Year;
            List<DoubanSubject> result;
            if (Config.EnableDoubanAvoidRiskControl)
            {
                if (info.Year != null && info.Year > 0)
                {
                    result = await this.DoubanApi.SearchBySuggestAsync(searchName, cancellationToken).ConfigureAwait(false);
                    var item = result.Where(x => x.Year == info.Year && x.Name == searchName).FirstOrDefault();
                    if (item != null) return item.Sid;
                    item = result.Where(x => x.Year == info.Year).FirstOrDefault();
                    if (item != null) return item.Sid;
                }
            }

            result = await this.DoubanApi.SearchAsync(searchName, cancellationToken).ConfigureAwait(false);
            var cat = info is MovieInfo ? "电影" : "电视剧";
            if (info.Year != null && info.Year > 0)
            {
                var item = result.Where(x => x.Category == cat && x.Year == info.Year).FirstOrDefault();
                if (item != null) return item.Sid;
                return null;
            }

            var first = result.Where(x => x.Category == cat).FirstOrDefault();
            if (first != null) return first.Sid;
            return null;
        }

        public async Task<string?> GuestDoubanSeasonByYearAsync(string seriesName, int? year, int? seasonNumber, CancellationToken cancellationToken)
        {
            if (year == null || year == 0) return null;
            if (Config.EnableDoubanAvoidRiskControl)
            {
                var suggestResult = await this.DoubanApi.SearchBySuggestAsync(seriesName, cancellationToken).ConfigureAwait(false);
                var suggestItem = suggestResult.Where(x => x.Year == year && x.Name == seriesName).FirstOrDefault();
                if (suggestItem != null) return suggestItem.Sid;
                suggestItem = suggestResult.Where(x => x.Year == year).FirstOrDefault();
                if (suggestItem != null) return suggestItem.Sid;
            }

            var result = await this.DoubanApi.SearchAsync(seriesName, cancellationToken).ConfigureAwait(false);
            var item = result.Where(x => x.Category == "电视剧" && x.Year == year).FirstOrDefault();
            if (item != null && !string.IsNullOrEmpty(item.Sid))
            {
                var nameIndexNumber = ParseChineseSeasonNumberByName(item.Name);
                if (nameIndexNumber.HasValue && seasonNumber.HasValue && nameIndexNumber != seasonNumber) return null;
                return item.Sid;
            }

            return null;
        }

        public async Task<string?> GuestDoubanSeasonBySeasonNameAsync(string name, int? seasonNumber, CancellationToken cancellationToken)
        {
            if (seasonNumber is null or 0) return null;
            var chineseSeasonNumber = Utils.ToChineseNumber(seasonNumber);
            if (string.IsNullOrEmpty(chineseSeasonNumber)) return null;
            var seasonName = (seasonNumber == 1) ? name : $"{name}{seasonNumber}";
            var chineseSeasonName = $"{name} 第{chineseSeasonNumber}季";
            var result = await this.DoubanApi.SearchAsync(name, cancellationToken).ConfigureAwait(false);
            var item = result.Where(x => x.Category == "电视剧" && x.Rating > 0 && (x.Name == seasonName || x.Name == chineseSeasonName)).FirstOrDefault();
            if (item != null && !string.IsNullOrEmpty(item.Sid)) return item.Sid;
            return null;
        }

        public int? GuessSeasonNumberByDirectoryName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName)) return null;
            var regSeason = new Regex(@"第([0-9零一二三四五六七八九]+?)(季|部)", RegexOptions.Compiled);
            var match = regSeason.Match(fileName);
            if (match.Success && match.Groups.Count > 1)
            {
                var sn = match.Groups[1].Value.ToInt();
                if (sn <= 0) sn = Utils.ChineseNumberToInt(match.Groups[1].Value) ?? 0;
                if (sn > 0) return sn;
            }

            regSeason = new Regex(@"(?<![a-z])S(\d\d?)(?![0-9a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = regSeason.Match(fileName);
            if (match.Success && match.Groups.Count > 1)
            {
                var sn = match.Groups[1].Value.ToInt();
                if (sn > 0) return sn;
            }

            var seasonNameMap = new Dictionary<string, int>() { { @"[ ._](I|1st)[ ._]", 1 }, { @"[ ._](II|2nd)[ ._]", 2 }, { @"[ ._](III|3rd)[ ._]", 3 }, { @"[ ._](IIII|4th)[ ._]", 3 } };
            foreach (var entry in seasonNameMap) if (Regex.IsMatch(fileName, entry.Key)) return entry.Value;
            return null;
        }

        protected async Task<string?> GuestByTmdbAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var fileName = GetOriginalFileName(info);
            var tmdbId = this.RegTmdbIdAttribute.FirstMatchGroup(fileName);
            if (!string.IsNullOrWhiteSpace(tmdbId)) return tmdbId;
            var parseResult = NameParser.Parse(fileName);
            var searchName = !string.IsNullOrEmpty(parseResult.ChineseName) ? parseResult.ChineseName : parseResult.Name;
            info.Year = parseResult.Year;
            return await this.GuestByTmdbAsync(searchName, info.Year, info, cancellationToken).ConfigureAwait(false);
        }

        protected async Task<string?> GuestByTmdbAsync(string name, int? year, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            switch (info)
            {
                case MovieInfo:
                    var movieResults = await this.TmdbApi.SearchMovieAsync(name, year ?? 0, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                    var movieItem = movieResults.FirstOrDefault(x => x.Title == name || x.OriginalTitle == name) ?? (movieResults.Count > 0 ? movieResults[0] : null);
                    if (movieItem != null) return movieItem.Id.ToString(CultureInfo.InvariantCulture);
                    break;
                case SeriesInfo:
                    var seriesResults = await this.TmdbApi.SearchSeriesAsync(name, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                    var seriesItem = seriesResults.FirstOrDefault(x => (x.Name == name || x.OriginalName == name) && x.FirstAirDate?.Year == year) ?? seriesResults.FirstOrDefault(x => x.FirstAirDate?.Year == year) ?? seriesResults.FirstOrDefault(x => x.Name == name || x.OriginalName == name) ?? (seriesResults.Count > 0 ? seriesResults[0] : null);
                    if (seriesItem != null) return seriesItem.Id.ToString(CultureInfo.InvariantCulture);
                    break;
            }

            return null;
        }

        protected async Task<string?> GetTmdbIdByImdbAsync(string imdb, string language, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(imdb)) return null;
            var findResult = await this.TmdbApi.FindByExternalIdAsync(imdb, TMDbLib.Objects.Find.FindExternalSource.Imdb, language, cancellationToken).ConfigureAwait(false);
            if (info is MovieInfo && findResult?.MovieResults?.Count > 0) return findResult.MovieResults[0].Id.ToString(CultureInfo.InvariantCulture);
            if (info is SeriesInfo)
            {
                if (findResult?.TvResults?.Count > 0) return findResult.TvResults[0].Id.ToString(CultureInfo.InvariantCulture);
                if (findResult?.TvEpisode?.Count > 0) return findResult.TvEpisode[0].ShowId.ToString(CultureInfo.InvariantCulture);
                if (findResult?.TvSeason?.Count > 0) return findResult.TvSeason[0].ShowId.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        protected async Task<string> CheckNewImdbID(string imdb, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(imdb)) return imdb;
            var omdbItem = await this.OmdbApi.GetByImdbID(imdb, cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrEmpty(omdbItem?.ImdbID) ? omdbItem.ImdbID : imdb;
        }

        protected Uri GetProxyImageUrl(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);
            var baseUrl = this.GetBaseUrl();
            var encodedUrl = HttpUtility.UrlEncode(url.ToString());
            return new Uri($"{baseUrl}/plugin/metashark/proxy/image/?url={encodedUrl}", UriKind.Absolute);
        }

        protected void Log(string? message, params object?[] args)
        {
            var formatted = string.Format(CultureInfo.InvariantCulture, message ?? string.Empty, args);
            LogMetaSharkInfo(this.Logger, formatted, null);
        }

        protected string GetDoubanPoster(DoubanSubject subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            if (string.IsNullOrEmpty(subject.Img)) return string.Empty;
            var url = Config.EnableDoubanLargePoster ? subject.ImgLarge : subject.ImgMiddle;
            return this.GetProxyImageUrl(new Uri(url, UriKind.Absolute)).ToString();
        }

        protected string? GetOriginalSeasonPath(EpisodeInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.Path == null) return null;
            var seasonPath = Path.GetDirectoryName(info.Path);
            if (string.IsNullOrEmpty(seasonPath)) return null;
            var item = this.LibraryManager.FindByPath(seasonPath, true);
            if (item is Series) return null;
            return seasonPath;
        }

        protected bool IsVirtualSeason(EpisodeInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.Path == null) return false;
            var seasonPath = Path.GetDirectoryName(info.Path);
            if (string.IsNullOrEmpty(seasonPath)) return false;
            var parent = this.LibraryManager.FindByPath(seasonPath, true);
            if (parent is Series) return true;
            var seriesPath = Path.GetDirectoryName(seasonPath);
            if (string.IsNullOrEmpty(seriesPath)) return false;
            var series = this.LibraryManager.FindByPath(seriesPath, true);
            if (series is Series && parent is not Season) return true;
            return false;
        }

        protected string RemoveSeasonSuffix(string name)
        {
            return this.RegSeasonNameSuffix.Replace(name, string.Empty);
        }

        protected async Task<TMDbLib.Objects.Search.TvSeasonEpisode?> GetEpisodeAsync(int seriesTmdbId, int? seasonNumber, int? episodeNumber, string displayOrder, string? language, string? imageLanguages, CancellationToken cancellationToken)
        {
            if (!seasonNumber.HasValue || !episodeNumber.HasValue) return null;
            var normalizedLanguage = language ?? string.Empty;
            var normalizedImageLanguages = imageLanguages ?? string.Empty;
            var seriesIdStr = seriesTmdbId.ToString(CultureInfo.InvariantCulture);
            if (TmdbEpisodeGroupMapping.TryGetGroupId(Config.TmdbEpisodeGroupMap, seriesIdStr, out var groupId))
            {
                var group = await this.TmdbApi.GetEpisodeGroupByIdAsync(groupId, normalizedLanguage, cancellationToken).ConfigureAwait(false);
                if (group != null)
                {
                    var season = group.Groups.Find(s => s.Order == seasonNumber);
                    var ep = season?.Episodes.Find(e => e.Order == episodeNumber - 1);
                    if (ep is not null)
                    {
                        var result = await this.TmdbApi.GetSeasonAsync(seriesTmdbId, ep.SeasonNumber, normalizedLanguage, normalizedImageLanguages, cancellationToken).ConfigureAwait(false);
                        if (result?.Episodes != null && ep.EpisodeNumber <= result.Episodes.Count) return result.Episodes[ep.EpisodeNumber - 1];
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(displayOrder))
            {
                var group = await this.TmdbApi.GetSeriesGroupAsync(seriesTmdbId, displayOrder, normalizedLanguage, normalizedImageLanguages, cancellationToken).ConfigureAwait(false);
                if (group != null)
                {
                    var season = group.Groups.Find(s => s.Order == seasonNumber);
                    var ep = season?.Episodes.Find(e => e.Order == episodeNumber - 1);
                    if (ep is not null)
                    {
                        var result = await this.TmdbApi.GetSeasonAsync(seriesTmdbId, ep.SeasonNumber, normalizedLanguage, normalizedImageLanguages, cancellationToken).ConfigureAwait(false);
                        if (result?.Episodes != null && ep.EpisodeNumber <= result.Episodes.Count) return result.Episodes[ep.EpisodeNumber - 1];
                    }
                }
            }

            var seasonResult = await this.TmdbApi.GetSeasonAsync(seriesTmdbId, seasonNumber.Value, normalizedLanguage, normalizedImageLanguages, cancellationToken).ConfigureAwait(false);
            if (seasonResult?.Episodes != null && episodeNumber.Value <= seasonResult.Episodes.Count) return seasonResult.Episodes[episodeNumber.Value - 1];
            return null;
        }

        protected static int? ParseChineseSeasonNumberByName(string name)
        {
            var regSeason = new Regex(@"\s第([0-9零一二三四五六七八九]+?)(季|部)", RegexOptions.Compiled);
            var match = regSeason.Match(name);
            if (match.Success && match.Groups.Count > 1)
            {
                var sn = match.Groups[1].Value.ToInt();
                if (sn <= 0) sn = Utils.ChineseNumberToInt(match.Groups[1].Value) ?? 0;
                if (sn > 0) return sn;
            }

            return null;
        }

        private static T? FindFirst<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
        {
            for (var i = 0; i < items.Count; i++) { var item = items[i]; if (predicate(item)) return item; }
            return default;
        }

        private string GetBaseUrl()
        {
            var proxyBaseUrl = Config.DoubanImageProxyBaseUrl;
            if (!string.IsNullOrWhiteSpace(proxyBaseUrl)) return proxyBaseUrl.TrimEnd('/');
            if (MetaSharkPlugin.Instance != null && this.HttpContextAccessor.HttpContext != null) return MetaSharkPlugin.Instance.GetApiBaseUrl(this.HttpContextAccessor.HttpContext.Request).ToString();
            return MetaSharkPlugin.Instance?.GetLocalApiBaseUrl().ToString() ?? string.Empty;
        }
    }
}
