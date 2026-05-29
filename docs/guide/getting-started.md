---
title: 快速开始
description: 图吧工具箱开发环境搭建、项目克隆与构建指南，基于 .NET 10 和 WinUI 3。
---

# 快速开始

## 环境准备

1. 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
2. 安装 [Visual Studio 2022 17.14+](https://visualstudio.microsoft.com/) 或 [VS Code](https://code.visualstudio.com/)（配合 C# Dev Kit 扩展）
3. 安装 [Git](https://git-scm.com/downloads)

## 克隆与构建

```bash
git clone https://github.com/luolangaga/tubatool.git
cd tubawinui3
dotnet build        # 编译
dotnet run          # 运行（Unpackaged 模式）
```

## 项目结构

```
App.xaml.cs                       → 应用入口，创建 MainWindow
MainWindow.xaml.cs                → 导航框架，侧边栏 + Frame
Pages/
  HomePage.xaml.cs                → 外部工具网格（扫描 Tools/ 文件夹）
  BuiltinToolsPage.xaml.cs        → 内置工具页面
  HardwarePage.xaml.cs            → WMI 硬件信息
  SettingsPage.xaml.cs            → 设置页
Services/
  IBuiltinTool.cs                 → 内置工具接口
  BuiltinToolRegistry.cs          → 内置工具注册表
  BuiltinTools/                   → 所有内置工具的实现
  ToolCatalog.cs                  → 外部工具扫描
  ToolMetadataService.cs          → 工具元数据
  *Service.cs                     → 各工具对应的后端服务
Models/
  ToolItem.cs                     → 外部工具数据模型
Metadata/
  tools.json                      → 外部工具的描述/发布者/下载链接
Tools/                            → 第三方可执行文件
```

## 构建说明

- 默认 Debug 配置，x64 架构（RuntimeIdentifier 自动检测）
- 支持 x86、x64、ARM64 三种架构
- 推荐使用 **Unpackaged** 模式开发运行
- Packaged 运行需要 MSIX 注册

```bash
dotnet build                          # 默认 Debug, x64
dotnet run                            # Unpackaged 模式运行
dotnet run -p:Configuration=Debug     # 同上
```
