---
title: 添加内置工具
description: 如何开发新的内置工具，实现 IBuiltinTool 接口并注册到工具箱中。
---

# 添加内置工具

内置工具是直接嵌入在应用中的功能，无需外部 exe。所有内置工具都实现 `IBuiltinTool` 接口。

## 第 1 步：创建工具类

在 `Services/BuiltinTools/` 下新建一个 `.cs` 文件：

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class MyNewTool : IBuiltinTool
{
    public string Id => "my-new-tool";
    public string Name => "我的工具";
    public string Description => "这是一个示例内置工具。";
    public string Glyph => "\uE8E5";             // Segoe MDL2 Assets 图标
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = Name,
            Content = "Hello from MyNewTool!",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
```

## 第 2 步：选择工具类型

| 类型 | 说明 | 适用场景 |
|------|------|----------|
| `Dialog` | 弹窗式，工具 UI 在 ContentDialog 中展示 | 需要交互界面的工具 |
| `BackgroundTask` | 后台执行，完成后通知 | 快速查询类 |
| `ProgressTask` | 带进度的长时间任务 | 需要进度条的任务 |
| `InstantAction` | 即时操作，无 UI | 一键执行的动作 |

## 第 3 步：注册工具

打开 `Services/BuiltinToolRegistry.cs`，在 `RegisterDefaults()` 方法中添加：

```csharp
public static void RegisterDefaults()
{
    // ... 已有工具 ...
    Register(new MyNewTool());   // 添加这一行
}
```

## 第 4 步：运行验证

```bash
dotnet build
dotnet run
```

启动后在左侧导航点击"内置工具"，即可看到新工具。

## 开发模式参考

| 工具文件 | 类型 | 特点 |
|----------|------|------|
| `KeyboardTestTool.cs` | Dialog | 纯弹窗交互，KeyDown/KeyUp 事件处理 |
| `DiskSpaceAnalyzerTool.cs` | Dialog | 打开独立 Window，Canvas 自绘 |
| `PortViewerTool.cs` | Dialog | 列表 + 搜索 + 筛选，后台数据加载 |
| `HostsEditorTool.cs` | Dialog | CRUD 操作，保存/备份，未保存提醒 |
| `WifiPasswordTool.cs` | BackgroundTask | 后台获取数据，加载状态切换 |
| `SpeedTestTool.cs` | ProgressTask | 进度条 + 取消，长时间任务 |
| `JunkCleanerTool.cs` | ProgressTask | 扫描 + 清理两阶段 |

## 常见模式

- **状态管理** — 通过 `ScrollViewer.Tag` 存储内部 State 类，避免字段初始化问题
- **异步加载** — 先显示 ProgressRing，`await Task.Run(...)` 后切换到内容面板
- **颜色常量** — 使用 `ThemeColors` 静态类，强调色用 `AccentBlue/Green/Orange/Red/Purple`
- **卡片布局** — `MakeStatCard()` 是常用的统计卡片构建方法
