// <copyright file="ParseNameResult.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using MediaBrowser.Controller.Providers;

    public class ParseNameResult : ItemLookupInfo
    {
        private string animeType = string.Empty;

        public string? ChineseName { get; set; }

        /// <summary>
        /// Gets or sets 可能会解析不对，最好只在动画SP中才使用.
        /// </summary>
        public string? EpisodeName { get; set; }

        public string AnimeType
        {
            get
            {
                return this.animeType.ToUpperInvariant();
            }

            set
            {
                this.animeType = value;
            }
        }

        public bool IsSpecial
        {
            get
            {
                return !string.IsNullOrEmpty(this.AnimeType) && string.Equals(this.AnimeType, "SP", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsExtra
        {
            get
            {
                return !string.IsNullOrEmpty(this.AnimeType)
                    && !string.Equals(this.AnimeType, "SP", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(this.AnimeType, "OVA", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(this.AnimeType, "TV", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string? PaddingZeroIndexNumber
        {
            get
            {
                if (!this.IndexNumber.HasValue)
                {
                    return null;
                }

                return this.IndexNumber.Value.ToString("00", CultureInfo.InvariantCulture);
            }
        }

        public string ExtraName
        {
            get
            {
                if (this.IndexNumber.HasValue)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0} {1}", this.AnimeType, this.PaddingZeroIndexNumber);
                }
                else
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}", this.AnimeType);
                }
            }
        }

        public string SpecialName
        {
            get
            {
                if (!string.IsNullOrEmpty(this.EpisodeName) && this.IndexNumber.HasValue)
                {
                    return $"{this.EpisodeName} {this.IndexNumber}";
                }
                else if (!string.IsNullOrEmpty(this.EpisodeName))
                {
                    return this.EpisodeName;
                }

                return this.Name;
            }
        }
    }
}
