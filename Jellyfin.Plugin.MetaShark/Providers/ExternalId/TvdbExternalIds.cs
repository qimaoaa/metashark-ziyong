namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;

    public class TvdbSeriesExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";
        public bool Supports(IHasProviderIds item) => item is Series;
    }

    public class TvdbSeriesSlugExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB Slug";
        public string Key => "TvdbSlug";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";
        public bool Supports(IHasProviderIds item) => item is Series;
    }

    public class TvdbMovieExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";
        public bool Supports(IHasProviderIds item) => item is Movie;
    }

    public class TvdbMovieSlugExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB Slug";
        public string Key => "TvdbSlug";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";
        public bool Supports(IHasProviderIds item) => item is Movie;
    }

    public class TvdbSeasonExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Season;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=season&id={0}";
        public bool Supports(IHasProviderIds item) => item is Season;
    }

    public class TvdbEpisodeExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=episode&id={0}";
        public bool Supports(IHasProviderIds item) => item is Episode;
    }
}
