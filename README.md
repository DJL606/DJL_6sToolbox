# DJL_6's Toolbox / DJL_6's 工具箱

[中文](#中文说明) | [English](#english)

---

## 中文说明

📖 **详细使用教程：[docs/USAGE.md](docs/USAGE.md)**

**DJL_6's 工具箱** 是一个基于 **C# / WPF / .NET 8** 的 Windows 桌面工具，用于 B 站自动化场景：
自动扫描视频、点赞、ADB 设备桥接、手机环境模拟，以及本地人机验证辅助。

> 本项目仅供学习与个人自动化研究使用，请遵守 B 站社区规范与用户协议。
> 运行环境：Windows 10 / 11。

### 功能特性

- 现代化深色 UI、自定义标题栏、亚克力/毛玻璃效果
- 系统托盘常驻，支持静默启动
- Cookie 管理、自动获取与登录验证
- 自动点赞
  - 扫描全站最新视频
  - 按 UP 主粉丝数、等级、标题关键词筛选
  - 已点赞 ID 本地去重
  - 可配置点赞间隔、风控冷却
  - 支持“纯网页 / 移动端竖屏 / APP 签名”三种请求方式
  - 失败时自动切换模式，并支持切回纯网页
- 多手机设备环境
  - 可配置多套完整手机环境
  - 支持定时轮换、每套环境独立每轮上限
- ADB 请求桥（可选）
  - 通过 Android 模拟器/设备上的 `curl` 发起请求
  - 自动检测 ADB 设备在线状态
  - 设备不可用时自动回退本机 HTTP
- 人机验证辅助（实验性）
  - 支持 Geetest v3 点选验证码图片获取
  - 本地 OCR 识别（Tesseract，中文语言包内嵌）
  - 总览页预留验证码图片、识别进度与人机验证日志展示（实验性）
  - 不调用任何第三方打码平台
  - 自动触发流程仍在持续完善，可能无法保证每次通过
- 日志自动滚动，避免占用过多磁盘

### 直接使用

1. 安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（若使用单文件自包含版则不需要）。
2. 从 [Releases](https://github.com/DJL606/DJL_6sToolbox/releases) 下载 `DJL_6sToolbox.exe`。
3. 双击运行。

应用数据保存在：

```text
%AppData%\DJL_6sToolbox\
```

- `settings.json`：设置
- `liked_aids.json`：已点赞视频 ID
- `logs\`：运行日志
- `tessdata\`：OCR 语言包缓存

### 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
# 构建单文件 exe
.\build.cmd

# 构建并打包 zip
.\package.ps1
```

输出：

```text
dist\DJL_6sToolbox.exe
```

### 项目结构

```text
DJL_6sToolbox.Desktop/
├─ App.xaml / App.xaml.cs          # 启动、单实例、托盘生命周期
├─ MainWindow.xaml(.cs)            # 主窗口与页面切换
├─ Themes/Theme.xaml               # 全局样式
├─ Models/                         # 设置、视频、日志、设备等模型
├─ Services/
│  ├─ BilibiliClient.cs            # B 站 API 客户端
│  ├─ LikeService.cs               # 自动点赞调度
│  ├─ AdbRequestService.cs         # ADB 请求桥
│  ├─ CaptchaOcrService.cs         # 本地 OCR 识别
│  ├─ GeetestCaptchaService.cs     # Geetest 验证码处理
│  ├─ GeetestWebViewAutomation.cs  # WebView2 自动化
│  ├─ SettingsStore.cs             # 设置持久化
│  ├─ TrayService.cs               # 系统托盘
│  └─ ...
└─ ViewModels/                     # MVVM 命令与属性
docs/                              # 技术文档（含详细使用教程）
legacy/                             # 旧 Python 参考项目（已脱敏）
third_party/                        # 第三方参考代码与许可证说明
DJL_6sToolbox.Desktop/tessdata/     # OCR 语言包（内嵌资源）
```

### 安全与合规

- Cookie、AccessKey、Buvid 等敏感数据只保存在用户本机 `%AppData%\DJL_6sToolbox\`，不会自动上传到本仓库；但程序需要联网向 B 站发送请求。
- 请勿将个人 Cookie/Token 提交到公开仓库。
- 自动点赞、人机验证辅助等功能可能违反平台规则，使用风险自负。
- 请合理设置频率，避免对平台造成压力。

### 许可证

本项目使用 **Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)** 许可证。
禁止商业用途，转载请注明作者与来源。

- 许可证全文：[LICENSE](LICENSE)
- 协议说明：<https://creativecommons.org/licenses/by-nc/4.0/>
- 第三方组件（如 `third_party/gcc15-bilibili-captcha`）保留其原始 MIT 许可证，详见 [third_party/README.md](third_party/README.md)。

---

## English

📖 **Detailed user guide: [docs/USAGE.md](docs/USAGE.md)**

**DJL_6's Toolbox** is a Windows desktop application built with **C# / WPF / .NET 8** for Bilibili automation:
automatic video scanning, liking, ADB device bridging, mobile environment simulation, and local CAPTCHA assistance.

> This project is for learning and personal automation research only. Please follow Bilibili's community rules and terms of service.
> Platform: Windows 10 / 11.

### Features

- Modern dark UI with custom title bar and acrylic blur
- System tray support and silent startup
- Cookie management, auto-fetch, and login validation
- Automatic liking
  - Scan the latest videos across Bilibili
  - Filter by uploader fans, level, and title keywords
  - Locally de-duplicate liked videos
  - Configurable like interval and risk-control cooldown
  - Three request modes: Web, Mobile Portrait, and App Signature
  - Automatic mode fallback and fallback back to Web
- Multiple phone profiles
  - Multiple complete mobile environments
  - Timed rotation and per-profile like limits
- Optional ADB request bridge
  - Send requests via `curl` on an Android emulator/device
  - Automatic ADB device online detection
  - Fallback to local HTTP when ADB is unavailable
- CAPTCHA assistance (experimental)
  - Fetch Geetest v3 click CAPTCHA images
  - Local OCR via Tesseract with embedded Chinese language data
  - Overview page reserves CAPTCHA image, progress, and CAPTCHA log display (experimental)
  - No third-party CAPTCHA solving service is used
  - Automatic trigger integration is still being improved and may not always pass
- Log rotation to avoid excessive disk usage

### Usage

1. Install [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if needed.
2. Download `DJL_6sToolbox.exe` from [Releases](https://github.com/DJL606/DJL_6sToolbox/releases).
3. Run it.

Application data is stored at:

```text
%AppData%\DJL_6sToolbox\
```

- `settings.json` – settings
- `liked_aids.json` – liked video IDs
- `logs\` – logs
- `tessdata\` – OCR language data cache

### Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
.\build.cmd
.\package.ps1
```

Output:

```text
dist\DJL_6sToolbox.exe
```

### Project Layout

```text
DJL_6sToolbox.Desktop/
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml(.cs)
├─ Themes/
├─ Models/
├─ Services/
│  ├─ BilibiliClient.cs
│  ├─ LikeService.cs
│  ├─ AdbRequestService.cs
│  ├─ CaptchaOcrService.cs
│  ├─ GeetestCaptchaService.cs
│  ├─ GeetestWebViewAutomation.cs
│  └─ ...
└─ ViewModels/
docs/
legacy/
third_party/
DJL_6sToolbox.Desktop/tessdata/
```

### Security & Compliance

- Sensitive data such as cookies, AccessKeys, and Buvids stays locally under `%AppData%\DJL_6sToolbox\` and is not automatically uploaded to this repository; however, the app does need network access to send requests to Bilibili.
- Never commit personal cookies or tokens.
- Automated liking and CAPTCHA assistance may violate platform rules. Use at your own risk.
- Keep request rates reasonable.

### License

Licensed under the **Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)** license.
Commercial use is prohibited. Attribution is required.

- Full license: [LICENSE](LICENSE)
- Human-readable summary: <https://creativecommons.org/licenses/by-nc/4.0/>
- Third-party components (e.g. `third_party/gcc15-bilibili-captcha`) retain their original MIT license; see [third_party/README.md](third_party/README.md).
