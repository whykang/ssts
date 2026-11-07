# v5.6

## 更新内容

- 新增 MCP 插件开发：通过局域网或本机连接 AI，创建草稿、校验、测试、安装、备份恢复和导出 ZIP；内置四端通用 JavaScript 插件开发指南，包含完整书籍源、电台回听日期与时间分组、搜索验证和增量更新示例。
- 插件云同步支持安装、更新和删除，默认关闭。同名插件保留高版本，删除记录防止旧设备重新上传已删除插件；手动重新导入可恢复同步，变更记录按 WebDAV 账号隔离。
- 手动搜索或本地选择的封面参加云同步，支持恢复来源封面；改进网盘新增封面图片的读取与鉴权，保留用户手动选择。
- 支持逐集精确进度与“已听完”状态同步；目录显示每集收听状态，已听完章节可从头重听，书签保持精确跳转。
- 自动续播下一集从头播放，手动选集恢复各集保存的进度。
- 支持 WMA 播放、拖动和断点恢复，改进换集及低采样率音频兼容性。
- 支持手动设置连载／完本状态；完本书优先读取目录缓存，仍可手动刷新。
- 播放页支持查看目录、筛选、定位章节、切集和加入书架；广播回听按日期分组，直播单独显示。
- 优化睡眠定时，增加 5 分钟预设、自定义小时和分钟，修复定时面板裁切与滚动问题。
- 优化搜索验证，直接读取网页中的真实结果；无法识别时由用户手动完成并仅重搜一次，减少反复搜索。
- 优化后台刷新、隐藏及锁屏时的进度保存，以及章节缓存并发写入。
- 改进设置页布局、目录、书架状态与移出确认界面；播放页右上角可直接打开书籍详情。

## 下载

| 平台 | 下载 |
|---|---|
| Mac（Apple 芯片和 Intel 通用，macOS 14.6 及以上） | [ssts-mac-5.6.dmg](https://github.com/whykang/ssts/releases/download/v5.6/ssts-mac-5.6.dmg) |
| Windows x64 | [ssts-5.6.0-win-x64.zip](https://github.com/whykang/ssts/releases/download/v5.6/ssts-5.6.0-win-x64.zip) |
| Windows x86 | [ssts-5.6.0-win-x86.zip](https://github.com/whykang/ssts/releases/download/v5.6/ssts-5.6.0-win-x86.zip) |
| Windows ARM64 | [ssts-5.6.0-win-arm64.zip](https://github.com/whykang/ssts/releases/download/v5.6/ssts-5.6.0-win-arm64.zip) |

| 插件合集 5.6（38 个源，四端通用） | [Sources-v5.6.zip](https://github.com/whykang/ssts/releases/download/v5.6/Sources-v5.6.zip) |

插件国内导入直链：<https://gitee.com/whykan/ssxz/raw/master/Sources-v5.6.zip>

## 安装与使用

- Windows 解压后运行“随身听书.exe”，自带运行时。升级前请先从托盘退出旧程序。
- Mac 打开 DMG，将“随身听书”拖入“应用程序”。本包使用本地签名，未经过 Apple 公证。
- WebView 类书源在 Windows 上需系统具备 WebView2 运行时。听书源从应用的插件页导入。
- 插件删除同步需要其他设备更新到支持该功能的版本。

SHA-256

```text
ff9238a8c66bd72966c58d64f5e4af1414911f158cbbaf9037a9efb55c7df4a2  ssts-5.6.0-win-x64.zip
d1f26a24efdafdeac7d5b5ba0d4e1e7d0a70c562c7420f7fbaa8d679b5a82520  ssts-5.6.0-win-x86.zip
c56f4e8850f66f134e78d57a6fc0a1aef7253eb8331cacc6b3b25fc3de1c28d4  ssts-5.6.0-win-arm64.zip
```
