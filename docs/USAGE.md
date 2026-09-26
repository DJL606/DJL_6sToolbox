# 使用教程 / User Guide

[中文](#中文使用教程) | [English](#english-user-guide)

---

## 中文使用教程

### 1. 运行环境

本项目基于 **C# / WPF / .NET 8**，支持 **Windows 10 / 11**。

- 如果使用单文件自包含版 `DJL_6sToolbox.exe`，无需安装 .NET。
- 如果从源码构建，需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- 人机验证的 OCR 使用内置 Tesseract，中文语言包已随 exe 打包，不需要额外安装 Python。

### 2. 第一次启动

1. 双击 `DJL_6sToolbox.exe`。
2. 程序默认最小化到系统托盘，可在托盘图标上右键操作。
3. 首次启动后，先到 **Cookie** 页配置 B 站 Cookie。

### 3. Cookie 配置

有两种方式：

#### 方式 A：手动粘贴
1. 在浏览器登录 B 站。
2. 按 `F12` 打开开发者工具 → `Application` → `Cookies`。
3. 复制完整 Cookie 字符串。
4. 粘贴到软件 **Cookie** 页，点击 **保存并验证**。

#### 方式 B：自动获取
1. 进入 **Cookie** 页。
2. 点击 **从浏览器自动获取 Cookie**。
3. 如果读取失败，会打开内置登录窗口。
4. 登录成功后自动提取并验证。

验证成功后会显示当前账号昵称和 UID。

### 4. 总览页

总览页包含：

- **运行状态**：当前状态、上次运行时间、累计点赞数。
- **运行日志**：自动点赞相关日志。
- **人机自动验证**：验证码图片、进度、人机验证日志，以及启用开关。

### 5. 自动点赞

进入 **自动点赞** 页：

1. 设置扫描间隔、最大页数、每轮点赞上限。
2. 设置最小/最大点赞间隔（建议不低于 2 秒）。
3. 设置风控冷却时间（默认 30 分钟）。
4. 设置筛选条件：
   - 最小/最大粉丝数
   - 最小/最大等级
   - 标题包含/排除关键词
5. 点击 **启动自动点赞**。
6. 也可以点击 **立即扫描并点赞一次** 做单轮测试。

已点赞视频会记录在本地，不会重复点赞。

### 6. 手机设备环境

进入 **手机设备环境** 区域：

- 每套环境包含：
  - AccessKey
  - DeviceId
  - BuvidFp / Buvid3 / Buvid / Buvid4
  - Build / Channel / DeviceBrand / DeviceModel / OsVersion
  - BiliTicket / BiliTicketV2
  - ADB 序列号（可选）
- 支持新增、删除、选择当前环境。
- 每套环境可设置独立的每轮点赞上限。

> AccessKey 属于账号敏感信息，请勿提交到公开仓库。

### 7. ADB 请求桥（可选）

ADB 桥可以让请求从 Android 模拟器/设备上的 `curl` 发出，使 IP、TLS、UA 更接近真实手机环境。

1. 确保模拟器已开启 ADB 调试。
2. 在 ADB 设备里填写地址，例如：
   ```text
   127.0.0.1:16384
   ```
3. 勾选 **启用 ADB 请求桥（可选）**。
4. 勾选 **ADB 跟随系统切换** 时，切换手机环境会自动使用该环境绑定的 ADB 序列号。
5. 界面会显示 `ADB 状态：在线 / 离线 / 未启用`。
6. 如果设备离线，程序会自动回退到本机 HTTP 请求。

### 8. 人机验证（实验性）

进入总览页的人机验证区域：

1. 勾选 **启用自动人机验证**。
2. 遇到验证码时：
   - 验证码图片会显示在区域中。
   - 进度文本会显示当前识别/点击进度。
   - 人机验证日志会记录每一步。
3. 识别流程：
   - 获取 Geetest v3 点选验证码图片
   - 本地 OCR 识别提示词和候选文字
   - 按提示词顺序计算点击坐标
   - 通过 WebView2 执行自动化点击
   - 获取 `validate` 后继续原请求

> 注意：人机验证属于实验性功能，B 站风控策略可能变化，不保证 100% 成功。
> 本项目不调用任何第三方打码平台。

### 9. 托盘与开机自启

- 最小化后程序常驻系统托盘。
- 托盘菜单可启动/停止自动点赞。
- 在 **关于更新** 页可设置：
  - 开机自启（静默托盘运行）
  - 启动时自动检查更新
  - 运行中防止电脑休眠

### 10. 常见问题

#### 1. 一直返回 `-401`
- `-401` 通常表示风控或非法访问，不一定是登录失效。
- 关闭 **APP 签名**，优先使用纯网页或移动端模式。
- 降低点赞频率，增加随机延时。
- 开启 **ADB 跟随系统切换**，避免手机环境和 ADB 设备不匹配。
- 不要在同一 IP 下快速切换多套设备指纹。

#### 2. ADB 显示离线
- 确认模拟器已启动。
- 确认地址和端口正确。
- 程序会自动尝试 `adb connect`。
- 如果仍离线，检查模拟器 ADB 调试是否开启。

#### 3. 验证码识别失败
- 检查总览页人机验证日志。
- OCR 对图片质量要求较高，可等待验证码刷新后重试。
- 如果失败，程序会跳过当前视频，保持稳定。

#### 4. 滚轮无法滚动
- 这是 WPF + WebView2 的 airspace 问题。
- 当前版本已将 WebView2 与滚动区域做兼容处理。
- 如果仍有问题，可关注仓库后续更新。

### 11. 合规提示

- 本项目仅供学习和个人自动化研究。
- 请遵守 B 站用户协议与社区规范。
- 不要用于刷量、商业推广或恶意行为。
- 使用风险由使用者自行承担。

---

## English User Guide

### 1. Requirements

- Windows 10 / 11
- .NET 8 (only needed when building from source; the self-contained exe does not require it)
- Optional: Android emulator/device with ADB enabled

### 2. First Run

1. Run `DJL_6sToolbox.exe`.
2. The app starts in the system tray.
3. Go to the **Cookie** page and configure your Bilibili Cookie.

### 3. Cookie

- Paste a full Bilibili Cookie and click **Save & Verify**.
- Or use **Auto Fetch Cookie** to read it from a local browser.
- The app stores settings under `%AppData%\DJL_6sToolbox\`.

### 4. Overview Page

The Overview page contains:

- Run status
- Run log
- Human verification panel: CAPTCHA image, progress, CAPTCHA log, enable switch

### 5. Auto Like

1. Open the **Auto Like** page.
2. Configure scan interval, max pages, max likes per run, like delay, and risk-control cooldown.
3. Configure filters: fans, level, title include/exclude keywords.
4. Click **Start** or **Run Once**.

### 6. Phone Profiles

Each phone profile can store:

- AccessKey
- DeviceId
- BuvidFp / Buvid3 / Buvid / Buvid4
- Build / Channel / DeviceBrand / DeviceModel / OsVersion
- BiliTicket / BiliTicketV2
- ADB serial

> AccessKey and Buvid are sensitive; never commit them to a public repository.

### 7. ADB Bridge (Optional)

1. Start your Android emulator with ADB enabled.
2. Add an ADB device, e.g. `127.0.0.1:16384`.
3. Enable **ADB Request Bridge**.
4. Enable **Follow Phone Switch** to bind each phone profile to its own ADB serial.
5. If the ADB device is offline, the app automatically falls back to local HTTP.

### 8. CAPTCHA Assistance (Experimental)

1. Enable **Auto Human Verification** on the Overview page.
2. When a CAPTCHA appears, the app:
   - fetches the Geetest v3 click CAPTCHA image,
   - runs local OCR,
   - computes click coordinates in prompt order,
   - automates clicks via WebView2,
   - retrieves `validate` and continues.
3. No third-party CAPTCHA-solving service is used.

### 9. Tray & Startup

- The app stays in the system tray.
- The tray menu can start/stop auto like.
- The **About / Update** page provides startup-on-boot and update settings.

### 10. Troubleshooting

- **Frequent `-401`**: reduce frequency, avoid App Signature mode, enable ADB follow switch.
- **ADB offline**: start the emulator, verify the port, check ADB debugging.
- **OCR failure**: check the CAPTCHA log; the app skips the current video and continues.
- **Mouse wheel issue**: WebView2 airspace issue; use the latest build.

### 11. Compliance

This project is for learning and personal automation research only.
Please follow Bilibili's terms of service and community rules.
Use at your own risk.
