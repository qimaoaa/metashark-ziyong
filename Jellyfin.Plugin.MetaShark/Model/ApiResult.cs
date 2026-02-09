// <copyright file="ApiResult.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaShark.Model;

public class ApiResult
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;

    public ApiResult(int code, string msg = "")
    {
        this.Code = code;
        this.Msg = msg;
    }
}