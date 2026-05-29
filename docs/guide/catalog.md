---
title: 第三方工具目录
description: 自动扫描 Tools 文件夹中的第三方可执行文件，按分类展示，支持搜索、收藏、管理员运行和桌面快捷方式。
---

# 第三方工具目录

图吧工具箱自动扫描本地 `Tools/` 文件夹中的可执行文件，按分类展示，支持一键启动。

<div class="features-grid">

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-folder-tree"></i></div>
  <h3>自动扫描</h3>
  <p>自动扫描 Tools/ 文件夹，识别 .exe .bat .cmd .lnk .msc .ps1 .vbs 文件。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-tags"></i></div>
  <h3>分类展示</h3>
  <p>按中文分类文件夹自动归类，处理器工具、显卡工具、硬盘工具等。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-magnifying-glass"></i></div>
  <h3>实时搜索</h3>
  <p>按名字或路径搜索工具，快速定位需要的工具。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-star"></i></div>
  <h3>收藏夹</h3>
  <p>常用工具加收藏，下次直接找，一键访问。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-shield-halved"></i></div>
  <h3>管理员运行</h3>
  <p>一键以管理员身份启动工具，无需手动右键。</p>
</div>

<div class="feature-card">
  <div class="feature-icon"><i class="fa-solid fa-desktop"></i></div>
  <h3>桌面快捷方式</h3>
  <p>一键创建桌面快捷方式，方便从桌面直接启动。</p>
</div>

</div>

## 元数据系统

工具的描述、发布者、下载链接等信息通过 `Metadata/tools.json` 管理：

```json
{
  "match": "CrystalDiskMark",
  "description": "硬盘读写速度基准测试工具。",
  "publisher": "Crystal Dew World",
  "tags": ["硬盘", "速度测试"],
  "downloadUrl": "gh:hiyohiyo/crystaldiskmark",
  "downloadFilter": "*UserSetup*x64*.exe"
}
```

- **match** — 大小写不敏感的子串匹配，同时匹配文件名和相对路径
- **downloadUrl** — 下载地址，`gh:用户名/仓库名` 格式表示从 GitHub Release 下载
- **downloadFilter** — 下载文件名通配符筛选

## 图标缓存

工具图标通过 `ToolIconService` 提取并缓存：

- 从 .exe 文件提取图标，从 .lnk 文件解析目标图标
- 缓存为 PNG 格式，存储在 `%LocalAppData%/TubaWinUi3/IconCache/`
- 缓存键为工具路径的 SHA256 哈希，避免路径冲突
