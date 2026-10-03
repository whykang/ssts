# 随身听书

参照 [eprendre/tingshu](https://github.com/eprendre/tingshu) 的自定义源机制，用 WPF 编写的 Windows 听书程序。
本程序只是播放器，不提供任何内容，所有音频都来自第三方网站，请支持正版。

## 界面预览

**发现**：按源浏览分类，一键切换听书源

![发现](docs/images/discover.webp)

**聚合搜索**：多个源同时搜索，按源筛选结果

![聚合搜索](docs/images/search.webp)

## 功能

- **通用插件**：程序本身不带任何源，所有源都来自导入的 **DLL 插件**
  - C# 编写，引用 TingShu.Sdk 继承 SourceBase 即可（[开发指南](docs/插件开发指南.md)），一个插件可以包含多个源
  - 全部听书源都打包在 `ShunSources.Plugin` 这一个插件里，首次使用时在“插件”页导入它即可
  - 支持从文件 / 网址导入 .dll / .zip，启用、禁用、排序，以及一键测试和调试日志
- 发现（分类浏览、无限滚动）、**聚合搜索**（多源并发，按源筛选）、书架和最近播放
- 播放：断点续播、自动下一集、倍速 0.5–3x、快进快退、定时关闭、单书跳过片头片尾、章节倒序和筛选
- 音频提取：直链 / HTML / JSON / 正则 / **WebView 渲染** / **WebView 嗅探**；本地代理统一附加 UA、Referer、Cookie，并支持拖动进度
- 界面：浅色、深色、跟随系统，7 种主题色；自绘标题栏，Windows 11 下圆角；任务栏缩略图上有播放控制按钮和进度
- **适配大部分电脑**：
  - 每显示器 DPI 感知，高分屏和多屏缩放不同时也清晰
  - 窄窗口时侧栏自动收起，卡片列数随窗口宽度变化
  - 提供 x64 / x86 / ARM64 独立部署单文件版本，目标电脑无需安装 .NET
  - 首次启动按屏幕大小设置窗口尺寸，支持便携模式

快捷键：`空格` 播放/暂停 · `←/→` 快退/快进 · `Ctrl+←/→` 上/下一集 · `Ctrl+F` 搜索 · `Alt+←` 或鼠标侧键返回 · 媒体键

## 插件开发

📖 **[插件开发指南](docs/插件开发指南.md)**

新建一个 .NET 8 类库，引用 `TingShu.Sdk`，继承 `SourceBase` 实现搜索、分类、详情和音频提取，编译出的 dll 在程序的“插件”页导入即可。指南包括：

- 创建项目、编写第一个源
- 6 种音频提取方式（直链、HTML、JSON、WebView 渲染、WebView 嗅探、自定义）
- 宿主提供的网络请求、WebView、配置、进度报告等能力
- 登录、搜索验证、设置项、增量更新章节等可选接口
- 分页章节、访问频率限制的处理，调试、打包与安装

完整示例见 [sources/ShunSources.Plugin](sources/ShunSources.Plugin)。

## 目录结构

```
tingshu.sln
├─ tingshu/                  WPF 程序
│  ├─ Core/                  插件加载、网络、WebView、音频代理、播放器、书架存储
│  ├─ ViewModels/  Views/    界面（MVVM）
│  ├─ Controls/  Themes/     自定义控件、浅色/深色主题与控件样式
│  └─ Properties/PublishProfiles/   win-x64 / win-x86 / win-arm64 发布配置
├─ TingShu.Sdk/              插件 SDK（SourceBase、AudioExtractor、ISourceHost……）
├─ sources/
│  └─ ShunSources.Plugin/    听书源插件（全部源，单个 DLL）
└─ docs/                     插件开发文档
```

## 构建与运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。也可以用 Visual Studio 2022 打开 `tingshu.sln`。

```bash
dotnet build tingshu.sln
```

```bash
dotnet run --project tingshu
```

## 发布

发布程序（不包含任何源）：

```bash
dotnet publish tingshu -p:PublishProfile=win-x64
```

输出在 `publish/win-x64/`，包括 `TingShu.exe`（约 64 MB，自带运行时）、`docs/`，以及给插件开发者用的 `sdk/`。

单独编译源插件，得到 `sources/ShunSources.Plugin/bin/Release/net8.0/ShunSources.Plugin.dll`，在程序的“插件”页导入：

```bash
dotnet build sources/ShunSources.Plugin -c Release
```
32 位旧电脑用 `win-x86`，ARM 笔记本用 `win-arm64`。

运行环境：Windows 10 1607 及以上 / Windows 11。WebView 类插件需要 [WebView2 运行时](https://developer.microsoft.com/microsoft-edge/webview2/)（Windows 11 和大多数 Windows 10 已自带），没有它不影响其它功能。

## 数据位置

- 默认在 `%AppData%\TingShu`：`settings.json`、`library.json`、`plugins\`、`cache\`、`log.txt`
- 程序目录下放一个 `portable.txt` 即进入便携模式，数据改为保存在程序目录的 `data\` 下

## 协议

本项目采用 [PolyForm Strict License 1.0.0](LICENSE)，版权所有 © 2026 whykang。

- 允许：查看源代码，以及个人学习、研究、娱乐等非商业用途的使用
- 不允许：修改源代码、基于本项目制作新作品（二次开发）、分发本软件、任何商业用途

在此之外，作者另行许可：可以基于 TingShu.Sdk 开发听书源插件并自行分发，插件中不得包含本项目除 SDK 接口以外的代码。
