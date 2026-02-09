// <copyright file="ApiController.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Controllers
{
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Plugin.MetaShark.Api;
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Common.Extensions;
    using MediaBrowser.Common.Net;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [AllowAnonymous]
    [Route("/plugin/metashark")]
    public class ApiController : ControllerBase
    {
        private readonly DoubanApi doubanApi;
        private readonly IHttpClientFactory httpClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        public ApiController(IHttpClientFactory httpClientFactory, DoubanApi doubanApi)
        {
            this.httpClientFactory = httpClientFactory;
            this.doubanApi = doubanApi;
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

        public Task<Stream> ProxyImage(System.Uri url)
        {
            throw new System.NotImplementedException();
        }

        private HttpClient GetHttpClient()
        {
            var client = this.httpClientFactory.CreateClient(NamedClient.Default);
            return client;
        }
    }
}
