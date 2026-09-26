using System.Net.Http;
using System.Text.Json;
using Tesseract;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>验证码点击坐标。</summary>
public sealed record CaptchaPoint(int X, int Y);

/// <summary>Geetest 点选验证码信息。</summary>
public sealed record GeetestCaptchaInfo(string Gt, string Challenge, string PicUrl, string Raw);

/// <summary>
/// Geetest v3 点选验证码本地识别服务。
/// 负责获取验证码图片、OCR 识别、提示词匹配、输出点击坐标。
/// </summary>
public sealed class GeetestCaptchaService
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly CaptchaOcrService _ocr = new();

    public async Task<GeetestCaptchaInfo?> FetchInfoAsync(string gt, string challenge, CancellationToken ct = default)
    {
        var url = "https://api.geetest.com/get.php" +
                  $"?is_next=true&type=click&gt={Uri.EscapeDataString(gt)}" +
                  $"&challenge={Uri.EscapeDataString(challenge)}" +
                  "&lang=zh-cn&https=false&protocol=https%3A%2F%2F&offline=false" +
                  "&product=embed&api_server=api.geetest.com&isPC=true&autoReset=true&width=100%25" +
                  $"&callback=geetest_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var text = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        var json = UnwrapJsonp(text);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "success" ||
            !root.TryGetProperty("data", out var data))
        {
            return null;
        }

        var pic = data.TryGetProperty("pic", out var picProp) ? picProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(pic))
        {
            return null;
        }

        var picUrl = pic.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pic
            : "https://static.geetest.com" + pic;

        return new GeetestCaptchaInfo(gt, challenge, picUrl, text);
    }

    public async Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 识别验证码图片，返回按提示词顺序排列的点击坐标。
    /// 目前采用启发式分区：上方文字视为提示词，下方文字视为候选字。
    /// </summary>
    public async Task<IReadOnlyList<CaptchaPoint>?> SolveImageAsync(byte[] image, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
        {
            return null;
        }

        var words = await _ocr.RecognizeAsync(image, ct).ConfigureAwait(false);
        if (words.Count < 2)
        {
            return null;
        }

        int imageHeight;
        using (var pix = Pix.LoadFromMemory(image))
        {
            imageHeight = pix.Height;
        }

        var promptWords = words
            .Where(w => w.Y < imageHeight * 0.35)
            .OrderBy(w => w.X)
            .ToList();
        var candidateWords = words
            .Where(w => w.Y >= imageHeight * 0.35)
            .ToList();

        if (promptWords.Count == 0)
        {
            // 如果分区失败，退化为把所有词按“上到下、左到右”当作提示词，其余为候选。
            promptWords = words.Take(Math.Min(3, words.Count)).ToList();
            candidateWords = words.Skip(promptWords.Count).ToList();
        }

        if (promptWords.Count == 0 || candidateWords.Count == 0)
        {
            return null;
        }

        var points = new List<CaptchaPoint>();
        foreach (var prompt in promptWords)
        {
            var best = candidateWords
                .OrderByDescending(c => Similarity(prompt.Text, c.Text))
                .FirstOrDefault(c => Similarity(prompt.Text, c.Text) > 0.3);
            if (best is null)
            {
                continue;
            }

            points.Add(new CaptchaPoint(best.X + best.Width / 2, best.Y + best.Height / 2));
        }

        return points.Count > 0 ? points : null;
    }

    public async Task<IReadOnlyList<CaptchaPoint>?> SolveAsync(string gt, string challenge, CancellationToken ct = default)
    {
        var info = await FetchInfoAsync(gt, challenge, ct).ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        var image = await DownloadImageAsync(info.PicUrl, ct).ConfigureAwait(false);
        if (image is null)
        {
            return null;
        }

        try
        {
            return await SolveImageAsync(image, ct).ConfigureAwait(false);
        }
        finally
        {
            // 图片缓存及时释放，不落盘。
        }
    }

    private static string UnwrapJsonp(string text)
    {
        var start = text.IndexOf('(');
        var end = text.LastIndexOf(')');
        if (start >= 0 && end > start)
        {
            return text[(start + 1)..end];
        }

        return text;
    }

    private static double Similarity(string a, string b)
    {
        a = (a ?? string.Empty).Trim();
        b = (b ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
            b.Contains(a, StringComparison.OrdinalIgnoreCase))
        {
            return 0.85;
        }

        var common = a.Count(ch => b.Contains(ch, StringComparison.OrdinalIgnoreCase));
        return (double)common / Math.Max(a.Length, b.Length);
    }

    public void Dispose() => _http.Dispose();
}
