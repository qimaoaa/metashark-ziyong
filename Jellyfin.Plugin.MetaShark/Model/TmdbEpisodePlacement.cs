// <copyright file="TmdbEpisodePlacement.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System.Text.Json.Serialization;

    public class TmdbEpisodePlacement
    {
        [JsonPropertyName("airs_before_season")]
        public int? AirsBeforeSeason { get; set; }

        [JsonPropertyName("airs_before_episode")]
        public int? AirsBeforeEpisode { get; set; }

        [JsonPropertyName("airs_after_season")]
        public int? AirsAfterSeason { get; set; }

        [JsonPropertyName("airs_after_episode")]
        public int? AirsAfterEpisode { get; set; }
    }
}
