// <copyright file="SeasonImageProvider.cs" company="PlaceholderCompany">
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
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public class SeasonImageProvider : BaseProvider, IRemoteImageProvider
    {
        public SeasonImageProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<SeasonImageProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Season;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>
        {
            ImageType.Primary,
        };

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            var season = (Season)item;
            var series = season.Series;
            if (series == null)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var sid = item.GetProviderId(DoubanProviderId);
            var seriesTmdbId = series.GetProviderId(MetadataProvider.Tmdb);
            var seriesTvdbId = series.GetProviderId("Tvdb");
            var metaSource = series.GetMetaSource(MetaSharkPlugin.ProviderId);
            var language = item.GetPreferredMetadataLanguage();
            var seasonNumber = item.IndexNumber;

            var res = new List<RemoteImageInfo>();

            this.Log($"GetImages for season: {item.Name} number: {seasonNumber} [metaSource]: {metaSource}");

            // 1. 获取豆瓣季度海报
            if (!string.IsNullOrEmpty(sid))
            {
                var primary = await this.DoubanApi.GetMovieAsync(sid, cancellationToken).ConfigureAwait(false);
                if (primary != null && !string.IsNullOrEmpty(primary.Img))
                {
                    res.Add(new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (Douban)",
                        Url = this.GetDoubanPoster(primary),
                        Type = ImageType.Primary,
                        Language = language,
                    });
                }
            }

            // 2. 获取 TMDB 季度海报
            if (Config.EnableTmdb && !string.IsNullOrEmpty(seriesTmdbId) && seasonNumber.HasValue)
            {
                try
                {
                    var seasonResult = await this.TmdbApi.GetSeasonAsync(seriesTmdbId.ToInt(), seasonNumber.Value, language, language, cancellationToken).ConfigureAwait(false);
                    if (seasonResult?.Images?.Posters != null)
                    {
                        res.AddRange(seasonResult.Images.Posters.Select(x => new RemoteImageInfo
                        {
                            ProviderName = this.Name + " (TMDB)",
                            Url = this.TmdbApi.GetPosterUrl(x.FilePath)?.ToString(),
                            Type = ImageType.Primary,
                            CommunityRating = x.VoteAverage,
                            VoteCount = x.VoteCount,
                            Width = x.Width,
                            Height = x.Height,
                            Language = x.Iso_639_1,
                        }));
                    }
                }
                catch (HttpRequestException ex)
                {
                    this.Log("Error fetching TMDB season images: {0}", ex.Message);
                }
            }

            // 3. 获取 TVDB 季度海报
            if (Config.EnableTvdb && !string.IsNullOrEmpty(seriesTvdbId) && seasonNumber.HasValue)
            {
                try
                {
                    if (int.TryParse(seriesTvdbId, out var id))
                    {
                        var tvdbSeries = await this.TvdbApi.GetSeriesAsync(id, language, cancellationToken).ConfigureAwait(false);
                        if (tvdbSeries != null)
                        {
                            // 查找对应的 Global Season ID
                            var seasonType = MapDisplayOrderToTvdbType(series.DisplayOrder);
                            var tvdbSeason = tvdbSeries.Seasons?.FirstOrDefault(s => s.Number == seasonNumber.Value && s.Type?.Type == seasonType);
                            
                            // 优先使用季节记录自带的封面
                            if (tvdbSeason != null && !string.IsNullOrEmpty(tvdbSeason.Image))
                            {
                                res.Add(new RemoteImageInfo
                                {
                                    ProviderName = this.Name + " (TVDB)",
                                    Url = tvdbSeason.Image,
                                    Type = ImageType.Primary,
                                    Language = language,
                                });
                            }

                            if (tvdbSeason != null && tvdbSeries.Artworks != null)
                            {
                                // 过滤出对应 seasonId 的海报 (Type 7)
                                res.AddRange(tvdbSeries.Artworks
                                    .Where(a => a.Type == 7 && a.SeasonId == tvdbSeason.Id && !string.IsNullOrEmpty(a.Image))
                                    .Select(a => new RemoteImageInfo
                                    {
                                        ProviderName = this.Name + " (TVDB)",
                                        Url = a.Image,
                                        Type = ImageType.Primary,
                                        Language = a.Language,
                                    }));
                            }
                            
                            // 备选：如果按 seasonId 没找着，尝试找没有任何 seasonId 但可能是该季的海报（针对某些剧集数据不规范的情况）
                            if (res.Count == 0 && tvdbSeries.Artworks != null)
                            {
                                res.AddRange(tvdbSeries.Artworks
                                    .Where(a => a.Type == 7 && !string.IsNullOrEmpty(a.Image))
                                    .Select(a => new RemoteImageInfo
                                    {
                                        ProviderName = this.Name + " (TVDB Fallback)",
                                        Url = a.Image,
                                        Type = ImageType.Primary,
                                        Language = a.Language,
                                    }));
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    this.Log("Error fetching TVDB season images: {0}", ex.Message);
                }
            }

            return res.OrderByLanguageDescending(language);
        }
    }
}
