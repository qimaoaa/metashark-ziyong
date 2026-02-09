// <copyright file="DoubanCelebrity.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Model
{
    using System;
    using System.Text.Json.Serialization;
    using MediaBrowser.Model.Entities;

    public class DoubanCelebrity
    {
        private string roleType = string.Empty;

        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Img { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public string Intro { get; set; } = string.Empty;

        public string Gender { get; set; } = string.Empty;

        public string Constellation { get; set; } = string.Empty;

        public string Birthdate { get; set; } = string.Empty;

        public string Enddate { get; set; } = string.Empty;

        public string Birthplace { get; set; } = string.Empty;

        public string NickName { get; set; } = string.Empty;

        public string EnglishName { get; set; } = string.Empty;

        public string Imdb { get; set; } = string.Empty;

        public string Site { get; set; } = string.Empty;

        public string RoleType
        {
            get
            {
                if (string.IsNullOrEmpty(this.roleType))
                {
                    return this.Role.Contains("导演", StringComparison.Ordinal) ? PersonType.Director : PersonType.Actor;
                }

                return this.roleType.Contains("导演", StringComparison.Ordinal) ? PersonType.Director : PersonType.Actor;
            }

            set
            {
                this.roleType = value;
            }
        }

        public string? DisplayOriginalName
        {
            get
            {
                // 外国人才显示英文名
                if (this.Name.Contains('·', StringComparison.Ordinal) && !this.Birthplace.Contains("中国", StringComparison.Ordinal))
                {
                    return this.EnglishName;
                }

                return null;
            }
        }

        [JsonIgnore]
        public string ImgMiddle
        {
            get
            {
                return this.Img.Replace("/raw/", "/m/", StringComparison.Ordinal)
                    .Replace("/s_ratio_poster/", "/m/", StringComparison.Ordinal);
            }
        }
    }
}
