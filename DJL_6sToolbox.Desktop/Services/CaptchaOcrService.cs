using System.IO;
using System.Reflection;
using Tesseract;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>OCR 识别结果中的单个文字/词语。</summary>
public sealed record OcrWord(string Text, int X, int Y, int Width, int Height);

/// <summary>
/// 基于 Tesseract 的本地 OCR 服务，用于识别 B 站点选验证码图片。
/// 中文语言包作为嵌入资源随 exe 打包，首次运行释放到缓存目录。
/// </summary>
public sealed class CaptchaOcrService
{
    private readonly object _initLock = new();
    private bool _initialized;

    public void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            AppPaths.EnsureCreated();
            var target = Path.Combine(AppPaths.TessDataDirectory, "chi_sim.traineddata");
            if (!File.Exists(target))
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("tessdata.chi_sim.traineddata");
                if (stream is null)
                {
                    throw new InvalidOperationException("未找到内置中文 OCR 语言包。");
                }

                using var file = File.Create(target);
                stream.CopyTo(file);
            }

            _initialized = true;
        }
    }

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(byte[] image, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            EnsureInitialized();
            return RecognizeCore(image);
        }, ct).ConfigureAwait(false);
    }

    private IReadOnlyList<OcrWord> RecognizeCore(byte[] image)
    {
        var result = new List<OcrWord>();
        using var engine = new TesseractEngine(AppPaths.TessDataDirectory, "chi_sim", EngineMode.Default);
        using var pix = Pix.LoadFromMemory(image);
        using var page = engine.Process(pix);
        using var iter = page.GetIterator();
        do
        {
            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box) &&
                iter.GetText(PageIteratorLevel.Word) is { } text &&
                !string.IsNullOrWhiteSpace(text))
            {
                result.Add(new OcrWord(text.Trim(), box.X1, box.Y1, box.Width, box.Height));
            }
        } while (iter.Next(PageIteratorLevel.Word));

        return result;
    }
}
