# jellyfin-plugin-metashark

用 AI 改的 jellyfin-plugin-metashark。

## 功能

- 从豆瓣获取电影/剧集元数据，TMDb 用于补全剧集信息和图片
- 支持 TMDb 剧集组映射
- 可选写入 TMDb 关键词标签
- TMDb 特典按季内顺序插入（基于 AirsBefore/AirsAfter）

## 配置

- EnableSpecialsWithinSeasons: 启用时，TMDb 提供插入位置时，特典按季内顺序显示
- EnableTmdbTags: 启用时写入 TMDb 关键词标签
- TmdbEpisodeGroupMap: TMDb 剧集组映射（每行一条：tmdbId=episodeGroupId）

## 构建

```sh
dotnet restore
dotnet publish --configuration=Release Jellyfin.Plugin.MetaShark/Jellyfin.Plugin.MetaShark.csproj
```

