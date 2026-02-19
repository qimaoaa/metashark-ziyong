namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    public class TvdbMovieSlugExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB Slug";
        public string Key => "TvdbSlug";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";
        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
