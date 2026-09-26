#!/usr/bin/env python
# -*- coding: utf-8 -*-

import os
import json
import time
import random
import requests
from dotenv import load_dotenv

class BilibiliAuto:
    """
    Bilibili自动化工具：登录、搜索视频、点赞、评论、举报
    """
    
    def __init__(self):
        # 加载环境变量
        load_dotenv()
        
        # 基础URL
        self.base_url = "https://api.bilibili.com"
        self.search_url = "https://api.bilibili.com/x/web-interface/wbi/search/type"
        self.like_url = "https://api.bilibili.com/x/web-interface/archive/like"
        self.comment_url = "https://api.bilibili.com/x/v2/reply/add"
        self.report_url = "https://api.bilibili.com/x/web-interface/archive/report"
        
        # 用户代理
        self.user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
        
        # 从环境变量或配置文件加载cookie
        self.cookies = self._load_cookies()
        
        # 会话对象
        self.session = requests.Session()
        self.session.headers.update({
            "User-Agent": self.user_agent,
            "Referer": "https://www.bilibili.com",
            "Origin": "https://www.bilibili.com"
        })
        
        # 设置cookies
        if self.cookies:
            self._set_cookies()
    
    def _load_cookies(self):
        """从环境变量或cookie文件加载cookies"""
        # 优先从环境变量加载
        cookie_str = os.getenv("BILIBILI_COOKIE")
        if cookie_str:
            return self._parse_cookie_string(cookie_str)
        
        # 从文件加载
        cookie_file = os.getenv("COOKIE_FILE", "cookies.json")
        if os.path.exists(cookie_file):
            try:
                with open(cookie_file, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception as e:
                print(f"加载cookie文件失败: {e}")
        
        return None
    
    def _parse_cookie_string(self, cookie_str):
        """解析cookie字符串为字典"""
        cookies = {}
        for item in cookie_str.split("; "):
            if "=" in item:
                key, value = item.split("=", 1)
                cookies[key] = value
        return cookies
    
    def _set_cookies(self):
        """设置cookies到会话"""
        if isinstance(self.cookies, dict):
            self.session.cookies.update(self.cookies)
        else:
            print("Cookie格式错误，请检查")
    
    def check_login_status(self):
        """检查登录状态"""
        try:
            nav_url = "https://api.bilibili.com/x/web-interface/nav"
            response = self.session.get(nav_url)
            data = response.json()
            
            if data["code"] == 0 and data["data"]["isLogin"]:
                print(f"登录成功! 用户名: {data['data']['uname']}")
                return True
            else:
                print(f"登录失败: {data}")
                return False
        except Exception as e:
            print(f"检查登录状态时出错: {e}")
            return False
    
    def search_videos(self, keyword, page=1, page_size=20):
        """
        搜索视频
        
        Args:
            keyword: 搜索关键词
            page: 页码，从1开始
            page_size: 每页结果数
            
        Returns:
            视频列表
        """
        try:
            params = {
                "keyword": keyword,
                "search_type": "video",
                "page": page,
                "page_size": page_size,
                "order": "totalrank",  # 综合排序
                "platform": "pc"
            }
            
            response = self.session.get(self.search_url, params=params)
            data = response.json()
            
            if data["code"] == 0:
                videos = data["data"]["result"]
                print(f"找到 {len(videos)} 个视频")
                return videos
            else:
                print(f"搜索失败: {data}")
                return []
        except Exception as e:
            print(f"搜索视频时出错: {e}")
            return []
    
    def like_video(self, aid):
        """
        给视频点赞
        
        Args:
            aid: 视频av号
            
        Returns:
            是否点赞成功
        """
        try:
            csrf = self.cookies.get("bili_jct")
            if not csrf:
                print("未找到CSRF令牌，无法点赞")
                return False
            
            data = {
                "aid": aid,
                "like": 1,  # 1表示点赞，2表示取消点赞
                "csrf": csrf
            }
            
            response = self.session.post(self.like_url, data=data)
            result = response.json()
            
            if result["code"] == 0:
                print(f"成功点赞视频 av{aid}")
                return True
            else:
                print(f"点赞失败: {result}")
                return False
        except Exception as e:
            print(f"点赞视频时出错: {e}")
            return False
    
    def comment_video(self, aid, message):
        """
        给视频发表评论
        
        Args:
            aid: 视频av号
            message: 评论内容
            
        Returns:
            是否评论成功
        """
        try:
            csrf = self.cookies.get("bili_jct")
            if not csrf:
                print("未找到CSRF令牌，无法评论")
                return False
            
            data = {
                "oid": aid,  # 视频av号
                "type": 1,   # 1表示视频评论
                "message": message,
                "plat": 1,   # 平台，1为PC
                "csrf": csrf
            }
            
            response = self.session.post(self.comment_url, data=data)
            result = response.json()
            
            if result["code"] == 0:
                print(f"成功评论视频 av{aid}")
                return True
            else:
                print(f"评论失败: {result}")
                return False
        except Exception as e:
            print(f"评论视频时出错: {e}")
            return False
    
    def report_video(self, aid, reason_type, detail=""):
        """
        举报视频
        
        Args:
            aid: 视频av号
            reason_type: 举报原因类型ID
            detail: 详细说明（可选）
            
        Returns:
            是否举报成功
        """
        try:
            csrf = self.cookies.get("bili_jct")
            if not csrf:
                print("未找到CSRF令牌，无法举报")
                return False
            
            data = {
                "aid": aid,
                "reason": reason_type,
                "detail": detail,
                "csrf": csrf
            }
            
            response = self.session.post(self.report_url, data=data)
            result = response.json()
            
            if result["code"] == 0:
                print(f"成功举报视频 av{aid}")
                return True
            else:
                print(f"举报失败: {result}")
                return False
        except Exception as e:
            print(f"举报视频时出错: {e}")
            return False
    
    def get_report_reasons(self):
        """
        获取举报原因列表
        
        Returns:
            举报原因字典，键为ID，值为原因描述
        """
        # B站举报原因类型ID及描述
        return {
            1: "违法违禁",
            2: "色情低俗",
            3: "赌博诈骗",
            4: "人身攻击",
            5: "侵犯隐私",
            6: "垃圾广告",
            7: "引战",
            8: "剧透",
            9: "政治敏感",
            10: "其他"
        }
    
    def auto_like_videos(self, keyword, count=5, comment=None):
        """
        自动搜索并点赞视频
        
        Args:
            keyword: 搜索关键词
            count: 要点赞的视频数量
            comment: 要发表的评论，如果为None则不评论
        """
        videos = self.search_videos(keyword)
        
        if not videos:
            print("未找到视频，无法点赞")
            return
        
        liked_count = 0
        for video in videos:
            if liked_count >= count:
                break
            
            aid = video.get("aid")
            if not aid:
                continue
            
            title = video.get("title", "").replace("<em class=\"keyword\">", "").replace("</em>", "")
            print(f"正在点赞: {title} (av{aid})")
            
            if self.like_video(aid):
                liked_count += 1
                
                # 如果提供了评论内容，则发表评论
                if comment:
                    # 生成随机评论内容（如果提供了多个评论模板）
                    comment_text = comment
                    if isinstance(comment, list) and comment:
                        comment_text = random.choice(comment)
                    
                    print(f"正在评论: {title}")
                    self.comment_video(aid, comment_text)
            
            # 随机延迟，避免操作过于频繁
            delay = random.uniform(2, 5)
            time.sleep(delay)
        
        print(f"完成点赞任务，共点赞 {liked_count} 个视频")
    
    def auto_report_videos(self, keyword, count=5, reason_type=None, detail=""):
        """
        自动搜索并举报视频
        
        Args:
            keyword: 搜索关键词
            count: 要举报的视频数量
            reason_type: 举报原因类型ID
            detail: 详细说明（可选）
        """
        if reason_type is None:
            print("未指定举报原因，无法举报")
            return
        
        videos = self.search_videos(keyword)
        
        if not videos:
            print("未找到视频，无法举报")
            return
        
        reported_count = 0
        for video in videos:
            if reported_count >= count:
                break
            
            aid = video.get("aid")
            if not aid:
                continue
            
            title = video.get("title", "").replace("<em class=\"keyword\">", "").replace("</em>", "")
            print(f"正在举报: {title} (av{aid})")
            
            if self.report_video(aid, reason_type, detail):
                reported_count += 1
            
            # 随机延迟，避免操作过于频繁
            delay = random.uniform(3, 7)  # 举报操作延时更长，降低风险
            time.sleep(delay)
        
        print(f"完成举报任务，共举报 {reported_count} 个视频")


def main():
    # 创建B站自动化实例
    bilibili = BilibiliAuto()
    
    # 检查登录状态
    if not bilibili.check_login_status():
        print("请检查cookie是否有效")
        return
    
    print("\n请选择要执行的操作：")
    print("1. 搜索视频并点赞/评论")
    print("2. 搜索视频并举报")
    
    operation = input("请输入选项(1-2): ")
    
    if operation == "1":
        # 点赞/评论功能
        keyword = input("请输入要搜索的关键词: ")
        count = input("请输入要点赞的视频数量(默认5个): ")
        
        try:
            count = int(count) if count.strip() else 5
        except ValueError:
            count = 5
        
        # 询问是否需要评论
        need_comment = input("是否需要评论视频？(y/n): ").lower() == 'y'
        comment = None
        
        if need_comment:
            comment_type = input("选择评论方式：1.固定评论 2.随机评论: ")
            
            if comment_type == "1":
                comment = input("请输入评论内容: ")
            elif comment_type == "2":
                comments = []
                print("请输入多个评论内容，每行一个，输入空行结束:")
                while True:
                    line = input()
                    if not line:
                        break
                    comments.append(line)
                
                if comments:
                    comment = comments
                else:
                    print("未输入任何评论，将不进行评论")
        
        # 执行自动点赞和评论
        bilibili.auto_like_videos(keyword, count, comment)
    
    elif operation == "2":
        # 举报功能
        keyword = input("请输入要搜索的关键词: ")
        count = input("请输入要举报的视频数量(默认5个): ")
        
        try:
            count = int(count) if count.strip() else 5
        except ValueError:
            count = 5
        
        # 显示举报原因列表
        reasons = bilibili.get_report_reasons()
        print("\n举报原因列表：")
        for reason_id, reason_desc in reasons.items():
            print(f"{reason_id}. {reason_desc}")
        
        # 获取举报原因
        reason_type = input("请选择举报原因(1-10): ")
        try:
            reason_type = int(reason_type)
            if reason_type not in reasons:
                print("无效的举报原因，将使用'其他'原因")
                reason_type = 10
        except ValueError:
            print("无效的举报原因，将使用'其他'原因")
            reason_type = 10
        
        # 获取详细说明
        detail = input("请输入详细说明(可选): ")
        
        # 确认举报
        confirm = input(f"\n您将举报包含关键词 '{keyword}' 的 {count} 个视频，原因是 '{reasons[reason_type]}'。确认操作？(y/n): ")
        if confirm.lower() != 'y':
            print("操作已取消")
            return
        
        # 执行自动举报
        bilibili.auto_report_videos(keyword, count, reason_type, detail)
    
    else:
        print("无效的选项")


if __name__ == "__main__":
    main() 