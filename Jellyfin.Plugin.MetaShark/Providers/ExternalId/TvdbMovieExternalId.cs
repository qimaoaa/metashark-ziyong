// <copyright file="TvdbMovieExternalId.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;

    /// <summary>
    /// TheTVDB movie external id.
    /// </summary>
    public class TvdbMovieExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TheTVDB";

        /// <inheritdoc />
        public string Key => "Tvdb";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        /// <inheritdoc />
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
