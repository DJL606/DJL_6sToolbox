using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>
/// 通过 WebView2 运行 Geetest 前端，自动点击 OCR 识别出的坐标，
/// 并获取 Geetest 生成的 validate 结果。
/// </summary>
public sealed class GeetestWebViewAutomation
{
    private readonly CoreWebView2 _webView;

    public GeetestWebViewAutomation(CoreWebView2 webView)
    {
        _webView = webView;
    }

    public async Task LoadCaptchaAsync(string gt, string challenge)
    {
        var html = BuildHtml(gt, challenge);
        var dataUri = "data:text/html;charset=utf-8," + Uri.EscapeDataString(html);
        _webView.Navigate(dataUri);
        await WaitForAsync("window.__captchaReady === true", TimeSpan.FromSeconds(20));
    }

    public async Task<bool> ClickPointsAsync(IReadOnlyList<CaptchaPoint> points, CancellationToken ct = default)
    {
        if (points is null || points.Count == 0)
        {
            return false;
        }

        var script = new StringBuilder(@"
            (function(){
                var container = document.querySelector('.geetest_panel_next, .geetest_wind, .geetest_holder');
                if(!container) return false;
                var rect = container.getBoundingClientRect();
                var points = __POINTS__;
                for(var i=0;i<points.length;i++){
                    var x = rect.left + points[i][0] * rect.width / __IMG_W__;
                    var y = rect.top + points[i][1] * rect.height / __IMG_H__;
                    var el = document.elementFromPoint(x, y);
                    if(el){
                        var evt = new MouseEvent('click', {clientX:x, clientY:y, bubbles:true});
                        el.dispatchEvent(evt);
                    }
                }
                return true;
            })();
        ");

        // 坐标比例先按图片原始尺寸 1:1 处理，后续可根据实际 DOM 修正。
        var js = script.Replace("__POINTS__", JsonSerializer.Serialize(points.Select(p => new[] { p.X, p.Y })))
            .Replace("__IMG_W__", "1")
            .Replace("__IMG_H__", "1")
            .ToString();
        var result = await _webView.ExecuteScriptAsync(js);
        return result == "true";
    }

    public async Task<string?> GetValidateAsync()
    {
        var result = await _webView.ExecuteScriptAsync("JSON.stringify(window.__geetestValidate || null)");
        if (string.IsNullOrWhiteSpace(result) || result == "null")
        {
            return null;
        }

        return result;
    }

    private static string BuildHtml(string gt, string challenge)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<script src='https://static.geetest.com/static/tools/gt.js'></script>
</head>
<body style='margin:0;padding:0;background:#fff'>
<div id='captcha' style='width:100%;height:100%'></div>
<script>
initGeetest({{
    gt: '{gt}',
    challenge: '{challenge}',
    product: 'embed',
    width: '100%'
}}, function(captchaObj) {{
    captchaObj.appendTo('#captcha');
    captchaObj.onReady(function() {{ window.__captchaReady = true; }});
    captchaObj.onSuccess(function() {{
        window.__geetestValidate = captchaObj.getValidate();
    }});
}});
</script>
</body>
</html>";
    }

    private async Task WaitForAsync(string script, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var result = await _webView.ExecuteScriptAsync(script);
            if (result == "true")
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Geetest 验证码加载超时。");
    }
}
