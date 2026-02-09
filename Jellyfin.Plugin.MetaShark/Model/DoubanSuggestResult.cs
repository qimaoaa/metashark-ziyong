// <copyright file="DoubanSuggestResult.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

public class DoubanSuggestResult
{
    [JsonPropertyName("cards")]
    public Collection<DoubanSuggest> Cards { get; } = new();
}
