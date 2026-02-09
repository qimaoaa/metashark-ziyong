// <copyright file="DoubanExternalUrlProvider.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;

    /// <summary>
    /// External URLs for Douban.
    /// </summary>
    // Internal to avoid registering a second visible Douban provider entry.
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via Jellyfin type discovery.")]
    internal sealed class DoubanExternalUrlProvider : IExternalUrlProvider
    {
        /// <inheritdoc/>
        public string Name => BaseProvider.DoubanProviderName;

        /// <inheritdoc/>
        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            switch (item)
            {
                case Person:
                    if (item.TryGetProviderId(BaseProvider.DoubanProviderId, out var externalId))
                    {
                        yield return $"https://www.douban.com/personage/{externalId}/";
                    }

                    break;
                default:
                    if (item.TryGetProviderId(BaseProvider.DoubanProviderId, out externalId))
                    {
                        yield return $"https://movie.douban.com/subject/{externalId}/";
                    }

                    break;
            }
        }
    }
}