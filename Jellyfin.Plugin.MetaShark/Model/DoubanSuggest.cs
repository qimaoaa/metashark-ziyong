// <copyright file="DoubanSuggest.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MetaShark.Core;

namespace Jellyfin.Plugin.MetaShark.Model;

public class DoubanSuggest
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public System.Uri? Url { get; set; }

    [JsonPropertyName("year")]
    public string Year { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    public string Sid
    {
        get
        {
            var regSid = new Regex(@"subject\/(\d+?)\/", RegexOptions.Compiled);
            return (this.Url?.ToString() ?? string.Empty).GetMatchGroup(regSid);
        }
    }
}
