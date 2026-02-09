// <copyright file="ProviderIdsExtensions.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Providers
{
    using System;
    using System.Collections.Generic;
    using Jellyfin.Plugin.MetaShark.Model;
    using MediaBrowser.Model.Entities;

    public static class ProviderIdsExtensions
    {
        public static MetaSource GetMetaSource(this IHasProviderIds instance, string name)
        {
            var value = instance.GetProviderId(name);
            return value.ToMetaSource();
        }

        public static void TryGetMetaSource(this Dictionary<string, string> dict, string name, out MetaSource metaSource)
        {
            ArgumentNullException.ThrowIfNull(dict);
            if (dict.TryGetValue(name, out var value))
            {
                metaSource = value.ToMetaSource();
            }
            else
            {
                metaSource = MetaSource.None;
            }
        }
    }
}