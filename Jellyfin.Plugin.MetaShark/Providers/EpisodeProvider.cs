// <copyright file="EpisodeProvider.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
        private static readonly Action<ILogger, string, int, int, string, Exception?> LogTvdbPlacementLookup =
            LoggerMessage.Define<string, int, int, string>(LogLevel.Debug, new EventId(2, nameof(LogTvdbPlacementLookup)), "TVDB special placement lookup. tvdbId={TvdbId} s{Season}e{Episode} lang={Lang}");

        private static readonly Action<ILogger, string, int, int, Exception?> LogTvdbPlacementNotFound =
            LoggerMessage.Define<string, int, int>(LogLevel.Debug, new EventId(3, nameof(LogTvdbPlacementNotFound)), "TVDB special placement not found. tvdbId={TvdbId} s{Season}e{Episode}");

        private static readonly Action<ILogger, string, int, Exception?> LogTvdbPlacementInvalidInput =
            LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(4, nameof(LogTvdbPlacementInvalidInput)), "TVDB placement invalid input. tvdbId={TvdbId} episode={Episode}");

        private static readonly Action<ILogger, string, Exception?> LogTvdbPlacementEmptyList =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, nameof(LogTvdbPlacementEmptyList)), "TVDB placement empty episode list. tvdbId={TvdbId}");

        private static readonly Action<ILogger, string, int, Exception?> LogTvdbPlacementNoMatch =
            LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(6, nameof(LogTvdbPlacementNoMatch)), "TVDB placement no match. tvdbId={TvdbId} episode={Episode}");

        private static readonly Action<ILogger, string, Exception?> LogTvdbIdMissing =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(7, nameof(LogTvdbIdMissing)), "TVDB id not found for series. source={Source}");

        private static readonly Action<ILogger, string, Exception?> LogTvdbIdResolved =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(8, nameof(LogTvdbIdResolved)), "TVDB id resolved: {TvdbId}");

        private readonly MemoryCache memoryCache;

        public EpisodeProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<EpisodeProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
            this.memoryCache = new MemoryCache(new MemoryCacheOptions());
        }

        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(searchInfo);
            this.Log($"GetEpisodeSearchResults of [name]: {searchInfo.Name}");
            return await Task.FromResult(Enumerable.Empty<RemoteSearchResult>()).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);

            var fileName = Path.GetFileName(info.Path);
            this.Log($"GetEpisodeMetadata of [name]: {info.Name} [fileName]: {fileName} number: {info.IndexNumber} ParentIndexNumber: {info.ParentIndexNumber} IsMissingEpisode: {info.IsMissingEpisode} EnableTmdb: {Config.EnableTmdb} DisplayOrder: {info.SeriesDisplayOrder}");
            var result = new MetadataResult<Episode>();

            if (info.IsMissingEpisode)
            {
                return result;
            }

            var specialResult = this.HandleAnimeExtras(info);
            if (specialResult != null)
            {
                return specialResult;
            }

            info = this.FixParseInfo(info);

            info.SeriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var seriesTmdbId);
            var seriesTvdbId = info.SeriesProviderIds.GetValueOrDefault("Tvdb");
            var seasonNumber = info.ParentIndexNumber;
            var episodeNumber = info.IndexNumber;
            result.HasMetadata = true;
            result.Item = new Episode
            {
                ParentIndexNumber = seasonNumber,
                IndexNumber = episodeNumber,
                Name = info.Name,
            };

            if (episodeNumber is null or 0 || seasonNumber is null || (string.IsNullOrEmpty(seriesTmdbId) && string.IsNullOrEmpty(seriesTvdbId)))
            {
                this.Log("缺少元数据. episodeNumber: {0} seasonNumber: {1} seriesTmdbId:{2} seriesTvdbId:{3}", episodeNumber, seasonNumber, seriesTmdbId, seriesTvdbId);
                return result;
            }

            // 1. 尝试从 TMDB 获取元数据
            if (!string.IsNullOrEmpty(seriesTmdbId))
            {
                var episodeResult = await this.GetEpisodeAsync(
                        seriesTmdbId.ToInt(),
                        seasonNumber,
                        episodeNumber,
                        info.SeriesDisplayOrder,
                        info.MetadataLanguage,
                        info.MetadataLanguage,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (episodeResult != null)
                {
                    result.HasMetadata = true;
                    result.QueriedById = true;

                    if (!string.IsNullOrEmpty(episodeResult.Overview))
                    {
                        result.ResultLanguage = info.MetadataLanguage;
                    }

                    var item = new Episode
                    {
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = seasonNumber,
                        PremiereDate = episodeResult.AirDate,
                        ProductionYear = episodeResult.AirDate?.Year,
                        Name = episodeResult.Name,
                        Overview = episodeResult.Overview,
                        CommunityRating = (float)System.Math.Round(episodeResult.VoteAverage, 1),
                    };

                    result.Item = item;
                }
            }

            // 2. 如果 TMDB 没抓到，尝试从 TVDB 获取元数据 (Fallback)
            if ((result.Item == null || string.IsNullOrEmpty(result.Item.Name)) && !string.IsNullOrEmpty(seriesTvdbId) && int.TryParse(seriesTvdbId, out var tvdbIdFallback))
            {
                var seasonType = MapDisplayOrderToTvdbType(info.SeriesDisplayOrder);
                var tvdbEpisodes = await this.TvdbApi.GetSeriesEpisodesAsync(tvdbIdFallback, seasonType, seasonNumber.Value, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                var match = tvdbEpisodes.FirstOrDefault(e => e.SeasonNumber == seasonNumber && e.Number == episodeNumber);
                if (match != null)
                {
                    var item = result.Item ?? new Episode { IndexNumber = episodeNumber, ParentIndexNumber = seasonNumber };
                    item.Name = match.Name ?? item.Name ?? $"第 {episodeNumber} 集";
                    item.Overview = match.Overview ?? item.Overview;
                    item.PremiereDate = match.Aired ?? item.PremiereDate;
                    item.ProductionYear = match.Aired?.Year ?? item.ProductionYear;

                    result.Item = item;
                    result.HasMetadata = true;
                }
            }

            // 3. 处理 TVDB 特典插入逻辑 (Season 0 置入正片季)
            if (result.Item != null && seasonNumber == 0 && Config.EnableTvdbSpecialsWithinSeasons)
            {
                var resolvedTvdbId = await this.ResolveSeriesTvdbIdAsync(info, seriesTmdbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolvedTvdbId))
                {
                    LogTvdbPlacementLookup(this.Logger, resolvedTvdbId, seasonNumber.Value, episodeNumber.Value, info.MetadataLanguage ?? string.Empty, null);
                    var placement = await this.TryBuildTvdbSpecialPlacementAsync(resolvedTvdbId, episodeNumber, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                    if (placement != null)
                    {
                        result.Item.AirsBeforeSeasonNumber = placement.AirsBeforeSeasonNumber;
                        result.Item.AirsBeforeEpisodeNumber = placement.AirsBeforeEpisodeNumber;
                        result.Item.AirsAfterSeasonNumber = placement.AirsAfterSeasonNumber;
                        this.Log("TVDB special placement result. tvdbId: {0} s{1}e{2} -> beforeSeason: {3} beforeEpisode: {4} afterSeason: {5}",
                            resolvedTvdbId, seasonNumber, episodeNumber, result.Item.AirsBeforeSeasonNumber, result.Item.AirsBeforeEpisodeNumber, result.Item.AirsAfterSeasonNumber);
                    }
                    else
                    {
                        LogTvdbPlacementNotFound(this.Logger, resolvedTvdbId, seasonNumber.Value, episodeNumber.Value, null);
                    }
                }
            }

            return result;
        }

        public EpisodeInfo FixParseInfo(EpisodeInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);

            var fileName = Path.GetFileNameWithoutExtension(info.Path) ?? info.Name;
            if (string.IsNullOrEmpty(fileName))
            {
                return info;
            }

            var parseResult = NameParser.ParseEpisode(fileName);
            info.Year = parseResult.Year;
            info.Name = parseResult.ChineseName ?? parseResult.Name;

            if (parseResult.ParentIndexNumber.HasValue && parseResult.ParentIndexNumber > 0 && info.ParentIndexNumber != parseResult.ParentIndexNumber)
            {
                this.Log("FixSeasonNumber by anitomy. old: {0} new: {1}", info.ParentIndexNumber, parseResult.ParentIndexNumber);
                info.ParentIndexNumber = parseResult.ParentIndexNumber;
            }

            var isVirtualSeason = this.IsVirtualSeason(info);
            var seasonFolderPath = this.GetOriginalSeasonPath(info);
            if (info.ParentIndexNumber is null or 1 && isVirtualSeason)
            {
                if (seasonFolderPath != null)
                {
                    var guestSeasonNumber = this.GuessSeasonNumberByDirectoryName(seasonFolderPath);
                    if (guestSeasonNumber.HasValue && guestSeasonNumber != info.ParentIndexNumber)
                    {
                        this.Log("FixSeasonNumber by season path. old: {0} new: {1}", info.ParentIndexNumber, guestSeasonNumber);
                        info.ParentIndexNumber = guestSeasonNumber;
                    }
                }
                else
                {
                    this.Log("FixSeasonNumber by virtual season. old: {0} new: {1}", info.ParentIndexNumber, 1);
                    info.ParentIndexNumber = 1;
                }
            }

            if (info.ParentIndexNumber is null && !isVirtualSeason && !string.IsNullOrEmpty(seasonFolderPath))
            {
                var guestSeasonNumber = this.LibraryManager.GetSeasonNumberFromPath(seasonFolderPath);
                if (!guestSeasonNumber.HasValue)
                {
                    guestSeasonNumber = this.GuessSeasonNumberByDirectoryName(seasonFolderPath);
                }

                if (guestSeasonNumber.HasValue && guestSeasonNumber != info.ParentIndexNumber)
                {
                    this.Log("FixSeasonNumber by season path. old: {0} new: {1}", info.ParentIndexNumber, guestSeasonNumber);
                    info.ParentIndexNumber = guestSeasonNumber;
                }
            }

            if (info.ParentIndexNumber is null && NameParser.IsAnime(fileName) && (parseResult.IsSpecial || NameParser.IsSpecialDirectory(info.Path)))
            {
                this.Log("FixSeasonNumber to special. old: {0} new: 0", info.ParentIndexNumber);
                info.ParentIndexNumber = 0;
            }

            if (info.ParentIndexNumber.HasValue && info.ParentIndexNumber == 0)
            {
                info.Name = parseResult.SpecialName == info.Name ? fileName : parseResult.SpecialName;
            }

            if (parseResult.IndexNumber.HasValue && info.IndexNumber != parseResult.IndexNumber)
            {
                this.Log("FixEpisodeNumber by anitomy. old: {0} new: {1}", info.IndexNumber, parseResult.IndexNumber);
                info.IndexNumber = parseResult.IndexNumber;
            }

            return info;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.memoryCache.Dispose();
            }
        }

        protected int GetVideoFileCount(string? dir)
        {
            if (dir == null)
            {
                return 0;
            }

            var cacheKey = $"filecount_{dir}";
            if (this.memoryCache.TryGetValue<int>(cacheKey, out var videoFilesCount))
            {
                return videoFilesCount;
            }

            var dirInfo = new DirectoryInfo(dir);

            var files = dirInfo.GetFiles();
            var nameOptions = new Emby.Naming.Common.NamingOptions();

            foreach (var fileInfo in files.Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden)))
            {
                if (Emby.Naming.Video.VideoResolver.IsVideoFile(fileInfo.FullName, nameOptions))
                {
                    videoFilesCount++;
                }
            }

            var expiredOption = new MemoryCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };
            this.memoryCache.Set<int>(cacheKey, videoFilesCount, expiredOption);
            return videoFilesCount;
        }

        private async Task<TvdbSpecialPlacement?> TryBuildTvdbSpecialPlacementAsync(
            string seriesTvdbId,
            int? episodeNumber,
            string? metadataLanguage,
            CancellationToken cancellationToken)
        {
            if (!int.TryParse(seriesTvdbId, out var seriesId) || episodeNumber is null or 0)
            {
                LogTvdbPlacementInvalidInput(this.Logger, seriesTvdbId, episodeNumber ?? 0, null);
                return null;
            }

            // 特典插入逻辑强制请求官方顺序的分组
            var episodes = await this.TvdbApi
                .GetSeriesEpisodesAsync(seriesId, "official", 0, metadataLanguage, cancellationToken)
                .ConfigureAwait(false);
            if (episodes.Count == 0)
            {
                LogTvdbPlacementEmptyList(this.Logger, seriesTvdbId, null);
                return null;
            }

            var match = episodes.FirstOrDefault(e => e.SeasonNumber == 0 && e.Number == episodeNumber);
            if (match == null)
            {
                LogTvdbPlacementNoMatch(this.Logger, seriesTvdbId, episodeNumber ?? 0, null);
                return null;
            }

            return new TvdbSpecialPlacement
            {
                AirsBeforeSeasonNumber = match.AirsBeforeSeason,
                AirsBeforeEpisodeNumber = match.AirsBeforeEpisode,
                AirsAfterSeasonNumber = match.AirsAfterSeason,
            };
        }

        private async Task<string?> ResolveSeriesTvdbIdAsync(EpisodeInfo info, string? seriesTmdbId, CancellationToken cancellationToken)
        {
            if (info.SeriesProviderIds.TryGetValue("Tvdb", out var seriesTvdbId)
                && !string.IsNullOrWhiteSpace(seriesTvdbId))
            {
                LogTvdbIdResolved(this.Logger, seriesTvdbId, null);
                return seriesTvdbId;
            }

            var episodeItem = this.LibraryManager.FindByPath(info.Path, false) as Episode;
            var seriesItem = episodeItem?.Series;
            seriesTvdbId = seriesItem?.GetProviderId("Tvdb");
            if (!string.IsNullOrWhiteSpace(seriesTvdbId))
            {
                info.SeriesProviderIds["Tvdb"] = seriesTvdbId;
                LogTvdbIdResolved(this.Logger, seriesTvdbId, null);
                return seriesTvdbId;
            }

            if (!string.IsNullOrWhiteSpace(seriesTmdbId) && int.TryParse(seriesTmdbId, out var tmdbId))
            {
                var series = await this.TmdbApi
                    .GetSeriesAsync(tmdbId, info.MetadataLanguage ?? string.Empty, info.MetadataLanguage ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                seriesTvdbId = series?.ExternalIds?.TvdbId;
                if (!string.IsNullOrWhiteSpace(seriesTvdbId))
                {
                    info.SeriesProviderIds["Tvdb"] = seriesTvdbId;
                    LogTvdbIdResolved(this.Logger, seriesTvdbId, null);
                    return seriesTvdbId;
                }
            }

            LogTvdbIdMissing(this.Logger, "EpisodeInfo/SeriesItem/TmdbExternalIds", null);
            return null;
        }

        private MetadataResult<Episode>? HandleAnimeExtras(EpisodeInfo info)
        {
            var fileName = Path.GetFileNameWithoutExtension(info.Path) ?? info.Name;
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            var parseResult = NameParser.ParseEpisode(fileName);
            if (parseResult.IsExtra)
            {
                this.Log($"Found anime extra of [name]: {fileName}");
                var result = new MetadataResult<Episode>();
                result.HasMetadata = true;

                if (info.ParentIndexNumber.HasValue)
                {
                    result.Item = new Episode
                    {
                        ParentIndexNumber = 0,
                        IndexNumber = null,
                        Name = parseResult.ExtraName,
                    };
                    return result;
                }

                result.Item = new Episode
                {
                    Name = parseResult.ExtraName,
                };
                return result;
            }

            return null;
        }

        private sealed class TvdbSpecialPlacement
        {
            public int? AirsBeforeSeasonNumber { get; set; }

            public int? AirsBeforeEpisodeNumber { get; set; }

            public int? AirsAfterSeasonNumber { get; set; }
        }
    }
}
