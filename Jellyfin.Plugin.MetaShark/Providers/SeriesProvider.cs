// <copyright file="SeriesProvider.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Enums;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Core;
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using TMDbLib.Objects.TvShows;
    using MetadataProvider = MediaBrowser.Model.Entities.MetadataProvider;

    /// <summary>
    /// Series provider for MetaShark.
    /// </summary>
    public class SeriesProvider : BaseProvider, IRemoteMetadataProvider<Series, SeriesInfo>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SeriesProvider"/> class.
        /// </summary>
        public SeriesProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<SeriesProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        /// <inheritdoc />
        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(searchInfo);
            this.Log($"GetSearchResults of [name]: {searchInfo.Name}");
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchInfo.Name))
            {
                return result;
            }

            // 从douban搜索
            var res = await this.DoubanApi.SearchTVAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
            result.AddRange(res.Take(Configuration.PluginConfiguration.MAXSEARCHRESULT).Select(x =>
            {
                return new RemoteSearchResult
                {
                    ProviderIds = new Dictionary<string, string> { { DoubanProviderId, x.Sid }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Douban}_{x.Sid}" } },
                    ImageUrl = this.GetProxyImageUrl(new Uri(x.Img, UriKind.Absolute)).ToString(),
                    ProductionYear = x.Year,
                    Name = x.Name,
                };
            }));

            // 尝试从tmdb搜索
            if (Config.EnableTmdbSearch)
            {
                var tmdbList = await this.TmdbApi.SearchSeriesAsync(searchInfo.Name, searchInfo.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                result.AddRange(tmdbList.Take(Configuration.PluginConfiguration.MAXSEARCHRESULT).Select(x =>
                {
                    return new RemoteSearchResult
                    {
                        ProviderIds = new Dictionary<string, string> { { MetadataProvider.Tmdb.ToString(), x.Id.ToString(CultureInfo.InvariantCulture) }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Tmdb}_{x.Id}" } },
                        Name = string.Format(CultureInfo.InvariantCulture, "[TMDB]{0}", x.Name ?? x.OriginalName),
                        ImageUrl = this.TmdbApi.GetPosterUrl(x.PosterPath)?.ToString(),
                        Overview = x.Overview,
                        ProductionYear = x.FirstAirDate?.Year,
                    };
                }));
            }

            // 尝试从tvdb搜索
            if (Config.EnableTvdbSearch)
            {
                var tvdbList = await this.TvdbApi.SearchSeriesAsync(searchInfo.Name, searchInfo.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                result.AddRange(tvdbList.Take(Configuration.PluginConfiguration.MAXSEARCHRESULT).Select(x =>
                {
                    var finalTvdbId = !string.IsNullOrEmpty(x.TvdbId) ? x.TvdbId : x.Id;
                    return new RemoteSearchResult
                    {
                        ProviderIds = new Dictionary<string, string> { { "Tvdb", finalTvdbId }, { MetaSharkPlugin.ProviderId, $"tvdb_{finalTvdbId}" } },
                        Name = string.Format(CultureInfo.InvariantCulture, "[TVDB]{0}", x.Name),
                        ImageUrl = x.ImageUrl?.ToString(),
                        Overview = x.Overview,
                        ProductionYear = int.TryParse(x.Year, out var year) ? year : null,
                        SearchProviderName = this.Name,
                    };
                }));
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (string.IsNullOrEmpty(info.MetadataLanguage) && !string.IsNullOrEmpty(info.Name) && info.Name.HasChinese())
            {
                info.MetadataLanguage = "zh-CN";
            }

            var fileName = GetOriginalFileName(info);
            var result = new MetadataResult<Series>();

            var sid = info.GetProviderId(DoubanProviderId);
            var tmdbId = info.GetProviderId(MetadataProvider.Tmdb);
            var tvdbId = info.GetProviderId("Tvdb");
            var metaSourceStr = info.GetProviderId(MetaSharkPlugin.ProviderId);
            var metaSource = info.GetMetaSource(MetaSharkPlugin.ProviderId);

            // Determine primary source
            var isExplicitTvdb = (metaSource == MetaSource.Tvdb || (metaSourceStr != null && metaSourceStr.StartsWith("tvdb", StringComparison.OrdinalIgnoreCase)));
            var isExplicitTmdb = metaSource == MetaSource.Tmdb && !string.IsNullOrEmpty(tmdbId);
            var isExplicitDouban = metaSource == MetaSource.Douban && !string.IsNullOrEmpty(sid);

            this.Log($"GetSeriesMetadata of [name]: {info.Name} metaSource: {metaSource} tvdbId: {tvdbId} isTvdb: {isExplicitTvdb}");

            if (!isExplicitDouban && !isExplicitTmdb && !isExplicitTvdb && string.IsNullOrEmpty(sid) && string.IsNullOrEmpty(tmdbId) && string.IsNullOrEmpty(tvdbId))
            {
                sid = await this.GuessByDoubanAsync(info, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(sid) && Config.EnableTmdbMatch)
                {
                    tmdbId = await this.GuestByTmdbAsync(info, cancellationToken).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(sid) && string.IsNullOrEmpty(tmdbId) && Config.EnableTvdbMatch)
                {
                    tvdbId = await this.GuessByTvdbAsync(info, cancellationToken).ConfigureAwait(false);
                }
            }

            // --- Execution Logic ---
            if (isExplicitTvdb || (!string.IsNullOrEmpty(tvdbId) && string.IsNullOrEmpty(sid) && string.IsNullOrEmpty(tmdbId)))
            {
                result = await this.GetMetadataByTvdb(tvdbId ?? info.GetProviderId("Tvdb"), info, cancellationToken).ConfigureAwait(false);
            }
            else if (isExplicitTmdb || (!string.IsNullOrEmpty(tmdbId) && string.IsNullOrEmpty(sid)))
            {
                result = await this.GetMetadataByTmdb(tmdbId ?? info.GetProviderId(MetadataProvider.Tmdb.ToString()), info, cancellationToken).ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(sid) || !string.IsNullOrEmpty(info.GetProviderId(DoubanProviderId)))
            {
                var targetSid = sid ?? info.GetProviderId(DoubanProviderId);
                var subject = await this.DoubanApi.GetMovieAsync(targetSid, cancellationToken).ConfigureAwait(false);
                if (subject != null)
                {
                    foreach (var celebrity in await this.DoubanApi.GetCelebritiesBySidAsync(targetSid, cancellationToken).ConfigureAwait(false))
                    {
                        subject.Celebrities.Add(celebrity);
                    }

                    var seriesName = this.RemoveSeasonSuffix(subject.Name);
                    var item = new Series
                    {
                        ProviderIds = new Dictionary<string, string> { { DoubanProviderId, subject.Sid }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Douban}_{subject.Sid}" } },
                        Name = seriesName,
                        OriginalTitle = this.RemoveSeasonSuffix(subject.OriginalName),
                        CommunityRating = subject.Rating,
                        Overview = subject.Intro,
                        ProductionYear = subject.Year,
                        HomePageUrl = "https://www.douban.com",
                        Genres = subject.Genres.ToArray(),
                        PremiereDate = subject.ScreenTime,
                    };

                    if (!string.IsNullOrEmpty(subject.Imdb))
                    {
                        var newImdbId = await this.CheckNewImdbID(subject.Imdb, cancellationToken).ConfigureAwait(false);
                        item.SetProviderId(MetadataProvider.Imdb, newImdbId);
                    }

                    var linkedTmdbId = await this.FindTmdbId(seriesName, subject.Imdb, subject.Year, info, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(linkedTmdbId))
                    {
                        item.SetProviderId(MetadataProvider.Tmdb, linkedTmdbId);
                        await this.TryPopulateTvExternalIdsFromTmdbAsync(item, linkedTmdbId, info, cancellationToken).ConfigureAwait(false);
                    }

                    result.Item = item;
                    result.HasMetadata = true;
                    result.QueriedById = true;
                    subject.LimitDirectorCelebrities.Take(10).ToList().ForEach(c => result.AddPerson(new PersonInfo
                    {
                        Name = c.Name,
                        Type = c.RoleType == PersonType.Director ? PersonKind.Director : PersonKind.Actor,
                        Role = c.Role,
                        ImageUrl = GetLocalProxyImageUrl(new Uri(c.Img, UriKind.Absolute)).ToString(),
                        ProviderIds = new Dictionary<string, string> { { DoubanProviderId, c.Id } },
                    }));
                }
            }

            // --- Cross-Source Completion & Rating Logic ---
            if (result.HasMetadata && result.Item != null)
            {
                if (string.IsNullOrEmpty(result.Item.GetProviderId("Tvdb")) && Config.EnableTvdbMatch)
                {
                    var foundTvdbId = await this.GuessByTvdbAsync(info, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(foundTvdbId))
                    {
                        result.Item.SetProviderId("Tvdb", foundTvdbId);
                    }
                }

                float? finalRating = null;
                var currentSid = result.Item.GetProviderId(DoubanProviderId);
                if (!string.IsNullOrEmpty(currentSid))
                {
                    var doubanSubject = await this.DoubanApi.GetMovieAsync(currentSid, cancellationToken).ConfigureAwait(false);
                    if (doubanSubject != null && doubanSubject.Rating > 0)
                    {
                        finalRating = doubanSubject.Rating;
                    }
                }

                if (finalRating == null || finalRating <= 0)
                {
                    var currentTmdbId = result.Item.GetProviderId(MetadataProvider.Tmdb);
                    if (!string.IsNullOrEmpty(currentTmdbId))
                    {
                        var tmdbShow = await this.TmdbApi.GetSeriesAsync(Convert.ToInt32(currentTmdbId, CultureInfo.InvariantCulture), info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                        if (tmdbShow != null && tmdbShow.VoteAverage > 0)
                        {
                            finalRating = (float)Math.Round(tmdbShow.VoteAverage, 2);
                        }
                    }
                }

                if (finalRating.HasValue && finalRating > 0)
                {
                    result.Item.CommunityRating = finalRating;
                }
            }

            return result;
        }

        private async Task<MetadataResult<Series>> GetMetadataByTvdb(string? tvdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            if (string.IsNullOrEmpty(tvdbId) || !int.TryParse(tvdbId, out var id))
            {
                return result;
            }

            this.Log($"GetSeriesMetadata of tvdb [id]: \"{tvdbId}\"");
            var tvdbSeries = await this.TvdbApi.GetSeriesAsync(id, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);

            if (tvdbSeries == null)
            {
                return result;
            }

            var name = tvdbSeries.Name;
            var overview = tvdbSeries.Overview;

            if (tvdbSeries.Translations != null)
            {
                var targetLang = info.MetadataLanguage ?? "zh-CN";
                var langCode = targetLang.Split('-')[0].ToUpperInvariant();
                var searchLangs = new[] { targetLang, langCode, "ZHO", "CHI", "ZH", "zh-hans", "zh-hant" };

                if (tvdbSeries.Translations.NameTranslations != null)
                {
                    var translation = tvdbSeries.Translations.NameTranslations.FirstOrDefault(t =>
                        t.Language != null && searchLangs.Any(sl => sl.Equals(t.Language, StringComparison.OrdinalIgnoreCase)));

                    if (translation != null && !string.IsNullOrEmpty(translation.Name))
                    {
                        name = translation.Name;
                    }
                }

                if (tvdbSeries.Translations.OverviewTranslations != null)
                {
                    var translation = tvdbSeries.Translations.OverviewTranslations.FirstOrDefault(t =>
                        t.Language != null && searchLangs.Any(sl => sl.Equals(t.Language, StringComparison.OrdinalIgnoreCase)));

                    if (translation != null && !string.IsNullOrEmpty(translation.Overview))
                    {
                        overview = translation.Overview;
                    }
                }
            }

            var item = new Series
            {
                Name = name,
                Overview = overview,
                PremiereDate = DateTime.TryParse(tvdbSeries.FirstAired, out var d) ? d : null,
                ProductionYear = DateTime.TryParse(tvdbSeries.FirstAired, out var d2) ? d2.Year : null,
                Genres = tvdbSeries.Genres?.Select(g => g.Name).ToArray(),
                Studios = tvdbSeries.Companies?.Where(c => c.CompanyType?.Name == "Network" || c.CompanyType?.Name == "Production Company").Select(c => c.Name).ToArray(),
            };

            item.SetProviderId("Tvdb", tvdbId);
            item.SetProviderId(MetaSharkPlugin.ProviderId, $"tvdb_{tvdbId}");

            result.Item = item;
            result.QueriedById = true;
            result.HasMetadata = true;
            return result;
        }

        private async Task<MetadataResult<Series>> GetMetadataByTmdb(string? tmdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            if (string.IsNullOrEmpty(tmdbId))
            {
                return result;
            }

            this.Log($"GetSeriesMetadata of tmdb [id]: \"{tmdbId}\"");
            var tvShow = await this.TmdbApi
                .GetSeriesAsync(Convert.ToInt32(tmdbId, CultureInfo.InvariantCulture), info.MetadataLanguage, info.MetadataLanguage, cancellationToken)
                .ConfigureAwait(false);

            if (tvShow == null)
            {
                return result;
            }

            result = new MetadataResult<Series>
            {
                Item = this.MapTvShowToSeries(tvShow, info.MetadataCountryCode),
                ResultLanguage = info.MetadataLanguage ?? tvShow.OriginalLanguage,
            };

            foreach (var person in this.GetPersons(tvShow))
            {
                result.AddPerson(person);
            }

            result.QueriedById = true;
            result.HasMetadata = true;
            return result;
        }

        private async Task<string?> FindTmdbId(string name, string imdb, int? year, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(imdb))
            {
                var tid = await this.GetTmdbIdByImdbAsync(imdb, info.MetadataLanguage, info, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(tid))
                {
                    return tid;
                }
            }

            if (!string.IsNullOrEmpty(name) && year != null && year > 0)
            {
                return await this.GuestByTmdbAsync(name, year, info, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private Series MapTvShowToSeries(TvShow seriesResult, string preferredCountryCode)
        {
            var series = new Series
            {
                Name = seriesResult.Name,
                OriginalTitle = seriesResult.OriginalName,
                CommunityRating = (float)System.Math.Round(seriesResult.VoteAverage, 2),
                Overview = seriesResult.Overview,
                Studios = seriesResult.Networks?.Select(i => i.Name).ToArray(),
                Genres = seriesResult.Genres?.Select(i => i.Name).ToArray(),
                HomePageUrl = seriesResult.Homepage,
                PremiereDate = seriesResult.FirstAirDate,
                ProductionYear = seriesResult.FirstAirDate?.Year,
                Status = string.Equals(seriesResult.Status, "Ended", StringComparison.OrdinalIgnoreCase) ? SeriesStatus.Ended : SeriesStatus.Continuing,
                EndDate = string.Equals(seriesResult.Status, "Ended", StringComparison.OrdinalIgnoreCase) ? seriesResult.LastAirDate : null
            };

            series.SetProviderId(MetadataProvider.Tmdb, seriesResult.Id.ToString(CultureInfo.InvariantCulture));
            if (seriesResult.ExternalIds != null)
            {
                if (!string.IsNullOrWhiteSpace(seriesResult.ExternalIds.ImdbId))
                {
                    series.SetProviderId(MetadataProvider.Imdb, seriesResult.ExternalIds.ImdbId);
                }

                if (!string.IsNullOrWhiteSpace(seriesResult.ExternalIds.TvdbId))
                {
                    series.SetProviderId("Tvdb", seriesResult.ExternalIds.TvdbId);
                }
            }

            series.SetProviderId(MetaSharkPlugin.ProviderId, $"{MetaSource.Tmdb}_{seriesResult.Id}");
            return series;
        }

        private async Task TryPopulateTvExternalIdsFromTmdbAsync(Series series, string tmdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (!int.TryParse(tmdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tid))
            {
                return;
            }

            var tvShow = await this.TmdbApi
                .GetSeriesAsync(tid, info.MetadataLanguage, info.MetadataLanguage, cancellationToken)
                .ConfigureAwait(false);

            var externalIds = tvShow?.ExternalIds;
            if (externalIds == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(series.GetProviderId(MetadataProvider.Imdb)) && !string.IsNullOrWhiteSpace(externalIds.ImdbId))
            {
                series.SetProviderId(MetadataProvider.Imdb, externalIds.ImdbId);
            }

            if (!string.IsNullOrWhiteSpace(externalIds.TvdbId))
            {
                series.SetProviderId("Tvdb", externalIds.TvdbId);
                this.Log("Set series tvdb id by tmdb external ids. tmdbId: {0} tvdbId: {1}", tmdbId, externalIds.TvdbId);
            }
        }

        private IEnumerable<PersonInfo> GetPersons(TvShow seriesResult)
        {
            if (seriesResult.Credits?.Cast != null)
            {
                foreach (var actor in seriesResult.Credits.Cast.OrderBy(a => a.Order).Take(Configuration.PluginConfiguration.MAXCASTMEMBERS))
                {
                    yield return new PersonInfo
                    {
                        Name = actor.Name.Trim(),
                        Role = actor.Character,
                        Type = PersonKind.Actor,
                        SortOrder = actor.Order,
                        ImageUrl = this.TmdbApi.GetProfileUrl(actor.ProfilePath)?.ToString()
                    };
                }
            }

            if (seriesResult.Credits?.Crew != null)
            {
                foreach (var person in seriesResult.Credits.Crew)
                {
                    var type = MapCrewToPersonType(person);
                    if (string.IsNullOrEmpty(type))
                    {
                        continue;
                    }

                    yield return new PersonInfo
                    {
                        Name = person.Name.Trim(),
                        Role = person.Job,
                        Type = type == PersonType.Director ? PersonKind.Director : (type == PersonType.Producer ? PersonKind.Producer : PersonKind.Actor),
                        ImageUrl = this.TmdbApi.GetPosterUrl(person.ProfilePath)?.ToString()
                    };
                }
            }
        }
    }
}
