namespace DJL_6sToolbox.Desktop.Models;

public sealed record UpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotes,
    string? CommitSha,
    bool IsSourceUpdateOnly);
