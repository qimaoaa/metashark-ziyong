// <copyright file="MetaSource.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public enum MetaSource
    {
        Douban,
        Tmdb,
        None,
    }

    public static class MetaSourceExtensions
    {
        public static MetaSource ToMetaSource(this string? str)
        {
            if (str == null)
            {
                return MetaSource.None;
            }

            if (str.StartsWith("douban", StringComparison.OrdinalIgnoreCase))
            {
                return MetaSource.Douban;
            }

            if (str.StartsWith("tmdb", StringComparison.OrdinalIgnoreCase))
            {
                return MetaSource.Tmdb;
            }

            return MetaSource.None;
        }
    }
}
