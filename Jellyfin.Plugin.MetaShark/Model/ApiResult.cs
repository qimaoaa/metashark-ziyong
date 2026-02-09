// <copyright file="ApiResult.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model;

using System.Text.Json.Serialization;

public class ApiResult
{
    public ApiResult(int code, string msg = "")
    {
        this.Code = code;
        this.Msg = msg;
    }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
}