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
        public EpisodeImageProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<EpisodeImageProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Episode;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            this.Log($"GetEpisodeImages of [name]: {item.Name} number: {item.IndexNumber} ParentIndexNumber: {item.ParentIndexNumber}");

            var episode = (MediaBrowser.Controller.Entities.TV.Episode)item;
            var series = episode.Series;

            var seriesTmdbId = Convert.ToInt32(series.GetProviderId(MetadataProvider.Tmdb), CultureInfo.InvariantCulture);

            if (seriesTmdbId <= 0)
            {
                this.Log($"[GetEpisodeImages] The seriesTmdbId is empty!");
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var seasonNumber = episode.ParentIndexNumber;
            var episodeNumber = episode.IndexNumber;

            if (seasonNumber is null || episodeNumber is null or 0)
            {
                this.Log($"[GetEpisodeImages] The seasonNumber or episodeNumber is empty! seasonNumber: {seasonNumber} episodeNumber: {episodeNumber}");
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var language = item.GetPreferredMetadataLanguage();
            var displayOrder = series.DisplayOrder ?? string.Empty;

            var episodeResult = await this.GetEpisodeAsync(seriesTmdbId, seasonNumber, episodeNumber, displayOrder, language, language, cancellationToken)
                .ConfigureAwait(false);
            if (episodeResult == null)
            {
                this.Log("GetEpisodeImages] 找不到tmdb剧集数据. seriesTmdbId: {0} seasonNumber: {1} episodeNumber: {2} displayOrder: {3}", seriesTmdbId, seasonNumber, episodeNumber, displayOrder);
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var result = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(episodeResult.StillPath))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = this.TmdbApi.GetStillUrl(episodeResult.StillPath)?.ToString(),
                    CommunityRating = episodeResult.VoteAverage,
                    VoteCount = episodeResult.VoteCount,
                    ProviderName = this.Name,
                    Type = ImageType.Primary,
                });
            }

            return result;
        }
    }
}
