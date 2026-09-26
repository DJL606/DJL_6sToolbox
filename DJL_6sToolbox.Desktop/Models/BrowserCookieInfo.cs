namespace DJL_6sToolbox.Desktop.Models;

/// <summary>从浏览器提取到的 B 站 Cookie。</summary>
public sealed record BrowserCookieInfo(
    string? BrowserName,
    string? Sessdata,
    string? BiliJct,
    string? DedeUserId,
    string? DedeUserIdCkMd5,
    string? Sid);
