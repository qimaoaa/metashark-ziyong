namespace Jellyfin.Plugin.MetaShark.Providers.ExternalId
{
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    public class TvdbEpisodeExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=episode&id={0}";
        public bool Supports(IHasProviderIds item) => item is Episode;
    }
}
