// <copyright file="SpecialAirsPlacement.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers
{
    internal sealed class SpecialAirsPlacement
    {
        public int? AirsBeforeSeasonNumber { get; set; }

        public int? AirsBeforeEpisodeNumber { get; set; }

        public int? AirsAfterSeasonNumber { get; set; }
    }
}
