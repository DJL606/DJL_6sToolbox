# Bilibili 自动化工具

这是一个基于Python的B站自动化工具，可以实现以下功能：

- 使用Cookie登录B站账号
- 搜索指定关键词的视频
- 自动为搜索结果的视频点赞
- 自动在视频下发表评论（支持固定评论或随机评论）
- 自动举报违规视频（支持多种举报原因）

## 安装依赖

```bash
pip install -r requirements.txt
```

## 配置

在使用前，需要配置B站账号的Cookie信息。有四种方式：

### 方式1：使用Cookie获取工具（推荐）

我们提供了一个简单的工具来获取Cookie：

1. 运行Cookie获取工具：
   ```bash
   python get_cookies.py
   ```
2. 选择获取Cookie的方式：
   - **手动输入Cookie（推荐）**：可以直接粘贴完整Cookie字符串或分别输入各个关键Cookie值
   - 从浏览器自动提取Cookie：需要安装额外的库，且在某些环境下可能不稳定
3. 按照提示操作，工具会自动保存Cookie到相应的文件中

### 方式2：使用命令行快速设置工具

如果你已经知道自己的Cookie值，可以使用命令行工具快速设置：

```bash
# 使用完整cookie字符串
python set_cookie.py --cookie "你的完整cookie字符串"

# 或者分别设置各个字段
python set_cookie.py --sessdata "你的SESSDATA" --bili_jct "你的bili_jct" --userid "你的DedeUserID"
```

更多选项可以查看帮助：
```bash
python set_cookie.py --help
```

### 方式3：使用.env文件

1. 复制`.env.example`文件并重命名为`.env`
2. 编辑`.env`文件，填入你的B站Cookie信息

```
BILIBILI_COOKIE=SESSDATA=your_sessdata; bili_jct=your_csrf_token; DedeUserID=your_user_id
```

### 方式4：使用cookies.json文件

1. 复制`cookies.json.example`文件并重命名为`cookies.json`
2. 编辑`cookies.json`文件，填入你的B站Cookie信息

```json
{
    "SESSDATA": "your_sessdata",
    "bili_jct": "your_csrf_token",
    "DedeUserID": "your_user_id",
    "DedeUserID__ckMd5": "your_ckmd5",
    "sid": "your_sid"
}
```

## 如何手动获取Cookie

如果需要手动获取Cookie，请按照以下步骤操作：

1. 登录B站网页版 (https://www.bilibili.com/)
2. 打开浏览器开发者工具 (F12 或右键 -> 检查)
3. 切换到"网络"(Network)选项卡
4. 刷新页面
5. 在请求列表中找到任意一个B站的请求
6. 在请求头(Headers)中找到"Cookie"字段
7. 复制整个Cookie字符串

**必要的Cookie字段**：
- SESSDATA：会话数据，用于身份验证
- bili_jct：CSRF令牌，用于点赞、评论等操作
- DedeUserID：用户ID

**注意：Cookie中包含你的账号登录凭证，请勿分享给他人！**

## 使用方法

运行脚本：

```bash
python bilibili_auto.py
```

程序会提供两种操作模式：

### 1. 搜索视频并点赞/评论

按照提示输入：
1. 要搜索的关键词
2. 要点赞的视频数量
3. 是否需要评论视频
   - 如选择评论，可以设置固定评论或随机评论
   - 随机评论模式下可以输入多条评论，系统会随机选择一条发送

### 2. 搜索视频并举报

按照提示输入：
1. 要搜索的关键词
2. 要举报的视频数量
3. 举报原因（从列表中选择）
4. 详细说明（可选）
5. 确认操作

程序会自动搜索视频并执行相应操作。

## 举报原因说明

举报功能支持以下原因：
1. 违法违禁
2. 色情低俗
3. 赌博诈骗
4. 人身攻击
5. 侵犯隐私
6. 垃圾广告
7. 引战
8. 剧透
9. 政治敏感
10. 其他

**重要提示：请仅举报真正违反社区规范的内容，不要滥用举报功能。**

## 注意事项

- 请合理使用此工具，避免频繁操作导致账号被风控
- 脚本中已添加随机延时，以模拟真实用户行为
- Cookie有效期通常为一个月，过期后需要重新获取
- 评论内容请遵守B站社区规范，避免发布违规内容
- 举报功能应当谨慎使用，仅用于举报真正违规的内容 