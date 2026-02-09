// <copyright file="DoubanPhoto.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    public class DoubanPhoto
    {
        public string Id { get; set; } = string.Empty;

        public string Small { get; set; } = string.Empty;

        public string Medium { get; set; } = string.Empty;

        public string Large { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets 原始图片url，必须带referer访问.
        /// </summary>
        public string Raw { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public int? Width { get; set; }

        public int? Height { get; set; }
    }
}
