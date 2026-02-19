// <copyright file="MovieProvider.cs" company="PlaceholderCompany">
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
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using TMDbLib.Objects.Movies;
    using MetadataProvider = MediaBrowser.Model.Entities.MetadataProvider;
    using Movie = MediaBrowser.Controller.Entities.Movies.Movie;

    public class MovieProvider : BaseProvider, IRemoteMetadataProvider<Movie, MovieInfo>
    {
        public MovieProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IHttpContextAccessor httpContextAccessor, DoubanApi doubanApi, TmdbApi tmdbApi, OmdbApi omdbApi, ImdbApi imdbApi, TvdbApi tvdbApi)
            : base(httpClientFactory, loggerFactory.CreateLogger<MovieProvider>(), libraryManager, httpContextAccessor, doubanApi, tmdbApi, omdbApi, imdbApi, tvdbApi)
        {
        }

        public string Name => MetaSharkPlugin.PluginName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(searchInfo);
            this.Log($"GetSearchResults of [name]: {searchInfo.Name}");
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchInfo.Name))
            {
                return result;
            }

            // 从douban搜索
            var res = await this.DoubanApi.SearchAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
            result.AddRange(res.Take(Configuration.PluginConfiguration.MAXSEARCHRESULT).Select(x =>
            {
                return new RemoteSearchResult
                {
                    // 这里 MetaSharkPlugin.ProviderId 的值做这么复杂，是为了和电视剧保持一致并唯一
                    ProviderIds = new Dictionary<string, string> { { DoubanProviderId, x.Sid }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Douban}_{x.Sid}" } },
                    ImageUrl = this.GetProxyImageUrl(new Uri(x.Img, UriKind.Absolute)).ToString(),
                    ProductionYear = x.Year,
                    Name = x.Name,
                };
            }));

            // 尝试从tmdb搜索
            if (Config.EnableTmdbSearch)
            {
                var tmdbList = await this.TmdbApi.SearchMovieAsync(searchInfo.Name, searchInfo.IndexNumber ?? 0, searchInfo.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                result.AddRange(tmdbList.Take(Configuration.PluginConfiguration.MAXSEARCHRESULT).Select(x =>
                {
                    return new RemoteSearchResult
                    {
                        // 这里 MetaSharkPlugin.ProviderId 的值做这么复杂，是为了和电视剧保持一致并唯一
                        ProviderIds = new Dictionary<string, string> { { MetadataProvider.Tmdb.ToString(), x.Id.ToString(CultureInfo.InvariantCulture) }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Tmdb}_{x.Id}" } },
                        Name = string.Format(CultureInfo.InvariantCulture, "[TMDB]{0}", x.Title ?? x.OriginalTitle),
                        ImageUrl = this.TmdbApi.GetPosterUrl(x.PosterPath)?.ToString(),
                        Overview = x.Overview,
                        ProductionYear = x.ReleaseDate?.Year,
                    };
                }));
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (string.IsNullOrEmpty(info.MetadataLanguage) && !string.IsNullOrEmpty(info.Name) && info.Name.HasChinese())
            {
                info.MetadataLanguage = "zh-CN";
            }

            var fileName = GetOriginalFileName(info);
            var result = new MetadataResult<Movie>();

            var sid = info.GetProviderId(DoubanProviderId);
            var tmdbId = info.GetProviderId(MetadataProvider.Tmdb);
            var metaSource = info.GetMetaSource(MetaSharkPlugin.ProviderId);

            // 注意：会存在元数据有tmdbId，但metaSource没值的情况（之前由TMDB插件刮削导致）
            var hasTmdbMeta = metaSource == MetaSource.Tmdb && !string.IsNullOrEmpty(tmdbId);
            var hasDoubanMeta = metaSource != MetaSource.Tmdb && !string.IsNullOrEmpty(sid);
            this.Log($"GetMovieMetadata of [name]: {info.Name} [fileName]: {fileName} metaSource: {metaSource} EnableTmdb: {Config.EnableTmdb}");
            if (!hasDoubanMeta && !hasTmdbMeta)
            {
                // 自动扫描搜索匹配元数据
                sid = await this.GuessByDoubanAsync(info, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(sid) && Config.EnableTmdbMatch)
                {
                    tmdbId = await this.GuestByTmdbAsync(info, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(tmdbId))
                    {
                        metaSource = MetaSource.Tmdb;
                    }
                }
            }

            if (metaSource != MetaSource.Tmdb && !string.IsNullOrEmpty(sid))
            {
                this.Log($"GetMovieMetadata of douban [sid]: {sid}");
                var subject = await this.DoubanApi.GetMovieAsync(sid, cancellationToken).ConfigureAwait(false);
                if (subject == null)
                {
                    return result;
                }

                subject.Celebrities.Clear();
                foreach (var celebrity in await this.DoubanApi.GetCelebritiesBySidAsync(sid, cancellationToken).ConfigureAwait(false))
                {
                    subject.Celebrities.Add(celebrity);
                }

                var item = new Movie
                {
                    ProviderIds = new Dictionary<string, string> { { DoubanProviderId, subject.Sid }, { MetaSharkPlugin.ProviderId, $"{MetaSource.Douban}_{subject.Sid}" } },
                    Name = subject.Name,
                    OriginalTitle = subject.OriginalName,
                    CommunityRating = subject.Rating,
                    Overview = subject.Intro,
                    ProductionYear = subject.Year,
                    HomePageUrl = "https://www.douban.com",
                    Genres = subject.Genres.ToArray(),
                    PremiereDate = subject.ScreenTime,
                    Tagline = string.Empty,
                };

                // 设置imdb元数据
                if (!string.IsNullOrEmpty(subject.Imdb))
                {
                    var newImdbId = await this.CheckNewImdbID(subject.Imdb, cancellationToken).ConfigureAwait(false);
                    subject.Imdb = newImdbId;
                    item.SetProviderId(MetadataProvider.Imdb, newImdbId);
                }

                // 搜索匹配tmdbId
                var newTmdbId = await this.FindTmdbId(subject.Name, subject.Imdb, subject.Year, info, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(newTmdbId))
                {
                    tmdbId = newTmdbId;
                    item.SetProviderId(MetadataProvider.Tmdb, tmdbId);
                }

                // 通过imdb获取电影分级信息
                if (Config.EnableTmdbOfficialRating && !string.IsNullOrEmpty(tmdbId))
                {
                    var officialRating = await this.GetTmdbOfficialRating(info, tmdbId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(officialRating))
                    {
                        item.OfficialRating = officialRating;
                    }
                }

                result.Item = item;
                result.QueriedById = true;
                result.HasMetadata = true;
                subject.LimitDirectorCelebrities.Take(Configuration.PluginConfiguration.MAXCASTMEMBERS).ToList().ForEach(c => result.AddPerson(new PersonInfo
                {
                    Name = c.Name,
                    Type = c.RoleType == PersonType.Director ? PersonKind.Director : PersonKind.Actor,
                    Role = c.Role,
                    ImageUrl = GetLocalProxyImageUrl(new Uri(c.Img, UriKind.Absolute)).ToString(),
                    ProviderIds = new Dictionary<string, string> { { DoubanProviderId, c.Id } },
                }));
            }
            else if (metaSource == MetaSource.Tmdb && !string.IsNullOrEmpty(tmdbId))
            {
                result = await this.GetMetadataByTmdb(tmdbId, info, cancellationToken).ConfigureAwait(false);
            }

            // --- 统一评分补全逻辑 ---
            if (result.HasMetadata && result.Item != null)
            {
                // 1. 尝试从豆瓣补全评分
                var currentSid = result.Item.GetProviderId(DoubanProviderId);
                if (string.IsNullOrEmpty(currentSid))
                {
                    currentSid = await this.GuessByDoubanAsync(info, cancellationToken).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(currentSid))
                {
                    var doubanSubject = await this.DoubanApi.GetMovieAsync(currentSid, cancellationToken).ConfigureAwait(false);
                    if (doubanSubject != null && doubanSubject.Rating > 0)
                    {
                        this.Log("Preferring Douban rating: {0}", doubanSubject.Rating);
                        result.Item.CommunityRating = doubanSubject.Rating;
                    }
                }

                // 2. 如果豆瓣没分，尝试从 TMDB 补全
                if (result.Item.CommunityRating == null || result.Item.CommunityRating <= 0)
                {
                    var currentTmdbId = result.Item.GetProviderId(MetadataProvider.Tmdb);
                    if (!string.IsNullOrEmpty(currentTmdbId))
                    {
                        var tmdbMovie = await this.TmdbApi.GetMovieAsync(currentTmdbId.ToInt(), info.MetadataLanguage, info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
                        if (tmdbMovie != null && tmdbMovie.VoteAverage > 0)
                        {
                            this.Log("Fallback to TMDB rating: {0}", tmdbMovie.VoteAverage);
                            result.Item.CommunityRating = (float)System.Math.Round(tmdbMovie.VoteAverage, 2);
                        }
                    }
                }
            }

            return result;
        }

        private async Task<MetadataResult<Movie>> GetMetadataByTmdb(string? tmdbId, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>();
            if (string.IsNullOrEmpty(tmdbId))
            {
                return result;
            }

            this.Log($"GetMovieMetadata of tmdb [id]: \"{tmdbId}\"");
            var movie = await this.TmdbApi
                .GetMovieAsync(Convert.ToInt32(tmdbId, CultureInfo.InvariantCulture), info.MetadataLanguage, info.MetadataLanguage, cancellationToken)
                .ConfigureAwait(false);

            if (movie == null)
            {
                return result;
            }

            result = new MetadataResult<Movie>
            {
                Item = this.MapMovieToMovie(movie, info.MetadataCountryCode),
                ResultLanguage = info.MetadataLanguage ?? movie.OriginalLanguage,
            };

            foreach (var person in this.GetPersons(movie))
            {
                result.AddPerson(person);
            }

            result.QueriedById = true;
            result.HasMetadata = true;
            return result;
        }

        private async Task<string?> FindTmdbId(string name, string imdb, int? year, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            // 通过imdb获取TMDB id
            if (!string.IsNullOrEmpty(imdb))
            {
                var tmdbId = await this.GetTmdbIdByImdbAsync(imdb, info.MetadataLanguage, info, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(tmdbId))
                {
                    return tmdbId;
                }
                else
                {
                    this.Log($"Can not found tmdb [id] by imdb id: \"{imdb}\"");
                }
            }

            // 尝试通过搜索匹配获取tmdbId
            if (!string.IsNullOrEmpty(name) && year != null && year > 0)
            {
                var tmdbId = await this.GuestByTmdbAsync(name, year, info, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(tmdbId))
                {
                    return tmdbId;
                }
                else
                {
                    this.Log($"Can not found tmdb [id] by name: \"{name}\" and year: \"{year}\"");
                }
            }

            return null;
        }

        private string? GetTmdbOfficialRatingByData(TMDbLib.Objects.Movies.Movie? movie, string preferredCountryCode)
        {
            _ = this.Logger;
            if (movie != null)
            {
                var releases = movie.ReleaseDates.Results ?? new List<ReleaseDatesContainer>();

                var ourRelease = releases.FirstOrDefault(c => string.Equals(c.Iso_3166_1, preferredCountryCode, StringComparison.OrdinalIgnoreCase));
                var usRelease = releases.FirstOrDefault(c => string.Equals(c.Iso_3166_1, "US", StringComparison.OrdinalIgnoreCase));
                var minimumRelease = releases.FirstOrDefault();

                if (ourRelease != null && ourRelease.ReleaseDates.Count > 0)
                {
                    return ourRelease.ReleaseDates[0].Certification;
                }
                else if (usRelease != null && usRelease.ReleaseDates.Count > 0)
                {
                    return usRelease.ReleaseDates[0].Certification;
                }
                else if (minimumRelease != null && minimumRelease.ReleaseDates.Count > 0)
                {
                    return minimumRelease.ReleaseDates[0].Certification;
                }
            }

            return null;
        }

        private Movie MapMovieToMovie(TMDbLib.Objects.Movies.Movie movieResult, string preferredCountryCode)
        {
            var movie = new Movie
            {
                Name = movieResult.Title,
                OriginalTitle = movieResult.OriginalTitle,
            };

            movie.SetProviderId(MetadataProvider.Tmdb, movieResult.Id.ToString(CultureInfo.InvariantCulture));

            movie.CommunityRating = (float)System.Math.Round(movieResult.VoteAverage, 2);

            movie.Overview = movieResult.Overview;

            if (movieResult.ProductionCompanies != null)
            {
                movie.Studios = movieResult.ProductionCompanies.Select(i => i.Name).ToArray();
            }

            if (movieResult.Genres != null)
            {
                movie.Genres = movieResult.Genres.Select(i => i.Name).ToArray();
            }

            if (Config.EnableTmdbTags && movieResult.Keywords?.Keywords != null)
            {
                var tagCount = movieResult.Keywords.Keywords.Count;
                for (var i = 0; i < movieResult.Keywords.Keywords.Count; i++)
                {
                    movie.AddTag(movieResult.Keywords.Keywords[i].Name);
                }

                if (tagCount > 0)
                {
                    this.Log("TMDb tags added for movie: id={0} name={1} count={2}", movieResult.Id, movieResult.Title, tagCount);
                }
            }

            movie.HomePageUrl = movieResult.Homepage;

            if (movieResult.Runtime.HasValue)
            {
                movie.RunTimeTicks = TimeSpan.FromMinutes(movieResult.Runtime.Value).Ticks;
            }

            movie.PremiereDate = movieResult.ReleaseDate;
            movie.ProductionYear = movieResult.ReleaseDate?.Year;

            if (!string.IsNullOrWhiteSpace(movieResult.ImdbId))
            {
                movie.SetProviderId(MetadataProvider.Imdb, movieResult.ImdbId);
            }

            if (movieResult.BelongsToCollection != null)
            {
                movie.SetProviderId(MetadataProvider.TmdbCollection, movieResult.BelongsToCollection.Id.ToString(CultureInfo.InvariantCulture));
            }

            movie.SetProviderId(MetaSharkPlugin.ProviderId, $"{MetaSource.Tmdb}_{movieResult.Id}");
            movie.OfficialRating = this.GetTmdbOfficialRatingByData(movieResult, preferredCountryCode);

            return movie;
        }

        private async Task<string?> GetTmdbOfficialRating(ItemLookupInfo info, string tmdbId, CancellationToken cancellationToken)
        {
            var movie = await this.TmdbApi
                            .GetMovieAsync(Convert.ToInt32(tmdbId, CultureInfo.InvariantCulture), info.MetadataLanguage, info.MetadataLanguage, cancellationToken)
                            .ConfigureAwait(false);
            return this.GetTmdbOfficialRatingByData(movie, info.MetadataCountryCode);
        }

        private IEnumerable<PersonInfo> GetPersons(TMDbLib.Objects.Movies.Movie movieResult)
        {
            // 演员
            if (movieResult.Credits?.Cast != null)
            {
                foreach (var actor in movieResult.Credits.Cast.OrderBy(a => a.Order).Take(Configuration.PluginConfiguration.MAXCASTMEMBERS))
                {
                    var personInfo = new PersonInfo
                    {
                        Name = actor.Name.Trim(),
                        Role = actor.Character,
                        Type = PersonKind.Actor,
                        SortOrder = actor.Order,
                    };

                    if (!string.IsNullOrWhiteSpace(actor.ProfilePath))
                    {
                        personInfo.ImageUrl = this.TmdbApi.GetProfileUrl(actor.ProfilePath)?.ToString();
                    }

                    if (actor.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, actor.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    yield return personInfo;
                }
            }

            // 导演
            if (movieResult.Credits?.Crew != null)
            {
                var keepTypes = new[]
                {
                    PersonType.Director,
                    PersonType.Writer,
                    PersonType.Producer,
                };

                foreach (var person in movieResult.Credits.Crew)
                {
                    // Normalize this
                    var type = MapCrewToPersonType(person);

                    if (!keepTypes.Contains(type, StringComparer.OrdinalIgnoreCase)
                        && !keepTypes.Contains(person.Job ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var personInfo = new PersonInfo
                    {
                        Name = person.Name.Trim(),
                        Role = person.Job,
                        Type = type == PersonType.Director ? PersonKind.Director : (type == PersonType.Producer ? PersonKind.Producer : PersonKind.Actor),
                    };

                    if (!string.IsNullOrWhiteSpace(person.ProfilePath))
                    {
                        personInfo.ImageUrl = this.TmdbApi.GetPosterUrl(person.ProfilePath)?.ToString();
                    }

                    if (person.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, person.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    yield return personInfo;
                }
            }
        }
    }
}
