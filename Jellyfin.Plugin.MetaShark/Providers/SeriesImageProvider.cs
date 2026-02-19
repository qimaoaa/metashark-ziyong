// <copyright file="SeriesImageProvider.cs" company="PlaceholderCompany">
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
    using MediaBrowser.Model.Dto;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Series image provider.
    /// </summary>
    public class SeriesImageProvider : BaseProvider, IRemoteImageProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SeriesImageProvider"/> class.
        /// </summary>
        public SeriesImageProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<SeriesImageProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Series;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>
        {
            ImageType.Primary,
            ImageType.Backdrop,
            ImageType.Logo,
        };

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            var sid = item.GetProviderId(DoubanProviderId);
            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            var tvdbId = item.GetProviderId("Tvdb");
            var metaSource = item.GetMetaSource(MetaSharkPlugin.ProviderId);
            var language = item.GetPreferredMetadataLanguage();

            var res = new List<RemoteImageInfo>();

            this.Log($"GetImages for item: {item.Name} lang: {language} [metaSource]: {metaSource} tvdbId: {tvdbId}");

            // 1. 获取豆瓣图片 (作为主选或备选)
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

                // 豆瓣背景图
                var doubanBackdrops = await this.GetBackdrop(item, string.Empty, cancellationToken).ConfigureAwait(false);
                res.AddRange(doubanBackdrops);
            }

            // 2. 获取 TMDB 图片
            if (Config.EnableTmdb && !string.IsNullOrEmpty(tmdbId))
            {
                var tmdbImages = await this.GetTmdbImages(tmdbId, language, cancellationToken).ConfigureAwait(false);
                res.AddRange(tmdbImages);
            }

            // 3. 获取 TVDB 图片
            if (Config.EnableTvdb && !string.IsNullOrEmpty(tvdbId))
            {
                var tvdbImages = await this.GetTvdbImages(tvdbId, language, cancellationToken).ConfigureAwait(false);
                res.AddRange(tvdbImages);
            }

            if (res.Count == 0)
            {
                this.Log($"Got images failed because the images of \"{item.Name}\" is empty!");
            }

            return res.OrderByLanguageDescending(language);
        }

        private async Task<List<RemoteImageInfo>> GetTmdbImages(string tmdbId, string language, CancellationToken cancellationToken)
        {
            var res = new List<RemoteImageInfo>();
            try
            {
                var movie = await this.TmdbApi.GetSeriesAsync(tmdbId.ToInt(), language, language, cancellationToken).ConfigureAwait(false);
                var images = await this.TmdbApi.GetSeriesImagesAsync(tmdbId.ToInt(), string.Empty, string.Empty, cancellationToken).ConfigureAwait(false);

                if (movie != null && images != null)
                {
                    res.AddRange(images.Posters.Select(x => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetPosterUrl(x.FilePath)?.ToString(),
                        Type = ImageType.Primary,
                        CommunityRating = x.VoteAverage,
                        VoteCount = x.VoteCount,
                        Width = x.Width,
                        Height = x.Height,
                        Language = x.Iso_639_1,
                        RatingType = RatingType.Score,
                    }));

                    res.AddRange(images.Backdrops.Select(x => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetBackdropUrl(x.FilePath)?.ToString(),
                        Type = ImageType.Backdrop,
                        CommunityRating = x.VoteAverage,
                        VoteCount = x.VoteCount,
                        Width = x.Width,
                        Height = x.Height,
                        Language = x.Iso_639_1,
                        RatingType = RatingType.Score,
                    }));

                    res.AddRange(images.Logos.Select(x => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetLogoUrl(x.FilePath)?.ToString(),
                        Type = ImageType.Logo,
                        Language = x.Iso_639_1,
                    }));
                }
            }
            catch (HttpRequestException ex)
            {
                this.Log("Error fetching TMDB images: {0}", ex.Message);
            }

            return res;
        }

        private async Task<List<RemoteImageInfo>> GetTvdbImages(string tvdbId, string language, CancellationToken cancellationToken)
        {
            var res = new List<RemoteImageInfo>();
            try
            {
                if (int.TryParse(tvdbId, out var id))
                {
                    var tvdbSeries = await this.TvdbApi.GetSeriesAsync(id, language, cancellationToken).ConfigureAwait(false);
                    if (tvdbSeries != null)
                    {
                        // 1. 默认封面
                        if (!string.IsNullOrEmpty(tvdbSeries.Image))
                        {
                            res.Add(new RemoteImageInfo
                            {
                                ProviderName = this.Name + " (TVDB)",
                                Url = tvdbSeries.Image,
                                Type = ImageType.Primary,
                            });
                        }

                        // 2. 扩展 Artwork
                        if (tvdbSeries.Artworks != null)
                        {
                            foreach (var art in tvdbSeries.Artworks)
                            {
                                if (string.IsNullOrEmpty(art.Image))
                                {
                                    continue;
                                }

                                var imgType = ImageType.Primary;
                                if (art.Type == 2)
                                {
                                    imgType = ImageType.Primary; // Poster
                                }
                                else if (art.Type == 12 || art.Type == 3)
                                {
                                    imgType = ImageType.Backdrop; // Backdrop / Fanart
                                }
                                else
                                {
                                    continue;
                                }

                                res.Add(new RemoteImageInfo
                                {
                                    ProviderName = this.Name + " (TVDB)",
                                    Url = art.Image,
                                    Type = imgType,
                                    Language = art.Language,
                                    Width = 0,
                                    Height = 0,
                                });
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                this.Log("Error fetching TVDB images: {0}", ex.Message);
            }

            return res;
        }

        /// <summary>
        /// Query for a background photo from Douban.
        /// </summary>
        private async Task<IEnumerable<RemoteImageInfo>> GetBackdrop(BaseItem item, string alternativeImageLanguage, CancellationToken cancellationToken)
        {
            var sid = item.GetProviderId(DoubanProviderId);
            var list = new List<RemoteImageInfo>();

            if (!string.IsNullOrEmpty(sid))
            {
                var photo = await this.DoubanApi.GetWallpaperBySidAsync(sid, cancellationToken).ConfigureAwait(false);
                if (photo != null && photo.Count > 0)
                {
                    list = photo.Where(x => x.Width >= 1280 && x.Width <= 4096 && x.Width > x.Height * 1.3).Select(x =>
                    {
                        if (Config.EnableDoubanBackdropRaw)
                        {
                            return new RemoteImageInfo
                            {
                                ProviderName = this.Name + " (Douban)",
                                Url = this.GetProxyImageUrl(new Uri(x.Raw, UriKind.Absolute)).ToString(),
                                Height = x.Height,
                                Width = x.Width,
                                Type = ImageType.Backdrop,
                                Language = "zh",
                            };
                        }
                        else
                        {
                            return new RemoteImageInfo
                            {
                                ProviderName = this.Name + " (Douban)",
                                Url = this.GetProxyImageUrl(new Uri(x.Large, UriKind.Absolute)).ToString(),
                                Type = ImageType.Backdrop,
                                Language = "zh",
                            };
                        }
                    }).ToList();
                }
            }

            // 添加 TheMovieDb 背景图为备选
            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            if (Config.EnableTmdbBackdrop && !string.IsNullOrEmpty(tmdbId))
            {
                var language = item.GetPreferredMetadataLanguage();
                var movie = await this.TmdbApi
                .GetSeriesAsync(tmdbId.ToInt(), language, language, cancellationToken)
                .ConfigureAwait(false);

                if (movie != null && !string.IsNullOrEmpty(movie.BackdropPath))
                {
                    this.Log("GetBackdrop from tmdb id: {0} lang: {1}", tmdbId, language);
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetBackdropUrl(movie.BackdropPath)?.ToString(),
                        Type = ImageType.Backdrop,
                        Language = language,
                    });
                }
            }

            // 添加 TheTVDB 背景图为备选
            var tvdbIdBackdrop = item.GetProviderId("Tvdb");
            if (Config.EnableTvdbBackdrop && !string.IsNullOrEmpty(tvdbIdBackdrop))
            {
                var tvdbSeries = await this.TvdbApi.GetSeriesAsync(int.Parse(tvdbIdBackdrop, CultureInfo.InvariantCulture), item.GetPreferredMetadataLanguage(), cancellationToken).ConfigureAwait(false);
                if (tvdbSeries?.Artworks != null)
                {
                    list.AddRange(tvdbSeries.Artworks.Where(a => (a.Type == 12 || a.Type == 3) && !string.IsNullOrEmpty(a.Image)).Select(a => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TVDB)",
                        Url = a.Image,
                        Type = ImageType.Backdrop,
                        Language = a.Language,
                    }));
                }
            }

            return list;
        }

        private async Task<IEnumerable<RemoteImageInfo>> GetLogos(BaseItem item, string alternativeImageLanguage, CancellationToken cancellationToken)
        {
            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            var language = item.GetPreferredMetadataLanguage();
            var list = new List<RemoteImageInfo>();
            if (Config.EnableTmdbLogo && !string.IsNullOrEmpty(tmdbId))
            {
                var images = await this.TmdbApi.GetSeriesImagesAsync(tmdbId.ToInt(), string.Empty, string.Empty, cancellationToken).ConfigureAwait(false);
                if (images != null)
                {
                    list.AddRange(images.Logos.Select(x => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetLogoUrl(x.FilePath)?.ToString(),
                        Type = ImageType.Logo,
                        Language = AdjustImageLanguage(x.Iso_639_1, language),
                    }));
                }
            }

            return AdjustImageLanguagePriority(list, language, alternativeImageLanguage);
        }
    }
}
