namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Enums;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Core;
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    public class SeasonProvider : BaseProvider, IRemoteMetadataProvider<Season, SeasonInfo>
    {
        public SeasonProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<SeasonProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi) { }
        public string Name => MetaSharkPlugin.PluginName;
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken) => await Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();
            info.SeriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var seriesTmdbId);
            var metaSource = info.SeriesProviderIds.GetMetaSource(MetaSharkPlugin.ProviderId);
            info.SeriesProviderIds.TryGetValue(DoubanProviderId, out var sid);
            var seriesTvdbId = info.SeriesProviderIds.GetValueOrDefault("Tvdb");
            var seasonNumber = info.IndexNumber;
            if (metaSource != MetaSource.Tmdb && metaSource != MetaSource.Tvdb && !string.IsNullOrEmpty(sid)) {
                if (seasonNumber is null) seasonNumber = this.GuessSeasonNumberByDirectoryName(info.Path);
                var seasonSid = info.GetProviderId(DoubanProviderId);
                if (string.IsNullOrEmpty(seasonSid)) seasonSid = await this.GuessDoubanSeasonId(sid, seriesTmdbId, seasonNumber, info, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(seasonSid)) {
                    var subject = await this.DoubanApi.GetMovieAsync(seasonSid, cancellationToken).ConfigureAwait(false);
                    if (subject != null) {
                        result.Item = new Season { ProviderIds = new Dictionary<string, string> { { DoubanProviderId, subject.Sid } }, Name = subject.Name, CommunityRating = subject.Rating, Overview = subject.Intro, ProductionYear = subject.Year, Genres = subject.Genres.ToArray(), PremiereDate = subject.ScreenTime, IndexNumber = seasonNumber };
                        result.HasMetadata = true;
                        return result;
                    }
                }
            }
            if (!string.IsNullOrEmpty(seriesTmdbId) && seasonNumber.HasValue) {
                var tmdbResult = await this.GetMetadataByTmdb(info, seriesTmdbId, seasonNumber.Value, cancellationToken).ConfigureAwait(false);
                if (tmdbResult.HasMetadata) return tmdbResult;
            }
            if (!string.IsNullOrEmpty(seriesTvdbId) && seasonNumber.HasValue) {
                var tvdbResult = await this.GetMetadataByTvdb(info, seriesTvdbId, seasonNumber.Value, cancellationToken).ConfigureAwait(false);
                if (tvdbResult.HasMetadata) return tvdbResult;
            }
            return result;
        }
        public async Task<MetadataResult<Season>> GetMetadataByTvdb(SeasonInfo info, string? seriesTvdbId, int? seasonNumber, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();
            if (string.IsNullOrEmpty(seriesTvdbId) || !int.TryParse(seriesTvdbId, out var id) || seasonNumber == null) return result;
            var tvdbSeries = await this.TvdbApi.GetSeriesAsync(id, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (tvdbSeries != null) {
                var seasonType = MapDisplayOrderToTvdbType(info.SeriesDisplayOrder);
                var tvdbSeason = tvdbSeries.Seasons?.FirstOrDefault(s => s.Number == seasonNumber.Value && s.Type?.Type == seasonType);
                result.Item = new Season { Name = $"第 {seasonNumber} 季", IndexNumber = seasonNumber };
                if (tvdbSeason != null) result.Item.SetProviderId("Tvdb", tvdbSeason.Id.ToString(CultureInfo.InvariantCulture));
                result.HasMetadata = true;
            }
            return result;
        }
        public async Task<string?> GuessDoubanSeasonId(string? sid, string? seriesTmdbId, int? seasonNumber, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(sid) || (!seasonNumber.HasValue && string.IsNullOrEmpty(info.Path))) return null;
            var series = await this.DoubanApi.GetMovieAsync(sid, cancellationToken).ConfigureAwait(false);
            if (series == null) return null;
            var seriesName = this.RemoveSeasonSuffix(series.Name);
            var seasonYear = 0;
            if (!string.IsNullOrEmpty(seriesTmdbId) && seasonNumber > 0) {
                var season = await this.TmdbApi.GetSeasonAsync(seriesTmdbId.ToInt(), seasonNumber.Value, info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                seasonYear = season?.AirDate?.Year ?? 0;
            }
            if (!string.IsNullOrEmpty(seriesName) && seasonYear > 0) {
                var ssid = await this.GuestDoubanSeasonByYearAsync(seriesName, seasonYear, seasonNumber, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(ssid)) return ssid;
            }
            if (!string.IsNullOrEmpty(seriesName) && seasonNumber > 0) return await this.GuestDoubanSeasonBySeasonNameAsync(seriesName, seasonNumber, cancellationToken).ConfigureAwait(false);
            return null;
        }
        public async Task<MetadataResult<Season>> GetMetadataByTmdb(SeasonInfo info, string? seriesTmdbId, int? seasonNumber, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();
            if (string.IsNullOrEmpty(seriesTmdbId) || seasonNumber is null or 0) return result;
            var seasonResult = await this.TmdbApi.GetSeasonAsync(seriesTmdbId.ToInt(), seasonNumber.Value, info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (seasonResult == null) return result;
            result.Item = new Season { Name = seasonResult.Name, IndexNumber = seasonNumber, Overview = seasonResult.Overview, PremiereDate = seasonResult.AirDate, ProductionYear = seasonResult.AirDate?.Year };
            result.HasMetadata = true;
            if (!string.IsNullOrEmpty(seasonResult.ExternalIds?.TvdbId)) result.Item.SetProviderId("Tvdb", seasonResult.ExternalIds.TvdbId);
            return result;
        }
    }
}
