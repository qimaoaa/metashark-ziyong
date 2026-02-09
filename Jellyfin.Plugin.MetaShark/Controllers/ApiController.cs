// <copyright file="ApiController.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Enums;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Common.Extensions;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.IO;
    using MediaBrowser.Model.Providers;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    [ApiController]
    [AllowAnonymous]
    [Route("/plugin/metashark")]
    public class ApiController : ControllerBase
    {
        private readonly DoubanApi doubanApi;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILibraryManager libraryManager;
        private readonly IProviderManager providerManager;
        private readonly IFileSystem fileSystem;
        private readonly ILogger<ApiController> logger;

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
            this.httpClientFactory = httpClientFactory;
            this.doubanApi = doubanApi;
            this.libraryManager = libraryManager;
            this.providerManager = providerManager;
            this.fileSystem = fileSystem;
            this.logger = logger;
        }

        /// <summary>
        /// 代理访问图片.
        /// </summary>
        /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
        [Route("proxy/image")]
        [HttpGet]
        public async Task<Stream> ProxyImage(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ResourceNotFoundException();
            }

            HttpResponseMessage response;
            var httpClient = this.GetHttpClient();
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, url))
            {
                requestMessage.Headers.Add("User-Agent", DoubanApi.HTTPUSERAGENT);
                requestMessage.Headers.Add("Referer", DoubanApi.HTTPREFERER);

                response = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            }

            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            this.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType != null)
            {
                this.Response.ContentType = response.Content.Headers.ContentType.ToString();
            }

            this.Response.ContentLength = response.Content.Headers.ContentLength;

            foreach (var header in response.Headers)
            {
                this.Response.Headers[header.Key] = header.Value.ToArray();
            }

            return stream;
        }

        /// <summary>
        /// 检查豆瓣cookie是否失效.
        /// </summary>
        /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
        [Route("douban/checklogin")]
        [HttpGet]
        public async Task<ApiResult> CheckDoubanLogin()
        {
            var loginInfo = await this.doubanApi.GetLoginInfoAsync(CancellationToken.None).ConfigureAwait(false);
            return new ApiResult(loginInfo.IsLogined ? 1 : 0, loginInfo.Name);
        }

        /// <summary>
        /// Refresh series metadata for mapped TMDB episode groups.
        /// </summary>
        [Route("tmdb/refresh-series")]
        [HttpPost]
        public ApiResult RefreshSeriesByEpisodeGroupMap()
        {
            var mapping = MetaSharkPlugin.Instance?.Configuration.TmdbEpisodeGroupMap ?? string.Empty;
            var tmdbIds = GetMappedSeriesIds(mapping);
            if (tmdbIds.Count == 0)
            {
                return new ApiResult(0, "No mapped series ids.");
            }

            var items = this.libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                IsVirtualItem = false,
                IsMissing = false,
                Recursive = true,
                HasTmdbId = true,
            });

            var refreshOptions = new MetadataRefreshOptions(new DirectoryService(this.fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
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
                    this.logger.LogWarning("Skip refresh for series with empty Id. Name: {Name}", item.Name);
                    continue;
                }

                this.providerManager.QueueRefresh(item.Id, refreshOptions, RefreshPriority.High);
                queued++;
            }

            this.logger.LogInformation("Queued refresh for {Count} series from episode group map.", queued);
            return new ApiResult(1, $"Queued {queued} series refresh(es).");
        }

        private HttpClient GetHttpClient()
        {
            var client = this.httpClientFactory.CreateClient(NamedClient.Default);
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
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
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