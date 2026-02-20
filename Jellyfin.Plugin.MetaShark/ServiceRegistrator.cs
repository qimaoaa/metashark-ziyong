namespace Jellyfin.Plugin.MetaShark
{
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Providers.ExternalId;
    using MediaBrowser.Controller;
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Plugins;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public class ServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHostedService<BoxSetManager>();
            serviceCollection.AddSingleton((ctx) => new DoubanApi(ctx.GetRequiredService<ILoggerFactory>()));
            serviceCollection.AddSingleton((ctx) => new TmdbApi(ctx.GetRequiredService<ILoggerFactory>()));
            serviceCollection.AddSingleton((ctx) => new OmdbApi(ctx.GetRequiredService<ILoggerFactory>()));
            serviceCollection.AddSingleton((ctx) => new ImdbApi(ctx.GetRequiredService<ILoggerFactory>()));
            serviceCollection.AddSingleton((ctx) => new TvdbApi(ctx.GetRequiredService<ILoggerFactory>()));

            serviceCollection.AddSingleton<IExternalId, DoubanExternalId>();
            serviceCollection.AddSingleton<IExternalId, DoubanPersonExternalId>();
            serviceCollection.AddSingleton<IExternalId, TvdbSeriesExternalId>();
            serviceCollection.AddSingleton<IExternalId, TvdbSeriesSlugExternalId>();
            serviceCollection.AddSingleton<IExternalId, TvdbMovieExternalId>();
            serviceCollection.AddSingleton<IExternalId, TvdbMovieSlugExternalId>();
            serviceCollection.AddSingleton<IExternalId, TvdbSeasonExternalId>();
            serviceCollection.AddSingleton<IExternalId, TvdbEpisodeExternalId>();
        }
    }

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
        public string ProviderName => "TheTVDB Season";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Season;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=season&id={0}";
        public bool Supports(IHasProviderIds item) => item is Season;
    }

    public class TvdbEpisodeExternalId : IExternalId
    {
        public string ProviderName => "TheTVDB Episode";
        public string Key => "Tvdb";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;
        public string UrlFormatString => "https://www.thetvdb.com/?tab=episode&id={0}";
        public bool Supports(IHasProviderIds item) => item is Episode;
    }
}
