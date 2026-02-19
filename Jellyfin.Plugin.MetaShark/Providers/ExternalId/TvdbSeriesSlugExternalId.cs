// <copyright file="TvdbSeriesSlugExternalId.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;

    /// <summary>
    /// TheTVDB slug external id.
    /// </summary>
    public class TvdbSeriesSlugExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TheTVDB Slug";

        /// <inheritdoc />
        public string Key => "TvdbSlug";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        /// <inheritdoc />
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
