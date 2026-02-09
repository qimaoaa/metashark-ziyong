// <copyright file="GuessInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class GuessInfo
    {
        public int? EpisodeNumber { get; set; }

        public int? SeasonNumber { get; set; }

        public string? Name { get; set; }
    }
}