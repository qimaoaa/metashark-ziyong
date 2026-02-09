// <copyright file="DoubanLoginInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text.Json.Serialization;

    public class DoubanLoginInfo
    {
        public string Name { get; set; } = string.Empty;

        public bool IsLogined { get; set; }
    }
}