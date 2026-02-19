// <copyright file="EpisodeImageProvider.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Core;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public class EpisodeImageProvider : BaseProvider, IRemoteImageProvider
    {
        public EpisodeImageProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<EpisodeImageProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Episode;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>
        {
            ImageType.Primary,
        };

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            var episode = (Episode)item;
            var series = episode.Series;
            if (series == null)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var seriesTmdbId = series.GetProviderId(MetadataProvider.Tmdb);
            var seriesTvdbId = series.GetProviderId("Tvdb");
            var seasonNumber = episode.ParentIndexNumber;
            var episodeNumber = episode.IndexNumber;
            var language = item.GetPreferredMetadataLanguage();

            var res = new List<RemoteImageInfo>();

            this.Log($"GetEpisodeImages of [name]: {item.Name} number: {episodeNumber} ParentIndexNumber: {seasonNumber}");

            if (!seasonNumber.HasValue || !episodeNumber.HasValue)
            {
                return res;
            }

            // 1. 从 TMDB 获取单集剧照
            if (Config.EnableTmdb && !string.IsNullOrEmpty(seriesTmdbId))
            {
                try
                {
                    var displayOrder = series.DisplayOrder ?? string.Empty;
                    var episodeResult = await this.GetEpisodeAsync(seriesTmdbId.ToInt(), seasonNumber, episodeNumber, displayOrder, language, language, cancellationToken).ConfigureAwait(false);
                    if (episodeResult != null && !string.IsNullOrEmpty(episodeResult.StillPath))
                    {
                        res.Add(new RemoteImageInfo
                        {
                            Url = this.TmdbApi.GetStillUrl(episodeResult.StillPath)?.ToString(),
                            CommunityRating = episodeResult.VoteAverage,
                            VoteCount = episodeResult.VoteCount,
                            ProviderName = this.Name + " (TMDB)",
                            Type = ImageType.Primary,
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.Log("Error fetching TMDB episode images: {0}", ex.Message);
                }
            }

            // 2. 从 TVDB 获取单集剧照
            if (Config.EnableTvdb && !string.IsNullOrEmpty(seriesTvdbId))
            {
                try
                {
                    if (int.TryParse(seriesTvdbId, out var id))
                    {
                        // 使用剧集的显示顺序来匹配 TVDB 季度类型
                        var seasonType = MapDisplayOrderToTvdbType(series.DisplayOrder);
                        this.Log("TVDB Images using seasonType: {0} for series: {1}", seasonType, seriesTvdbId);

                        var tvdbEpisodes = await this.TvdbApi.GetSeriesEpisodesAsync(id, seasonType, seasonNumber.Value, language, cancellationToken).ConfigureAwait(false);
                        var match = tvdbEpisodes.FirstOrDefault(e => e.SeasonNumber == seasonNumber && e.Number == episodeNumber);
                        if (match != null && !string.IsNullOrEmpty(match.Image))
                        {
                            res.Add(new RemoteImageInfo
                            {
                                ProviderName = this.Name + " (TVDB)",
                                Url = match.Image,
                                Type = ImageType.Primary,
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Log("Error fetching TVDB episode images: {0}", ex.Message);
                }
            }

            return res;
        }
    }
}
