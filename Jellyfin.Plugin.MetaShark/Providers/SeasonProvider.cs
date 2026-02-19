// <copyright file="SeasonProvider.cs" company="PlaceholderCompany">
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
            : base(httpClientFactory, loggerFactory.CreateLogger<SeasonProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(searchInfo);
            this.Log($"GetSeasonSearchResults of [name]: {searchInfo.Name}");
            return await Task.FromResult(Enumerable.Empty<RemoteSearchResult>()).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var result = new MetadataResult<Season>();

            info.SeriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var seriesTmdbId);
            info.SeriesProviderIds.TryGetMetaSource(MetaSharkPlugin.ProviderId, out var metaSource);
            info.SeriesProviderIds.TryGetValue(DoubanProviderId, out var sid);
            var seriesTvdbId = info.SeriesProviderIds.GetValueOrDefault("Tvdb");

            var seasonNumber = info.IndexNumber;
            var seasonSid = info.GetProviderId(DoubanProviderId);
            var fileName = Path.GetFileName(info.Path);
            this.Log($"GetSeasonMetaData of [name]: {info.Name} [fileName]: {fileName} number: {info.IndexNumber} seriesTmdbId: {seriesTmdbId} sid: {sid} metaSource: {metaSource} seriesTvdbId: {seriesTvdbId}");

            if (metaSource != MetaSource.Tmdb && metaSource != MetaSource.Tvdb && !string.IsNullOrEmpty(sid))
            {
                if (seasonNumber is null)
                {
                    seasonNumber = this.GuessSeasonNumberByDirectoryName(info.Path);
                }

                if (string.IsNullOrEmpty(seasonSid))
                {
                    seasonSid = await this.GuessDoubanSeasonId(sid, seriesTmdbId, seasonNumber, info, cancellationToken).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(seasonSid))
                {
                    var subject = await this.DoubanApi.GetMovieAsync(seasonSid, cancellationToken).ConfigureAwait(false);
                    if (subject != null)
                    {
                        subject.Celebrities.Clear();
                        foreach (var celebrity in await this.DoubanApi.GetCelebritiesBySidAsync(seasonSid, cancellationToken).ConfigureAwait(false))
                        {
                            subject.Celebrities.Add(celebrity);
                        }

                        var movie = new Season
                        {
                            ProviderIds = new Dictionary<string, string> { { DoubanProviderId, subject.Sid } },
                            Name = subject.Name,
                            CommunityRating = subject.Rating,
                            Overview = subject.Intro,
                            ProductionYear = subject.Year,
                            Genres = subject.Genres.ToArray(),
                            PremiereDate = subject.ScreenTime,
                            IndexNumber = seasonNumber,
                        };

                        result.Item = movie;
                        result.HasMetadata = true;
                        subject.LimitDirectorCelebrities.Take(Configuration.PluginConfiguration.MAXCASTMEMBERS).ToList().ForEach(c => result.AddPerson(new PersonInfo
                        {
                            Name = c.Name,
                            Type = c.RoleType == PersonType.Director ? PersonKind.Director : PersonKind.Actor,
                            Role = c.Role,
                            ImageUrl = GetLocalProxyImageUrl(new Uri(c.Img, UriKind.Absolute)).ToString(),
                            ProviderIds = new Dictionary<string, string> { { DoubanProviderId, c.Id } },
                        }));

                        this.Log($"Season [{info.Name}] found douban [sid]: {seasonSid}");
                        return result;
                    }
                }
            }

            // TMDB 优先级高于 TVDB 兜底
            if (!string.IsNullOrEmpty(seriesTmdbId) && seasonNumber.HasValue)
            {
                var tmdbResult = await this.GetMetadataByTmdb(info, seriesTmdbId, seasonNumber.Value, cancellationToken).ConfigureAwait(false);
                if (tmdbResult.HasMetadata)
                {
                    return tmdbResult;
                }
            }

            // TVDB 兜底
            if (!string.IsNullOrEmpty(seriesTvdbId) && seasonNumber.HasValue)
            {
                var tvdbResult = await this.GetMetadataByTvdb(info, seriesTvdbId, seasonNumber.Value, cancellationToken).ConfigureAwait(false);
                if (tvdbResult.HasMetadata)
                {
                    return tvdbResult;
                }
            }

            return result;
        }

        public async Task<MetadataResult<Season>> GetMetadataByTvdb(SeasonInfo info, string? seriesTvdbId, int? seasonNumber, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var result = new MetadataResult<Season>();

            if (string.IsNullOrEmpty(seriesTvdbId) || !int.TryParse(seriesTvdbId, out var seriesId) || seasonNumber == null)
            {
                return result;
            }

            // 使用剧集的显示顺序来映射 TVDB 季度类型
            var seasonType = MapDisplayOrderToTvdbType(info.SeriesDisplayOrder);
            this.Log($"GetSeasonMetadata of tvdb seriesId: \"{seriesTvdbId}\" season: {seasonNumber} type: {seasonType}");

            var episodes = await this.TvdbApi.GetSeriesEpisodesAsync(seriesId, seasonType, seasonNumber.Value, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);

            if (episodes.Any())
            {
                result.Item = new Season
                {
                    Name = $"第 {seasonNumber} 季",
                    IndexNumber = seasonNumber,
                };
                result.HasMetadata = true;
            }

            return result;
        }

        public async Task<string?> GuessDoubanSeasonId(string? sid, string? seriesTmdbId, int? seasonNumber, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (string.IsNullOrEmpty(sid))
            {
                return null;
            }

            if (string.IsNullOrEmpty(info.Path) && !seasonNumber.HasValue)
            {
                return null;
            }

            var fileName = GetOriginalFileName(info);
            var doubanId = this.RegDoubanIdAttribute.FirstMatchGroup(fileName);
            if (!string.IsNullOrWhiteSpace(doubanId))
            {
                this.Log($"Found season douban [id] by attr: {doubanId}");
                return doubanId;
            }

            var series = await this.DoubanApi.GetMovieAsync(sid, cancellationToken).ConfigureAwait(false);
            if (series == null)
            {
                return null;
            }

            var seriesName = this.RemoveSeasonSuffix(series.Name);

            var seasonYear = 0;
            if (!string.IsNullOrEmpty(seriesTmdbId) && (seasonNumber.HasValue && seasonNumber > 0))
            {
                var season = await this.TmdbApi
                    .GetSeasonAsync(seriesTmdbId.ToInt(), seasonNumber.Value, info.MetadataLanguage, info.MetadataLanguage, cancellationToken)
                    .ConfigureAwait(false);
                seasonYear = season?.AirDate?.Year ?? 0;
            }

            if (!string.IsNullOrEmpty(seriesName) && seasonYear > 0)
            {
                var seasonSid = await this.GuestDoubanSeasonByYearAsync(seriesName, seasonYear, seasonNumber, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(seasonSid))
                {
                    return seasonSid;
                }
            }

            if (!string.IsNullOrEmpty(seriesName) && seasonNumber.HasValue && seasonNumber > 0)
            {
                return await this.GuestDoubanSeasonBySeasonNameAsync(seriesName, seasonNumber, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        public async Task<MetadataResult<Season>> GetMetadataByTmdb(SeasonInfo info, string? seriesTmdbId, int? seasonNumber, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var result = new MetadataResult<Season>();

            if (string.IsNullOrEmpty(seriesTmdbId))
            {
                return result;
            }

            if (seasonNumber is null or 0)
            {
                return result;
            }

            if (TmdbEpisodeGroupMapping.TryGetGroupId(Config.TmdbEpisodeGroupMap, seriesTmdbId, out var groupId))
            {
                this.Log("TMDb episode group mapping hit (season): seriesId={0} groupId={1} season={2}", seriesTmdbId, groupId, seasonNumber);
                var group = await this.TmdbApi
                    .GetEpisodeGroupByIdAsync(groupId, info.MetadataLanguage, cancellationToken)
                    .ConfigureAwait(false);
                var seasonGroup = group?.Groups.FirstOrDefault(g => g.Order == seasonNumber);
                if (seasonGroup != null)
                {
                    this.Log("TMDb episode group mapping resolved (season): seriesId={0} groupId={1} season={2} name={3}", seriesTmdbId, groupId, seasonNumber, seasonGroup.Name);
                    result.Item = new Season
                    {
                        Name = seasonGroup.Name,
                        IndexNumber = seasonNumber,
                    };
                    result.HasMetadata = true;
                    return result;
                }
            }

            var seasonResult = await this.TmdbApi
                .GetSeasonAsync(seriesTmdbId.ToInt(), seasonNumber ?? 0, info.MetadataLanguage, info.MetadataLanguage, cancellationToken)
                .ConfigureAwait(false);
            if (seasonResult == null)
            {
                this.Log($"Not found season from TMDB. {info.Name} seriesTmdbId: {seriesTmdbId} seasonNumber: {seasonNumber}");
                return result;
            }

            result.HasMetadata = true;
            result.Item = new Season
            {
                Name = seasonResult.Name,
                IndexNumber = seasonNumber,
                Overview = seasonResult.Overview,
                PremiereDate = seasonResult.AirDate,
                ProductionYear = seasonResult.AirDate?.Year,
            };

            if (!string.IsNullOrEmpty(seasonResult.ExternalIds?.TvdbId))
            {
                result.Item.SetProviderId("Tvdb", seasonResult.ExternalIds.TvdbId);
            }

            return result;
        }
    }
}
