namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    public class TvdbSeasonExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Season;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=season&id={0}";
        public bool Supports(IHasProviderIds item) => item is Season;
    }
}
