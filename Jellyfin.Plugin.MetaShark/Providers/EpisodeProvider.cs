namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Core;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Logging;
    public class EpisodeProvider : BaseProvider, IRemoteMetadataProvider<Episode, EpisodeInfo>, IDisposable
    {
        private readonly MemoryCache memoryCache;
        public EpisodeProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<EpisodeProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi) { this.memoryCache = new MemoryCache(new MemoryCacheOptions()); }
        public string Name => MetaSharkPlugin.PluginName;
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken) => await Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            if (info.IsMissingEpisode) return new MetadataResult<Episode>();
            var specialResult = this.HandleAnimeExtras(info); if (specialResult != null) return specialResult;
            info = this.FixParseInfo(info);
            info.SeriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var seriesTmdbId);
            var seriesTvdbId = info.SeriesProviderIds.GetValueOrDefault("Tvdb");
            var seasonNumber = info.ParentIndexNumber; var episodeNumber = info.IndexNumber;
            var result = new MetadataResult<Episode> { Item = new Episode { ParentIndexNumber = seasonNumber, IndexNumber = episodeNumber, Name = info.Name }, HasMetadata = true };
            if (episodeNumber is null or 0 || seasonNumber is null || (string.IsNullOrEmpty(seriesTmdbId) && string.IsNullOrEmpty(seriesTvdbId))) return result;
            if (!string.IsNullOrEmpty(seriesTmdbId)) {
                var er = await this.GetEpisodeAsync(seriesTmdbId.ToInt(), seasonNumber, episodeNumber, info.SeriesDisplayOrder, info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                if (er != null) {
                    result.QueriedById = true;
                    if (!string.IsNullOrEmpty(er.Overview)) result.ResultLanguage = info.MetadataLanguage;
                    result.Item.PremiereDate = er.AirDate; result.Item.ProductionYear = er.AirDate?.Year; result.Item.Name = er.Name; result.Item.Overview = er.Overview; result.Item.CommunityRating = (float)Math.Round(er.VoteAverage, 1);
                }
            }
            if ((string.IsNullOrEmpty(result.Item.Name) || string.IsNullOrEmpty(result.Item.Overview)) && !string.IsNullOrEmpty(seriesTvdbId) && int.TryParse(seriesTvdbId, out var tvdbIdFallback)) {
                var seasonType = MapDisplayOrderToTvdbType(info.SeriesDisplayOrder);
                var tvdbEpisodes = await this.TvdbApi.GetSeriesEpisodesAsync(tvdbIdFallback, seasonType, seasonNumber.Value, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                var match = tvdbEpisodes.FirstOrDefault(e => e.SeasonNumber == seasonNumber && e.Number == episodeNumber);
                if (match != null) {
                    result.Item.Name = match.Name ?? result.Item.Name; result.Item.Overview = match.Overview ?? result.Item.Overview; result.Item.PremiereDate = match.Aired ?? result.Item.PremiereDate; result.Item.ProductionYear = match.Aired?.Year ?? result.Item.ProductionYear;
                }
            }
            if (seasonNumber == 0 && Config.EnableTvdbSpecialsWithinSeasons) {
                var rTvdbId = await this.ResolveSeriesTvdbIdAsync(info, seriesTmdbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(rTvdbId)) {
                    var placement = await this.TryBuildTvdbSpecialPlacementAsync(rTvdbId, episodeNumber, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                    if (placement != null) {
                        result.Item.AirsBeforeSeasonNumber = placement.AirsBeforeSeasonNumber; result.Item.AirsBeforeEpisodeNumber = placement.AirsBeforeEpisodeNumber; result.Item.AirsAfterSeasonNumber = placement.AirsAfterSeasonNumber;
                    }
                }
            }
            return result;
        }
        public EpisodeInfo FixParseInfo(EpisodeInfo info) {
            var fileName = Path.GetFileNameWithoutExtension(info.Path) ?? info.Name; if (string.IsNullOrEmpty(fileName)) return info;
            var pr = NameParser.ParseEpisode(fileName); info.Year = pr.Year; info.Name = pr.ChineseName ?? pr.Name;
            if (pr.ParentIndexNumber.HasValue && pr.ParentIndexNumber > 0 && info.ParentIndexNumber != pr.ParentIndexNumber) info.ParentIndexNumber = pr.ParentIndexNumber;
            if (pr.IndexNumber.HasValue && info.IndexNumber != pr.IndexNumber) info.IndexNumber = pr.IndexNumber;
            return info;
        }
        public void Dispose() { this.memoryCache.Dispose(); GC.SuppressFinalize(this); }
        private async Task<TvdbSpecialPlacement?> TryBuildTvdbSpecialPlacementAsync(string seriesTvdbId, int? episodeNumber, string? metadataLanguage, CancellationToken cancellationToken) {
            if (!int.TryParse(seriesTvdbId, out var seriesId) || episodeNumber is null or 0) return null;
            var episodes = await this.TvdbApi.GetSeriesEpisodesAsync(seriesId, "official", 0, metadataLanguage, cancellationToken).ConfigureAwait(false);
            var match = episodes.FirstOrDefault(e => e.SeasonNumber == 0 && e.Number == episodeNumber);
            if (match == null) return null;
            return new TvdbSpecialPlacement { AirsBeforeSeasonNumber = match.AirsBeforeSeason, AirsBeforeEpisodeNumber = match.AirsBeforeEpisode, AirsAfterSeasonNumber = match.AirsAfterSeason };
        }
        private async Task<string?> ResolveSeriesTvdbIdAsync(EpisodeInfo info, string? seriesTmdbId, CancellationToken cancellationToken) {
            if (info.SeriesProviderIds.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrWhiteSpace(tvdbId)) return tvdbId;
            if (!string.IsNullOrWhiteSpace(seriesTmdbId) && int.TryParse(seriesTmdbId, out var tmdbId)) {
                var series = await this.TmdbApi.GetSeriesAsync(tmdbId, info.MetadataLanguage ?? string.Empty, info.MetadataLanguage ?? string.Empty, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(series?.ExternalIds?.TvdbId)) { info.SeriesProviderIds["Tvdb"] = series.ExternalIds.TvdbId; return series.ExternalIds.TvdbId; }
            }
            return null;
        }
        private MetadataResult<Episode>? HandleAnimeExtras(EpisodeInfo info) {
            var fileName = Path.GetFileNameWithoutExtension(info.Path) ?? info.Name; if (string.IsNullOrEmpty(fileName)) return null;
            var pr = NameParser.ParseEpisode(fileName);
            if (pr.IsExtra) {
                var res = new MetadataResult<Episode> { Item = new Episode { ParentIndexNumber = 0, IndexNumber = null, Name = pr.ExtraName }, HasMetadata = true };
                return res;
            }
            return null;
        }
        private sealed class TvdbSpecialPlacement { public int? AirsBeforeSeasonNumber { get; set; } public int? AirsBeforeEpisodeNumber { get; set; } public int? AirsAfterSeasonNumber { get; set; } }
    }
}
