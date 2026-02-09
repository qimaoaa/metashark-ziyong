// <copyright file="ElementExtension.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MetaShark.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using AngleSharp.Dom;

    public static class ElementExtension
    {
        public static string? GetText(this IElement el, string css)
        {
            ArgumentNullException.ThrowIfNull(el);
            var node = el.QuerySelector(css);
            if (node != null)
            {
                return node.Text().Trim();
            }

            return null;
        }

        public static string? GetHtml(this IElement el, string css)
        {
            ArgumentNullException.ThrowIfNull(el);
            var node = el.QuerySelector(css);
            if (node != null)
            {
                return node.Html().Trim();
            }

            return null;
        }

        public static string GetTextOrDefault(this IElement el, string css, string defaultVal = "")
        {
            ArgumentNullException.ThrowIfNull(el);
            var node = el.QuerySelector(css);
            if (node != null)
            {
                return node.Text().Trim();
            }

            return defaultVal;
        }

        public static string? GetAttr(this IElement el, string css, string attr)
        {
            ArgumentNullException.ThrowIfNull(el);
            var node = el.QuerySelector(css);
            if (node != null)
            {
                var attrVal = node.GetAttribute(attr);
                return attrVal != null ? attrVal.Trim() : null;
            }

            return null;
        }

        public static string? GetAttrOrDefault(this IElement el, string css, string attr, string defaultVal = "")
        {
            ArgumentNullException.ThrowIfNull(el);
            var node = el.QuerySelector(css);
            if (node != null)
            {
                var attrVal = node.GetAttribute(attr);
                return attrVal != null ? attrVal.Trim() : defaultVal;
            }

            return defaultVal;
        }
    }
}
