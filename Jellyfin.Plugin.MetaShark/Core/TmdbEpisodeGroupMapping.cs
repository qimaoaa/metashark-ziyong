using System;

namespace Jellyfin.Plugin.MetaShark.Core
{
    public static class TmdbEpisodeGroupMapping
    {
        public static bool TryGetGroupId(string? mapping, string? tmdbSeriesId, out string groupId)
        {
            groupId = string.Empty;
            if (string.IsNullOrWhiteSpace(mapping) || string.IsNullOrWhiteSpace(tmdbSeriesId))
            {
                return false;
            }

            var lines = mapping.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var parts = trimmed.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (string.Equals(key, tmdbSeriesId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    groupId = value;
                    return true;
                }
            }

            return false;
        }
    }
}
