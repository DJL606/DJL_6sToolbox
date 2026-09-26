namespace DJL_6sToolbox.Desktop.Models;

public sealed record LoginInfo(
    bool IsLogin,
    long UserId,
    string UserName,
    int Level,
    long CoinCount,
    string Message = "");
