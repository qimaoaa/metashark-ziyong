// <copyright file="ServiceRegistrator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark
{
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Providers.ExternalId;
    using MediaBrowser.Controller;
    using MediaBrowser.Controller.Plugins;
    using MediaBrowser.Controller.Providers;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <inheritdoc />
    public class ServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHostedService<BoxSetManager>();
            serviceCollection.AddSingleton((ctx) =>
            {
                return new DoubanApi(ctx.GetRequiredService<ILoggerFactory>());
            });
            serviceCollection.AddSingleton((ctx) =>
            {
                return new TmdbApi(ctx.GetRequiredService<ILoggerFactory>());
            });
            serviceCollection.AddSingleton((ctx) =>
            {
                return new OmdbApi(ctx.GetRequiredService<ILoggerFactory>());
            });
            serviceCollection.AddSingleton((ctx) =>
            {
                return new ImdbApi(ctx.GetRequiredService<ILoggerFactory>());
            });
            serviceCollection.AddSingleton((ctx) =>
            {
                return new TvdbApi(ctx.GetRequiredService<ILoggerFactory>());
            });

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
}
