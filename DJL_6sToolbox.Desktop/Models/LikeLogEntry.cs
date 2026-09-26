namespace DJL_6sToolbox.Desktop.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record LikeLogEntry(DateTime Time, LogLevel Level, string Message);
