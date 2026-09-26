#!/usr/bin/env python
# -*- coding: utf-8 -*-

"""
Bilibili Cookie提取工具

这个脚本帮助用户获取B站的Cookie，并保存为程序可用的格式。
支持手动输入或从浏览器中自动提取。

使用方法：
1. 运行此脚本
2. 选择获取Cookie的方式（手动输入或自动提取）
3. 按照提示操作
4. Cookie将被保存到指定的文件中

注意：自动提取功能需要安装browser-cookie3库
pip install browser-cookie3
"""

import os
import json
import sys
import re

# 尝试导入browser_cookie3，如果失败也不影响手动输入功能
try:
    import browser_cookie3
    BROWSER_COOKIE3_AVAILABLE = True
except ImportError:
    BROWSER_COOKIE3_AVAILABLE = False

def get_bilibili_cookies_from_browser(browser_type):
    """从指定浏览器中提取B站Cookie"""
    if not BROWSER_COOKIE3_AVAILABLE:
        print("未安装browser-cookie3库，无法使用自动提取功能")
        print("请安装：pip install browser-cookie3")
        return None
    
    try:
        if browser_type == "chrome":
            cookies = browser_cookie3.chrome(domain_name=".bilibili.com")
        elif browser_type == "firefox":
            cookies = browser_cookie3.firefox(domain_name=".bilibili.com")
        elif browser_type == "edge":
            cookies = browser_cookie3.edge(domain_name=".bilibili.com")
        elif browser_type == "opera":
            cookies = browser_cookie3.opera(domain_name=".bilibili.com")
        elif browser_type == "brave":
            cookies = browser_cookie3.brave(domain_name=".bilibili.com")
        else:
            print(f"不支持的浏览器类型: {browser_type}")
            return None
        
        # 提取需要的Cookie
        cookie_dict = {}
        for cookie in cookies:
            if cookie.name in ["SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "sid"]:
                cookie_dict[cookie.name] = cookie.value
        
        # 检查是否获取到必要的Cookie
        required_cookies = ["SESSDATA", "bili_jct", "DedeUserID"]
        missing_cookies = [name for name in required_cookies if name not in cookie_dict]
        
        if missing_cookies:
            print(f"警告：缺少必要的Cookie: {', '.join(missing_cookies)}")
            print("请确保你已经登录B站网站")
            return None
        
        return cookie_dict
    
    except Exception as e:
        print(f"提取Cookie时出错: {e}")
        print("可能的原因：")
        print("1. 浏览器正在运行，请关闭所有浏览器窗口后重试")
        print("2. 浏览器配置文件被加密或损坏")
        print("3. 系统权限问题")
        return None

def parse_cookie_string(cookie_str):
    """解析cookie字符串为字典"""
    cookies = {}
    for item in cookie_str.split("; "):
        if "=" in item:
            key, value = item.split("=", 1)
            cookies[key] = value
    return cookies

def get_bilibili_cookies_manual():
    """手动输入B站Cookie"""
    print("\n请选择输入方式：")
    print("1. 输入完整Cookie字符串")
    print("2. 分别输入各个关键Cookie值")
    
    choice = input("请输入选项(1-2): ")
    
    if choice == "1":
        print("\n请从浏览器中复制完整的Cookie字符串")
        print("获取方法：")
        print("1. 登录B站网页版 (https://www.bilibili.com/)")
        print("2. 按F12打开开发者工具")
        print("3. 切换到'网络'(Network)选项卡")
        print("4. 刷新页面")
        print("5. 在请求列表中找到任意一个B站的请求")
        print("6. 在请求头(Headers)中找到'Cookie'字段")
        print("7. 复制整个Cookie字符串")
        
        cookie_str = input("\n请粘贴Cookie字符串: ").strip()
        if not cookie_str:
            print("Cookie字符串不能为空")
            return None
        
        cookies = parse_cookie_string(cookie_str)
        
        # 检查是否包含必要的Cookie
        required_cookies = ["SESSDATA", "bili_jct", "DedeUserID"]
        missing_cookies = [name for name in required_cookies if name not in cookies]
        
        if missing_cookies:
            print(f"警告：缺少必要的Cookie: {', '.join(missing_cookies)}")
            print("请确保复制了完整的Cookie字符串")
            
            # 尝试从字符串中提取关键Cookie
            for required in missing_cookies:
                match = re.search(f"{required}=([^;]+)", cookie_str)
                if match:
                    cookies[required] = match.group(1)
            
            # 再次检查
            missing_cookies = [name for name in required_cookies if name not in cookies]
            if missing_cookies:
                print(f"仍然缺少必要的Cookie: {', '.join(missing_cookies)}")
                
                # 询问是否手动输入缺失的Cookie
                if input("是否手动输入缺失的Cookie? (y/n): ").lower() == 'y':
                    for name in missing_cookies:
                        value = input(f"请输入 {name}: ").strip()
                        if value:
                            cookies[name] = value
                else:
                    return None
        
        return cookies
    
    elif choice == "2":
        cookies = {}
        print("\n请分别输入以下Cookie值（从浏览器开发者工具中获取）：")
        
        cookies["SESSDATA"] = input("SESSDATA: ").strip()
        cookies["bili_jct"] = input("bili_jct: ").strip()
        cookies["DedeUserID"] = input("DedeUserID: ").strip()
        
        # 可选Cookie
        dedemd5 = input("DedeUserID__ckMd5 (可选): ").strip()
        if dedemd5:
            cookies["DedeUserID__ckMd5"] = dedemd5
        
        sid = input("sid (可选): ").strip()
        if sid:
            cookies["sid"] = sid
        
        # 检查必要的Cookie是否为空
        if not cookies["SESSDATA"] or not cookies["bili_jct"] or not cookies["DedeUserID"]:
            print("必要的Cookie不能为空")
            return None
        
        return cookies
    
    else:
        print("无效的选项")
        return None

def save_cookies_to_file(cookies, filename="cookies.json"):
    """将Cookie保存到文件"""
    try:
        with open(filename, "w", encoding="utf-8") as f:
            json.dump(cookies, f, indent=4)
        print(f"Cookie已保存到 {filename}")
        return True
    except Exception as e:
        print(f"保存Cookie时出错: {e}")
        return False

def save_cookies_to_env(cookies, filename=".env"):
    """将Cookie保存到.env文件"""
    try:
        # 构建Cookie字符串
        cookie_str = "; ".join([f"{key}={value}" for key, value in cookies.items()])
        
        # 检查.env文件是否存在
        if os.path.exists(filename):
            # 读取现有内容
            with open(filename, "r", encoding="utf-8") as f:
                lines = f.readlines()
            
            # 查找并替换BILIBILI_COOKIE行
            cookie_line_found = False
            for i, line in enumerate(lines):
                if line.startswith("BILIBILI_COOKIE="):
                    lines[i] = f"BILIBILI_COOKIE={cookie_str}\n"
                    cookie_line_found = True
                    break
            
            # 如果没有找到BILIBILI_COOKIE行，则添加
            if not cookie_line_found:
                lines.append(f"BILIBILI_COOKIE={cookie_str}\n")
            
            # 写回文件
            with open(filename, "w", encoding="utf-8") as f:
                f.writelines(lines)
        else:
            # 创建新文件
            with open(filename, "w", encoding="utf-8") as f:
                f.write(f"BILIBILI_COOKIE={cookie_str}\n")
        
        print(f"Cookie已保存到 {filename}")
        return True
    except Exception as e:
        print(f"保存Cookie到.env文件时出错: {e}")
        return False

def main():
    print("=" * 50)
    print("Bilibili Cookie获取工具")
    print("=" * 50)
    print("请选择获取Cookie的方式：")
    print("1. 手动输入Cookie (推荐)")
    print("2. 从浏览器自动提取Cookie")
    
    method_choice = input("请输入选项(1-2): ")
    
    cookies = None
    
    if method_choice == "1":
        cookies = get_bilibili_cookies_manual()
    elif method_choice == "2":
        if not BROWSER_COOKIE3_AVAILABLE:
            print("未安装browser-cookie3库，无法使用自动提取功能")
            print("请安装：pip install browser-cookie3")
            print("或选择手动输入Cookie")
            return
        
        print("\n请选择你使用的浏览器：")
        print("1. Chrome")
        print("2. Firefox")
        print("3. Edge")
        print("4. Opera")
        print("5. Brave")
        
        browser_choice = input("请输入选项(1-5): ")
        
        browser_map = {
            "1": "chrome",
            "2": "firefox",
            "3": "edge",
            "4": "opera",
            "5": "brave"
        }
        
        if browser_choice not in browser_map:
            print("无效的选项")
            return
        
        browser_type = browser_map[browser_choice]
        print(f"正在从 {browser_type} 中提取B站Cookie...")
        
        cookies = get_bilibili_cookies_from_browser(browser_type)
    else:
        print("无效的选项")
        return
    
    if not cookies:
        print("获取Cookie失败，请重试")
        return
    
    print("\n成功获取Cookie!")
    print("Cookie内容:")
    for key, value in cookies.items():
        # 只显示部分值，保护隐私
        masked_value = value[:3] + "*" * (len(value) - 6) + value[-3:] if len(value) > 6 else "***"
        print(f"  {key}: {masked_value}")
    
    print("\n请选择保存方式：")
    print("1. 保存到cookies.json文件")
    print("2. 保存到.env文件")
    print("3. 同时保存到两种格式")
    
    save_choice = input("请输入选项(1-3): ")
    
    if save_choice == "1" or save_choice == "3":
        save_cookies_to_file(cookies)
    
    if save_choice == "2" or save_choice == "3":
        save_cookies_to_env(cookies)
    
    print("\n获取完成！现在你可以运行bilibili_auto.py来使用这些Cookie了。")

if __name__ == "__main__":
    main() 