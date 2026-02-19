namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    public class TvdbSeriesSlugExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB Slug";
        public string Key => "TvdbSlug";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=series&id={0}";
        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
