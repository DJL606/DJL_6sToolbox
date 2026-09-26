namespace DJL_6sToolbox.Desktop.Models;

/// <summary>B站视频摘要。</summary>
public sealed record VideoInfo(
    long Aid,
    string Bvid,
    string Title,
    string AuthorName,
    long AuthorMid,
    long PublishTime,
    string CoverUrl,
    string Partition,
    long Duration);
