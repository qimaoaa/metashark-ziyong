// <copyright file="RegexExtension.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Eventing.Reader;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using StringMetric;

    public static class RegexExtension
    {
        public static string FirstMatchGroup(this Regex reg, string text, string defalutVal = "")
        {
            ArgumentNullException.ThrowIfNull(reg);
            ArgumentNullException.ThrowIfNull(text);
            var match = reg.Match(text);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }

            return defalutVal;
        }
    }
}