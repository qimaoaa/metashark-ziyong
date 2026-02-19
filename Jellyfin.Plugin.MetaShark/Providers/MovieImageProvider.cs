// <copyright file="MovieImageProvider.cs" company="PlaceholderCompany">
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
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Dto;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public class MovieImageProvider : BaseProvider, IRemoteImageProvider
    {
        public MovieImageProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<MovieImageProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Movie;

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

            this.Log($"GetImages for movie: {item.Name} lang: {language} [metaSource]: {metaSource} tvdbId: {tvdbId}");

            // 1. 获取豆瓣图片
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

            // 3. 获取 TVDB 图片 (电影也支持)
            if (Config.EnableTvdb && !string.IsNullOrEmpty(tvdbId))
            {
                var tvdbImages = await this.GetTvdbImages(tvdbId, language, cancellationToken).ConfigureAwait(false);
                res.AddRange(tvdbImages);
            }

            return res.OrderByLanguageDescending(language);
        }

        private async Task<List<RemoteImageInfo>> GetTmdbImages(string tmdbId, string language, CancellationToken cancellationToken)
        {
            var res = new List<RemoteImageInfo>();
            try
            {
                var movie = await this.TmdbApi.GetMovieAsync(tmdbId.ToInt(), language, language, cancellationToken).ConfigureAwait(false);
                var images = await this.TmdbApi.GetMovieImagesAsync(tmdbId.ToInt(), string.Empty, string.Empty, cancellationToken).ConfigureAwait(false);

                if (movie != null && images != null)
                {
                    res.AddRange(images.Posters.Select(x => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetPosterUrl(x.FilePath)?.ToString(),
                        Type = ImageType.Primary,
                        Language = x.Iso_639_1,
                    }));

                    res.AddRange(images.Backdrops.Select(x => new RemoteImageInfo
                    {
                        ProviderName = this.Name + " (TMDB)",
                        Url = this.TmdbApi.GetBackdropUrl(x.FilePath)?.ToString(),
                        Type = ImageType.Backdrop,
                        Language = x.Iso_639_1,
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
                this.Log("Error fetching TMDB movie images: {0}", ex.Message);
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
                    // TVDB movies use the same endpoint but type is different internally, v4 usually handles it
                    var tvdbMovie = await this.TvdbApi.GetSeriesAsync(id, language, cancellationToken).ConfigureAwait(false);
                    if (tvdbMovie != null)
                    {
                        if (!string.IsNullOrEmpty(tvdbMovie.Image))
                        {
                            res.Add(new RemoteImageInfo
                            {
                                ProviderName = this.Name + " (TVDB)",
                                Url = tvdbMovie.Image,
                                Type = ImageType.Primary,
                            });
                        }

                        if (tvdbMovie.Artworks != null)
                        {
                            foreach (var art in tvdbMovie.Artworks)
                            {
                                if (string.IsNullOrEmpty(art.Image))
                                {
                                    continue;
                                }

                                var imgType = ImageType.Primary;
                                if (art.Type == 2)
                                {
                                    imgType = ImageType.Primary;
                                }
                                else if (art.Type == 12 || art.Type == 3)
                                {
                                    imgType = ImageType.Backdrop;
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
                                });
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                this.Log("Error fetching TVDB movie images: {0}", ex.Message);
            }

            return res;
        }

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

            return list;
        }

        private async Task<IEnumerable<RemoteImageInfo>> GetLogos(BaseItem item, string alternativeImageLanguage, CancellationToken cancellationToken)
        {
            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            var language = item.GetPreferredMetadataLanguage();
            var list = new List<RemoteImageInfo>();
            if (Config.EnableTmdbLogo && !string.IsNullOrEmpty(tmdbId))
            {
                var images = await this.TmdbApi.GetMovieImagesAsync(tmdbId.ToInt(), string.Empty, string.Empty, cancellationToken).ConfigureAwait(false);
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
