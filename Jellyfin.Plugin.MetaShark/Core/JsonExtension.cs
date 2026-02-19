// <copyright file="JsonExtension.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using AngleSharp.Dom;

    public static class JsonExtension
    {
        public static string ToJson(this object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            return JsonSerializer.Serialize(obj);
        }
    }
}
