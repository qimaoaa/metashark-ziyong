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
    public class SeriesProvider : BaseProvider, IRemoteMetadataProvider<Series, SeriesInfo>
    {
        public SeriesProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<SeriesProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi) { }
        public string Name => MetaSharkPlugin.PluginName;
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchInfo.Name)) return result;
            var res = await this.DoubanApi.SearchTVAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
            result.AddRange(res.Take(5).Select(x => new RemoteSearchResult {
                ProviderIds = new Dictionary<string, string> { { DoubanProviderId, x.Sid }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Douban}_{x.Sid}" } },
                ImageUrl = this.GetProxyImageUrl(new Uri(x.Img, UriKind.Absolute)).ToString(), ProductionYear = x.Year, Name = x.Name,
            }));
            if (Config.EnableTmdbSearch) {
                var tmdbList = await this.TmdbApi.SearchSeriesAsync(searchInfo.Name, searchInfo.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                result.AddRange(tmdbList.Take(5).Select(x => new RemoteSearchResult {
                    ProviderIds = new Dictionary<string, string> { { MetadataProvider.Tmdb.ToString(), x.Id.ToString(CultureInfo.InvariantCulture) }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Tmdb}_{x.Id}" } },
                    Name = $"[TMDB]{x.Name ?? x.OriginalName}", ImageUrl = this.TmdbApi.GetPosterUrl(x.PosterPath)?.ToString(), Overview = x.Overview, ProductionYear = x.FirstAirDate?.Year,
                }));
            }
            if (Config.EnableTvdbSearch) {
                var tvdbList = await this.TvdbApi.SearchSeriesAsync(searchInfo.Name, searchInfo.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                result.AddRange(tvdbList.Take(5).Select(x => {
                    var fid = !string.IsNullOrEmpty(x.TvdbId) ? x.TvdbId : x.Id;
                    return new RemoteSearchResult {
                        ProviderIds = new Dictionary<string, string> { { "Tvdb", fid }, { MetaSharkPlugin.ProviderId, $"tvdb_{fid}" } },
                        Name = $"[TVDB]{x.Name}", ImageUrl = x.ImageUrl?.ToString(), Overview = x.Overview, ProductionYear = int.TryParse(x.Year, out var y) ? y : null, SearchProviderName = this.Name,
                    };
                }));
            }
            return result;
        }
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(info.MetadataLanguage) && !string.IsNullOrEmpty(info.Name) && info.Name.HasChinese()) info.MetadataLanguage = "zh-CN";
            var result = new MetadataResult<Series>();
            var sid = info.GetProviderId(DoubanProviderId);
            var tmdbId = info.GetProviderId(MetadataProvider.Tmdb);
            var tvdbId = info.GetProviderId("Tvdb");
            var metaSourceStr = info.GetProviderId(MetaSharkPlugin.ProviderId);
            var metaSource = info.GetMetaSource(MetaSharkPlugin.ProviderId);
            var isTvdb = (metaSource == MetaSource.Tvdb || (metaSourceStr != null && metaSourceStr.StartsWith("tvdb", StringComparison.OrdinalIgnoreCase)));
            var isTmdb = metaSource == MetaSource.Tmdb && !string.IsNullOrEmpty(tmdbId);
            var isDouban = metaSource == MetaSource.Douban && !string.IsNullOrEmpty(sid);
            if (!isDouban && !isTmdb && !isTvdb) {
                sid = await this.GuessByDoubanAsync(info, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(sid) && Config.EnableTmdbMatch) tmdbId = await this.GuestByTmdbAsync(info, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(sid) && string.IsNullOrEmpty(tmdbId) && Config.EnableTvdbMatch) tvdbId = await this.GuessByTvdbAsync(info, cancellationToken).ConfigureAwait(false);
            }
            if (isTvdb || (!string.IsNullOrEmpty(tvdbId) && string.IsNullOrEmpty(sid) && string.IsNullOrEmpty(tmdbId)))
                result = await this.GetMetadataByTvdb(tvdbId ?? info.GetProviderId("Tvdb"), info, cancellationToken).ConfigureAwait(false);
            else if (isTmdb || (!string.IsNullOrEmpty(tmdbId) && string.IsNullOrEmpty(sid)))
                result = await this.GetMetadataByTmdb(tmdbId ?? info.GetProviderId(MetadataProvider.Tmdb.ToString()), info, cancellationToken).ConfigureAwait(false);
            else if (!string.IsNullOrEmpty(sid) || !string.IsNullOrEmpty(info.GetProviderId(DoubanProviderId))) {
                var targetSid = sid ?? info.GetProviderId(DoubanProviderId);
                var subject = await this.DoubanApi.GetMovieAsync(targetSid, cancellationToken).ConfigureAwait(false);
                if (subject != null) {
                    foreach (var c in await this.DoubanApi.GetCelebritiesBySidAsync(targetSid, cancellationToken).ConfigureAwait(false)) subject.Celebrities.Add(c);
                    var seriesName = this.RemoveSeasonSuffix(subject.Name);
                    var item = new Series {
                        ProviderIds = new Dictionary<string, string> { { DoubanProviderId, subject.Sid }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Douban}_{subject.Sid}" } },
                        Name = seriesName, OriginalTitle = this.RemoveSeasonSuffix(subject.OriginalName), CommunityRating = subject.Rating, Overview = subject.Intro, ProductionYear = subject.Year, HomePageUrl = "https://www.douban.com", Genres = subject.Genres.ToArray(), PremiereDate = subject.ScreenTime,
                    };
                    if (!string.IsNullOrEmpty(subject.Imdb)) item.SetProviderId(MetadataProvider.Imdb, await this.CheckNewImdbID(subject.Imdb, cancellationToken).ConfigureAwait(false));
                    var linkedTmdbId = await this.FindTmdbId(seriesName, subject.Imdb, subject.Year, info, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(linkedTmdbId)) {
                        item.SetProviderId(MetadataProvider.Tmdb, linkedTmdbId);
                        await this.TryPopulateTvExternalIdsFromTmdbAsync(item, linkedTmdbId, info, cancellationToken).ConfigureAwait(false);
                    }
                    result.Item = item; result.HasMetadata = true; result.QueriedById = true;
                    subject.LimitDirectorCelebrities.Take(10).ToList().ForEach(c => result.AddPerson(new PersonInfo { Name = c.Name, Type = c.RoleType == PersonType.Director ? PersonKind.Director : PersonKind.Actor, Role = c.Role, ImageUrl = GetLocalProxyImageUrl(new Uri(c.Img, UriKind.Absolute)).ToString(), ProviderIds = new Dictionary<string, string> { { DoubanProviderId, c.Id } } }));
                }
            }
            if (result.HasMetadata && result.Item != null) {
                if (string.IsNullOrEmpty(result.Item.GetProviderId("Tvdb")) && Config.EnableTvdbMatch) {
                    var foundTvdbId = await this.GuessByTvdbAsync(info, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(foundTvdbId)) result.Item.SetProviderId("Tvdb", foundTvdbId);
                }
                float? finalRating = null; var currentSid = result.Item.GetProviderId(DoubanProviderId);
                if (!string.IsNullOrEmpty(currentSid)) {
                    var ds = await this.DoubanApi.GetMovieAsync(currentSid, cancellationToken).ConfigureAwait(false);
                    if (ds != null && ds.Rating > 0) finalRating = ds.Rating;
                }
                if (finalRating == null || finalRating <= 0) {
                    var currentTmdbId = result.Item.GetProviderId(MetadataProvider.Tmdb);
                    if (!string.IsNullOrEmpty(currentTmdbId)) {
                        var ts = await this.TmdbApi.GetSeriesAsync(currentTmdbId.ToInt(), info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                        if (ts != null && ts.VoteAverage > 0) finalRating = (float)Math.Round(ts.VoteAverage, 2);
                    }
                }
                if (finalRating > 0) result.Item.CommunityRating = finalRating;
            }
            return result;
        }
        private async Task<MetadataResult<Series>> GetMetadataByTvdb(string? tvdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            if (string.IsNullOrEmpty(tvdbId) || !int.TryParse(tvdbId, out var id)) return result;
            var tvdbSeries = await this.TvdbApi.GetSeriesAsync(id, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (tvdbSeries == null) return result;
            var name = tvdbSeries.Name; var overview = tvdbSeries.Overview;
            if (tvdbSeries.Translations != null) {
                var targetLang = info.MetadataLanguage ?? "zh-CN";
                var langCode = targetLang.Split('-')[0].ToUpperInvariant();
                var searchLangs = new[] { targetLang, langCode, "ZHO", "CHI", "ZH", "zh-hans", "zh-hant" };
                if (tvdbSeries.Translations.NameTranslations != null) {
                    var trans = tvdbSeries.Translations.NameTranslations.FirstOrDefault(t => t.Language != null && searchLangs.Any(sl => sl.Equals(t.Language, StringComparison.OrdinalIgnoreCase)));
                    if (trans != null && !string.IsNullOrEmpty(trans.Name)) name = trans.Name;
                }
                if (tvdbSeries.Translations.OverviewTranslations != null) {
                    var trans = tvdbSeries.Translations.OverviewTranslations.FirstOrDefault(t => t.Language != null && searchLangs.Any(sl => sl.Equals(t.Language, StringComparison.OrdinalIgnoreCase)));
                    if (trans != null && !string.IsNullOrEmpty(trans.Overview)) overview = trans.Overview;
                }
            }
            var item = new Series { Name = name, Overview = overview, PremiereDate = DateTime.TryParse(tvdbSeries.FirstAired, out var d) ? d : null, ProductionYear = DateTime.TryParse(tvdbSeries.FirstAired, out var d2) ? d2.Year : null, Genres = tvdbSeries.Genres?.Select(g => g.Name).ToArray(), Studios = tvdbSeries.Companies?.Where(c => c.CompanyType?.Name == "Network" || c.CompanyType?.Name == "Production Company").Select(c => c.Name).ToArray() };
            item.SetProviderId("Tvdb", tvdbId); item.SetProviderId(MetaSharkPlugin.ProviderId, $"tvdb_{tvdbId}");
            result.Item = item; result.HasMetadata = true; result.QueriedById = true; return result;
        }
        private async Task<MetadataResult<Series>> GetMetadataByTmdb(string? tmdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            if (string.IsNullOrEmpty(tmdbId)) return result;
            var tvShow = await this.TmdbApi.GetSeriesAsync(tmdbId.ToInt(), info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (tvShow == null) return result;
            result.Item = this.MapTvShowToSeries(tvShow, info.MetadataCountryCode);
            result.ResultLanguage = info.MetadataLanguage ?? tvShow.OriginalLanguage;
            foreach (var person in this.GetPersons(tvShow)) result.AddPerson(person);
            result.HasMetadata = true; result.QueriedById = true; return result;
        }
        private async Task<string?> FindTmdbId(string name, string imdb, int? year, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(imdb)) {
                var tid = await this.GetTmdbIdByImdbAsync(imdb, info.MetadataLanguage, info, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(tid)) return tid;
            }
            if (!string.IsNullOrEmpty(name) && year > 0) return await this.GuestByTmdbAsync(name, year, info, cancellationToken).ConfigureAwait(false);
            return null;
        }
        private Series MapTvShowToSeries(TvShow seriesResult, string preferredCountryCode)
        {
            var series = new Series { Name = seriesResult.Name, OriginalTitle = seriesResult.OriginalName, CommunityRating = (float)Math.Round(seriesResult.VoteAverage, 2), Overview = seriesResult.Overview, Studios = seriesResult.Networks?.Select(i => i.Name).ToArray(), Genres = seriesResult.Genres?.Select(i => i.Name).ToArray(), HomePageUrl = seriesResult.Homepage, PremiereDate = seriesResult.FirstAirDate, ProductionYear = seriesResult.FirstAirDate?.Year, Status = string.Equals(seriesResult.Status, "Ended", StringComparison.OrdinalIgnoreCase) ? SeriesStatus.Ended : SeriesStatus.Continuing, EndDate = string.Equals(seriesResult.Status, "Ended", StringComparison.OrdinalIgnoreCase) ? seriesResult.LastAirDate : null };
            series.SetProviderId(MetadataProvider.Tmdb, seriesResult.Id.ToString(CultureInfo.InvariantCulture));
            if (seriesResult.ExternalIds != null) {
                if (!string.IsNullOrWhiteSpace(seriesResult.ExternalIds.ImdbId)) series.SetProviderId(MetadataProvider.Imdb, seriesResult.ExternalIds.ImdbId);
                if (!string.IsNullOrWhiteSpace(seriesResult.ExternalIds.TvdbId)) series.SetProviderId("Tvdb", seriesResult.ExternalIds.TvdbId);
            }
            series.SetProviderId(MetaSharkPlugin.ProviderId, $"{MetaSource.Tmdb}_{seriesResult.Id}");
            return series;
        }
        private async Task TryPopulateTvExternalIdsFromTmdbAsync(Series series, string tmdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (!int.TryParse(tmdbId, out var tid)) return;
            var tvShow = await this.TmdbApi.GetSeriesAsync(tid, info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (tvShow?.ExternalIds == null) return;
            if (string.IsNullOrWhiteSpace(series.GetProviderId(MetadataProvider.Imdb)) && !string.IsNullOrWhiteSpace(tvShow.ExternalIds.ImdbId)) series.SetProviderId(MetadataProvider.Imdb, tvShow.ExternalIds.ImdbId);
            if (!string.IsNullOrWhiteSpace(tvShow.ExternalIds.TvdbId)) series.SetProviderId("Tvdb", tvShow.ExternalIds.TvdbId);
        }
        private IEnumerable<PersonInfo> GetPersons(TvShow seriesResult)
        {
            if (seriesResult.Credits?.Cast != null) {
                foreach (var actor in seriesResult.Credits.Cast.OrderBy(a => a.Order).Take(10))
                    yield return new PersonInfo { Name = actor.Name.Trim(), Role = actor.Character, Type = PersonKind.Actor, SortOrder = actor.Order, ImageUrl = this.TmdbApi.GetProfileUrl(actor.ProfilePath)?.ToString() };
            }
            if (seriesResult.Credits?.Crew != null) {
                foreach (var person in seriesResult.Credits.Crew) {
                    var type = MapCrewToPersonType(person);
                    if (string.IsNullOrEmpty(type)) continue;
                    yield return new PersonInfo { Name = person.Name.Trim(), Role = person.Job, Type = type == PersonType.Director ? PersonKind.Director : (type == PersonType.Producer ? PersonKind.Producer : PersonKind.Actor), ImageUrl = this.TmdbApi.GetPosterUrl(person.ProfilePath)?.ToString() };
                }
            }
        }
        private async Task<string?> GetTmdbOfficialRating(ItemLookupInfo info, string tmdbId, CancellationToken cancellationToken)
        {
            var tvShow = await this.TmdbApi.GetSeriesAsync(tmdbId.ToInt(), info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            if (tvShow == null) return null;
            var ratings = tvShow.ContentRatings.Results ?? new List<ContentRating>();
            return ratings.FirstOrDefault(c => string.Equals(c.Iso_3166_1, info.MetadataCountryCode, StringComparison.OrdinalIgnoreCase))?.Rating 
                ?? ratings.FirstOrDefault(c => string.Equals(c.Iso_3166_1, "US", StringComparison.OrdinalIgnoreCase))?.Rating 
                ?? ratings.FirstOrDefault()?.Rating;
        }
    }
}
