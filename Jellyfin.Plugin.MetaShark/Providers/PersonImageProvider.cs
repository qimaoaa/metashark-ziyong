// <copyright file="PersonImageProvider.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Plugin.MetaShark.Api;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public class PersonImageProvider : BaseProvider, IRemoteImageProvider
    {
        public PersonImageProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<PersonImageProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Person;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            var list = new List<RemoteImageInfo>();
            var cid = item.GetProviderId(DoubanProviderId);
            var metaSource = item.GetMetaSource(MetaSharkPlugin.ProviderId);
            this.Log($"GetImages for item: {item.Name} [metaSource]: {metaSource}");
            if (!string.IsNullOrEmpty(cid))
            {
                var celebrity = await this.DoubanApi.GetCelebrityAsync(cid, cancellationToken).ConfigureAwait(false);
                if (celebrity != null)
                {
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = this.Name,
                        Url = this.GetProxyImageUrl(new Uri(celebrity.Img, UriKind.Absolute)).ToString(),
                        Type = ImageType.Primary,
                        Language = "zh",
                    });
                }

                var photos = await this.DoubanApi.GetCelebrityPhotosAsync(cid, cancellationToken).ConfigureAwait(false);
                photos.ForEach(x =>
                {
                    // 过滤不是竖图
                    if (x.Width < 400 || x.Height < x.Width * 1.3)
                    {
                        return;
                    }

                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = this.Name,
                        Url = this.GetProxyImageUrl(new Uri(x.Raw, UriKind.Absolute)).ToString(),
                        Width = x.Width,
                        Height = x.Height,
                        Type = ImageType.Primary,
                        Language = "zh",
                    });
                });
            }

            if (list.Count == 0)
            {
                this.Log($"Got images failed because the images of \"{item.Name}\" is empty!");
            }

            return list;
        }
    }
}
