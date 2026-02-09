// <copyright file="ListExtension.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public static class ListExtension
    {
        public static IEnumerable<(T Item, int Index)> WithIndex<T>(this IEnumerable<T> self)
           => self.Select((item, index) => (Item: item, Index: index));
    }
}
