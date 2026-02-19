// <copyright file="TvdbEpisodeExternalId.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;

    /// <summary>
    /// TheTVDB episode external id.
    /// </summary>
    public class TvdbEpisodeExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TheTVDB";

        /// <inheritdoc />
        public string Key => "Tvdb";

        /// <inheritdoc />
        public string UrlFormatString => "https://www.thetvdb.com/?tab=episode&id={0}";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Episode;
    }
}
