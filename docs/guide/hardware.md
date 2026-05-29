---
title: 硬件信息查询
description: 通过 WMI 实时读取 CPU、主板、内存、显卡、硬盘等全面硬件信息，后台线程查询不阻塞 UI。
---

# 硬件信息查询

通过 Windows Management Instrumentation (WMI) 实时读取系统硬件数据，无需安装任何驱动或第三方组件，信息准确可靠。

## 支持查询的硬件信息

| 类别 | 查询内容 | WMI 类 |
|------|----------|--------|
| <i class="fa-solid fa-microchip" style="color:#0078d4"></i> 处理器 | 型号、核心数、线程数、主频、缓存 | Win32_Processor |
| <i class="fa-solid fa-memory" style="color:#8b5cf6"></i> 内存 | 总容量、可用容量、频率、插槽信息 | Win32_PhysicalMemory |
| <i class="fa-solid fa-display" style="color:#10b981"></i> 显卡 | 型号、显存、驱动版本 | Win32_VideoController |
| <i class="fa-solid fa-hard-drive" style="color:#f59e0b"></i> 硬盘 | 型号、容量、接口类型、分区信息 | Win32_DiskDrive |
| <i class="fa-solid fa-desktop" style="color:#06b6d4"></i> 显示器 | 分辨率、刷新率、制造商 | Win32_DesktopMonitor |
| <i class="fa-solid fa-network-wired" style="color:#ec4899"></i> 网卡 | 型号、MAC 地址、IP 地址 | Win32_NetworkAdapter |
| <i class="fa-solid fa-volume-high" style="color:#6366f1"></i> 声卡 | 型号、制造商 | Win32_SoundDevice |
| <i class="fa-solid fa-server" style="color:#ef4444"></i> 主板 | 制造商、型号、BIOS 版本 | Win32_BaseBoard |

## 特性

- **实时读取** — 每次打开页面都获取最新数据，不缓存过期信息
- **后台查询** — WMI 查询在后台线程执行，不阻塞 UI
- **分类展示** — 按硬件类别分组，信息一目了然
- **零依赖** — 纯 WMI 查询，无需安装额外驱动或工具
