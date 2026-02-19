// <copyright file="MetaSourceExtensions.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System;

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

            if (str.StartsWith("tvdb", StringComparison.OrdinalIgnoreCase))
            {
                return MetaSource.Tvdb;
            }

            return MetaSource.None;
        }
    }
}
