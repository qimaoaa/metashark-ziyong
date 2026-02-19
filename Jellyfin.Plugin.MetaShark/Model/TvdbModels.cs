namespace Jellyfin.Plugin.MetaShark.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
public class TvdbSearchResult
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("tvdb_id")] public string TvdbId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("image_url")] public System.Uri? ImageUrl { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("year")] public string? Year { get; set; }
}
public class TvdbSeries
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("firstAired")] public string? FirstAired { get; set; }
    [JsonPropertyName("score")] public float? Score { get; set; }
    [JsonPropertyName("genres")] public Collection<TvdbGenre>? Genres { get; } = new();
    [JsonPropertyName("companies")] public Collection<TvdbCompany>? Companies { get; } = new();
    [JsonPropertyName("translations")] public TvdbTranslations? Translations { get; set; }
    [JsonPropertyName("artworks")] public Collection<TvdbArtwork>? Artworks { get; } = new();
    [JsonPropertyName("seasons")] public Collection<TvdbSeasonRecord>? Seasons { get; } = new();
}
public class TvdbGenre { [JsonPropertyName("name")] public string Name { get; set; } = string.Empty; }
public class TvdbCompany { [JsonPropertyName("name")] public string Name { get; set; } = string.Empty; [JsonPropertyName("companyType")] public TvdbCompanyType? CompanyType { get; set; } }
public class TvdbCompanyType { [JsonPropertyName("name")] public string Name { get; set; } = string.Empty; }
public class TvdbTranslations { [JsonPropertyName("nameTranslations")] public List<TvdbTranslation>? NameTranslations { get; set; } [JsonPropertyName("overviewTranslations")] public List<TvdbTranslation>? OverviewTranslations { get; set; } }
public class TvdbTranslation { [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("overview")] public string? Overview { get; set; } [JsonPropertyName("language")] public string? Language { get; set; } }
public class TvdbArtwork { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("image")] public string? Image { get; set; } [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; } [JsonPropertyName("type")] public int Type { get; set; } [JsonPropertyName("language")] public string? Language { get; set; } [JsonPropertyName("seasonId")] public int? SeasonId { get; set; } }
public class TvdbSeasonRecord { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("number")] public int Number { get; set; } [JsonPropertyName("type")] public TvdbSeasonType? Type { get; set; } [JsonPropertyName("image")] public string? Image { get; set; } }
public class TvdbSeasonType { [JsonPropertyName("type")] public string? Type { get; set; } }
public class TvdbEpisodeGroup { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("seriesId")] public int SeriesId { get; set; } [JsonPropertyName("episodes")] public Collection<TvdbGroupEpisode>? Episodes { get; } = new(); }
public class TvdbGroupEpisode { [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("seasonNumber")] public int? SeasonNumber { get; set; } [JsonPropertyName("number")] public int? Number { get; set; } }
