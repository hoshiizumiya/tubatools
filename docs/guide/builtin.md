---
title: 内置实用工具
description: 图吧工具箱内置 12 款实用工具：功耗监控、证书屏蔽、端口查看、Hosts 编辑、键盘测试、垃圾清理、蓝屏分析等。
---

# 内置实用工具

图吧工具箱内置了 12 款实用工具，无需外部 exe，直接在应用内使用。

<div class="features-grid">

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-bolt"></i></div>
  <h3>功耗监控</h3>
  <p>实时显示 CPU/GPU 负载和电池放电功率，掌握系统功耗状态。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-certificate"></i></div>
  <h3>证书屏蔽</h3>
  <p>管理和屏蔽不信任的数字证书，批量操作，一键添加/移除。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-network-wired"></i></div>
  <h3>端口查看</h3>
  <p>查看 TCP/UDP 端口占用和进程信息，快速定位端口冲突。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-pen-to-square"></i></div>
  <h3>Hosts 编辑</h3>
  <p>可视化编辑系统 Hosts 文件，支持备份/恢复，一键刷新 DNS。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-keyboard"></i></div>
  <h3>键盘测试</h3>
  <p>逐键检测键盘按键状态，可视化反馈，快速排查按键故障。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-broom"></i></div>
  <h3>垃圾清理</h3>
  <p>扫描清理系统临时文件、浏览器缓存、缩略图缓存等垃圾文件。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-triangle-exclamation"></i></div>
  <h3>蓝屏分析</h3>
  <p>解析蓝屏崩溃转储文件，查看 BSOD 错误代码和导致崩溃的驱动。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-wifi"></i></div>
  <h3>WiFi 密码查看</h3>
  <p>查看已连接 WiFi 的密码，一键复制，方便分享。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-gauge-high"></i></div>
  <h3>网速测试</h3>
  <p>测试网络下载/上传速度，带进度条，支持取消。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-battery-three-quarters"></i></div>
  <h3>电池报告</h3>
  <p>生成笔记本电池容量、循环次数、损耗和实时状态报告。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-chart-pie"></i></div>
  <h3>磁盘空间分析</h3>
  <p>可视化分析磁盘空间占用，树状图展示文件夹大小。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-download"></i></div>
  <h3>Winget 安装器</h3>
  <p>通过 Windows 包管理器搜索和安装软件，图形化操作。</p>
</div>

</div>

## 工具类型说明

| 类型 | 图标 | 说明 | 适用场景 |
|------|------|------|----------|
| Dialog | <i class="fa-solid fa-window-restore"></i> | 弹窗式，工具 UI 在 ContentDialog 中展示 | 需要交互界面的工具 |
| BackgroundTask | <i class="fa-solid fa-gear"></i> | 后台执行，完成后通知 | 快速查询类工具 |
| ProgressTask | <i class="fa-solid fa-spinner"></i> | 带进度的长时间任务 | 需要进度条的任务 |
| InstantAction | <i class="fa-solid fa-bolt"></i> | 即时操作，无 UI | 一键执行的动作 |
