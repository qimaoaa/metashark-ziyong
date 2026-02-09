using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Net;
using Jellyfin.Plugin.MetaShark.Api;
using Jellyfin.Plugin.MetaShark.Model;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaShark.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("/plugin/metashark")]
    public class ApiController : ControllerBase
    {
        private readonly DoubanApi _doubanApi;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<ApiController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        public ApiController(
            IHttpClientFactory httpClientFactory,
            DoubanApi doubanApi,
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILogger<ApiController> logger)
        {
            this._httpClientFactory = httpClientFactory;
            this._doubanApi = doubanApi;
            this._libraryManager = libraryManager;
            this._providerManager = providerManager;
            this._fileSystem = fileSystem;
            this._logger = logger;
        }


        /// <summary>
        /// 代理访问图片.
        /// </summary>
        [Route("proxy/image")]
        [HttpGet]
        public async Task<Stream> ProxyImage(string url)
        {

            if (string.IsNullOrEmpty(url))
            {
                throw new ResourceNotFoundException();
            }

            HttpResponseMessage response;
            var httpClient = GetHttpClient();
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, url))
            {
                requestMessage.Headers.Add("User-Agent", DoubanApi.HTTP_USER_AGENT);
                requestMessage.Headers.Add("Referer", DoubanApi.HTTP_REFERER);

                response = await httpClient.SendAsync(requestMessage);
            }
            var stream = await response.Content.ReadAsStreamAsync();

            Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType != null)
            {
                Response.ContentType = response.Content.Headers.ContentType.ToString();
            }
            Response.ContentLength = response.Content.Headers.ContentLength;

            foreach (var header in response.Headers)
            {
                Response.Headers.Add(header.Key, header.Value.First());
            }

            return stream;
        }

        /// <summary>
        /// 检查豆瓣cookie是否失效.
        /// </summary>
        [Route("douban/checklogin")]
        [HttpGet]
        public async Task<ApiResult> CheckDoubanLogin()
        {
            var loginInfo = await this._doubanApi.GetLoginInfoAsync(CancellationToken.None).ConfigureAwait(false);
            return new ApiResult(loginInfo.IsLogined ? 1 : 0, loginInfo.Name);
        }

        /// <summary>
        /// Refresh series metadata for mapped TMDB episode groups.
        /// </summary>
        [Route("tmdb/refresh-series")]
        [HttpPost]
        public ApiResult RefreshSeriesByEpisodeGroupMap()
        {
            var mapping = Plugin.Instance?.Configuration.TmdbEpisodeGroupMap ?? string.Empty;
            var tmdbIds = GetMappedSeriesIds(mapping);
            if (tmdbIds.Count == 0)
            {
                return new ApiResult(0, "No mapped series ids.");
            }

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                IsVirtualItem = false,
                IsMissing = false,
                Recursive = true,
                HasTmdbId = true
            });

            var refreshOptions = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false,
            };

            var queued = 0;
            foreach (var item in items)
            {
                if (!item.ProviderIds.TryGetValue(MediaBrowser.Model.Entities.MetadataProvider.Tmdb.ToString(), out var tmdbId))
                {
                    continue;
                }

                if (!tmdbIds.Contains(tmdbId))
                {
                    continue;
                }

                if (item.Id == Guid.Empty)
                {
                    _logger.LogWarning("Skip refresh for series with empty Id. Name: {Name}", item.Name);
                    continue;
                }

                _providerManager.QueueRefresh(item.Id, refreshOptions, RefreshPriority.High);
                queued++;
            }

            _logger.LogInformation("Queued refresh for {Count} series from episode group map.", queued);
            return new ApiResult(1, $"Queued {queued} series refresh(es).");
        }


        private HttpClient GetHttpClient()
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            return client;
        }

        private static HashSet<string> GetMappedSeriesIds(string mapping)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(mapping))
            {
                return result;
            }

            var lines = mapping.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var parts = trimmed.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var seriesId = parts[0].Trim();
                if (!string.IsNullOrWhiteSpace(seriesId))
                {
                    result.Add(seriesId);
                }
            }

            return result;
        }
    }
}
