// <copyright file="TvdbMovieSlugExternalId.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;

    /// <summary>
    /// TheTVDB movie slug external id.
    /// </summary>
    public class TvdbMovieSlugExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TheTVDB Slug";

        /// <inheritdoc />
        public string Key => "TvdbSlug";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        /// <inheritdoc />
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
