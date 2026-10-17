# 桌面流量监控插件（Desktop Traffic Widget）

<p align="center">Windows 任务栏实时网速挂件 · 单文件 exe（约 1.3 MB）· 点开即用 · 零上传</p>

<p align="center"><code>Windows</code> · <code>流量监控</code> · <code>任务栏插件</code> · <code>灵动岛</code> · <code>WebView2</code> · <code>C#</code> · <code>桌面挂件</code></p>

一个极轻量的 Windows 10/11 桌面实时流量监控挂件：单 exe 直接运行，无需安装、无需管理员权限、不联网上传任何数据，在任务栏角落或屏幕顶部灵动岛实时显示上下行速率与累计流量。

## 功能特性

- **双显示模式**（右键菜单一键切换，配置持久化）
  - 任务栏吸附模式：贴任务栏左侧，与主流音乐挂件同位置同厚度，横向宽度随内容自适应
  - 灵动岛模式：屏幕顶端居中黑色胶囊，hover 弹性展开显示总下载 / 总上传 / 峰值速率，离开自动收回
- **实时网速**：500ms 采样，下载 / 上传速率 + 迷你波形图 + 累计流量、峰值统计
- **点开即用**：单文件 exe（.NET 8 单文件发布，约 1.3 MB），WebView2 使用系统自带 Edge
- **绿色便携**：配置（`settings.json`）与缓存（`_webview2/`）均落在 exe 同级目录，不写注册表（开机自启除外，HKCU Run 可选）
- **可选开机自启**：右键菜单勾选，无需管理员权限
- **浅色 / 深色主题**、右键菜单（重置统计 / 退出）

## 使用

1. 下载 Releases 中的 `流量监控.exe`，双击运行
2. 右键挂件：切换显示模式 / 开机自启 / 重置统计 / 退出

> 运行依赖：.NET 8 Desktop Runtime（微软官网免费安装）+ WebView2 Runtime（Win11 内置，Win10 多数已随 Office/Edge 预装）。

## 技术架构

| 层 | 实现 |
|---|---|
| 宿主 | C# WinForms（.NET 8），顶层透明窗口 `WS_EX_NOREDIRECTIONBITMAP` + DirectComposition 直接合成 |
| 界面 | HTML/CSS/JS 内嵌于 WebView2（速率动画、灵动岛展开动效、宽度自适应测量） |
| 数据 | `IPInterface` / `NetworkInterface` 静态表轮询差分计算实时速率 |

关键设计：不用 `SetParent` 嵌入任务栏进程（会切断 WebView2 输入路由导致鼠标失效），而是顶层透明窗口浮在任务栏上方并跟随定位，与主流音乐挂件同机制。

## 目录结构

```
├── 流量监控.exe        # 交付物（单文件自包含）
├── settings.json       # 显示模式持久化（运行时生成）
└── src/webview/        # 全部生产源码
    ├── Program.cs      # 入口：窗口/菜单/前后端消息桥
    ├── TaskbarHost.cs  # 窗口定位、吸附、高度动画
    ├── NetMonitor.cs   # 网卡流量采样与速率计算
    └── WidgetHtml.cs   # 内嵌 HTML/CSS/JS 界面
```

## 构建

```powershell
dotnet publish src/webview/TrafficWidgetWeb.csproj -c Release -r win-x64 -o publish
```

产物 `TrafficWidgetWeb.exe` 重命名为 `流量监控.exe` 即可运行。如需免装 .NET 的完全自包含版，发布时追加 `--self-contained true -p:PublishTrimmed=false`（体积约增大到几十 MB）。

## License

MIT
