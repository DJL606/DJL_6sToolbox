namespace DJL_6sToolbox.Desktop.Models;

/// <summary>点赞接口返回结果。</summary>
public sealed record LikeResult(
    bool Success,
    int Code,
    string Message,
    bool IsRiskControl);
