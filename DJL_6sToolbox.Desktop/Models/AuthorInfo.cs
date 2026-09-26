namespace DJL_6sToolbox.Desktop.Models;

/// <summary>UP 主信息（用于筛选粉丝数和等级）。</summary>
public sealed record AuthorInfo(
    long Mid,
    string Name,
    int Level,
    long Fans);
