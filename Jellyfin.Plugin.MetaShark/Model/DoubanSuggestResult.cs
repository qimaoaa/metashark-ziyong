// <copyright file="DoubanSuggestResult.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaShark.Model;

public class DoubanSuggestResult
{
    [JsonPropertyName("cards")]
    public Collection<DoubanSuggest> Cards { get; } = new();
}
