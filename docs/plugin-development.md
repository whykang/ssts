# 插件开发指南

程序本身不带任何听书源，所有源都由插件提供。插件有两种，可以同时安装：

| | 脚本插件 | DLL 插件 |
|---|---|---|
| 形式 | zip 包：`manifest.json` + 若干 `.js` | .NET 8 类库编译出的 dll |
| 开发环境 | 任何文本编辑器 | .NET 8 SDK |
| 权限 | 只能使用宿主提供的能力（网络请求、WebView、配置、缓存） | 普通 .NET 代码，和本程序权限相同 |
| 更新 | 重新导入立即生效 | 替换已加载的 dll 需要重启程序 |
| 适合 | 绝大多数网站 | 需要用到 .NET 库或很复杂的处理 |

一般情况下推荐写脚本插件。两种插件里有 ID 相同的源时，以脚本插件的为准。

- [一、脚本插件](#一脚本插件)
- [二、DLL 插件](#二dll-插件)

# 一、脚本插件

## 1. 插件包的结构

一个 zip 包，里面是一个 `manifest.json` 和若干脚本（zip 里可以有一层目录）：

```
MySources.zip
├─ manifest.json
├─ common.js
└─ sites.js
```

`manifest.json`：

```json
{
  "name": "MySources",
  "version": "1.0.0",
  "author": "作者名",
  "description": "显示在插件页面上的说明",
  "scripts": ["common.js", "sites.js"]
}
```

| 字段 | 说明 |
|---|---|
| `name` | 插件名，也是安装后的目录名；重新导入同名的插件会覆盖旧版本 |
| `version` / `author` / `description` | 显示在“插件”页的“插件包”里 |
| `scripts` | 要加载的脚本，按顺序执行，前面的脚本里定义的函数后面的可以直接用。只有一个脚本时也可以写 `"main": "index.js"`；都不写时按文件名顺序加载包里所有的 `.js` |

一个插件包里可以注册任意多个源。

## 2. 编写一个源

脚本里调用 `registerSource({...})` 注册源，对象里的方法由宿主在需要时调用：

```js
registerSource({
  id: '0f9c0b5c2d7e4c1b9a0e6f3a2b1c4d5e', // 32 位十六进制，随机生成，发布后不要改
  name: '我的站点',
  url: 'https://example.com',
  description: '显示在插件页面上的说明',

  search: function (keywords, page) {
    var doc = this.host.getHtml('https://example.com/search?q=' + encodeURIComponent(keywords) + '&p=' + page);
    var books = doc.select('.item').map(function (e) {
      return {
        bookUrl: e.first('a').absUrl('href'),
        title: e.first('h3').text(),
        coverUrl: e.first('img').absUrl('src'),
        author: e.first('.author').text()
      };
    });
    return { books: books, totalPage: regexInt(doc.first('.total').text(), /(\d+)/, page) };
  },

  categoryMenus: function () {
    return [{ title: '分类', tabs: [{ title: '玄幻', url: 'https://example.com/list/1.html' }] }];
  },

  categoryPage: function (url) {
    var doc = this.host.getHtml(url);
    var books = doc.select('.item').map(function (e) {
      return { bookUrl: e.first('a').absUrl('href'), title: e.first('h3').text(), coverUrl: e.first('img').absUrl('src') };
    });
    return { books: books, currentPage: 1, totalPage: 1, nextUrl: doc.first('a.next').absUrl('href') };
  },

  bookDetail: function (bookUrl, loadEpisodes, loadFullPages) {
    var doc = this.host.getHtml(bookUrl);
    var episodes = doc.select('#playlist a').map(function (a) {
      return { title: a.text(), url: a.absUrl('href') };
    });
    return { episodes: episodes, intro: doc.first('.intro').text() };
  },

  audio: function (episodeUrl) {
    return this.host.getHtml(episodeUrl).first('audio').absUrl('src');
  }
});
```

脚本是同步执行的：`host.getHtml` 这些请求会直接返回结果，不需要 `await` 或回调。宿主在后台线程里调用这些方法，同一个源可以同时被调用多次（比如一边加载章节一边取封面的请求头），所以不要依赖“上一次调用留下的全局变量”。

运行环境是独立的脚本引擎，支持 ES2015 及以后的大部分语法，但**没有** `window`、`document`、`fetch`、`XMLHttpRequest`、`setTimeout`；需要等待时用 `host.sleep(毫秒)`。出错时直接 `throw new Error('给用户看的提示')`。

### 源的属性

| 属性 | 说明 |
|---|---|
| `id` | 源的唯一 ID，书架、设置、章节缓存都以它为准，发布后不要改 |
| `name` / `url` / `description` | 名称、网址、说明 |
| `group` | 分组，默认“听书” |
| `searchable` / `discoverable` | 设为 `false` 可以不参与聚合搜索 / 不出现在“发现”页。没有写 `search` 方法的源本来就不参与搜索，没有 `categoryMenus` 的不出现在“发现” |
| `multipleEpisodePages` | 章节分多页，见下面的“章节很多的书” |
| `enabledByDefault` | 首次导入时是否启用，默认 `true` |
| `loginUrl` / `loginDesktop` | 需要登录的源：登录页地址，以及是否用电脑版打开。见第 5 节 |
| `config` | 源自己的设置项，见第 5 节 |
| `searchVerificationDesktop` / `searchDelaySeconds` | 搜索需要人机验证时用，见第 5 节 |

### 源的方法

| 方法 | 返回 |
|---|---|
| `search(keywords, page)` | `{ books, totalPage }`。不知道总页数时，确定有下一页就返回 `page + 1` |
| `categoryMenus()` | `[{ title, tabs: [{ title, url }] }]`。标签的 `url` 不一定是网址，可以是任意字符串，宿主只会把它原样传回 `categoryPage` |
| `categoryPage(url)` | `{ books, currentPage, totalPage, nextUrl }`，`nextUrl` 为空表示没有下一页 |
| `bookDetail(bookUrl, loadEpisodes, loadFullPages)` | `{ episodes, intro, author, artist, coverUrl, title, status }`。除 `episodes` 外的字段不为空时会补充到书籍信息里 |
| `audio(episodeUrl)` | 可以播放的音频地址；需要请求头时返回 `{ url, headers }`。不写这个方法时，章节地址本身就是音频地址 |

一本书（`books` 里的每一项）：`bookUrl`（必填）、`title`、`coverUrl`、`author`、`artist`（演播）、`intro`、`status`。

一集（`episodes` 里的每一项）：`title`、`url`（必填），以及可选的 `isFree`（`false` 表示付费章节，列表里显示一把锁）和 `live`（`true` 表示直播流：不能拖动、不记进度）。

## 3. 宿主能力（this.host）

每个源有自己的 `host`，在方法里用 `this.host` 取得。

### 网络请求

| 方法 | 说明 |
|---|---|
| `getString(url, options)` | 请求文本，自动识别 GBK / UTF-8 等编码。非 2xx 状态码会抛出错误，错误对象上有 `status`（状态码） |
| `getHtml(url, options)` | 请求并解析成 HTML 元素，见第 4 节 |
| `getJson(url, options)` | 请求并解析 JSON，兼容 JSONP |
| `getStrings(urls, options, concurrency)` | 并发请求多个地址，返回文本数组，失败的位置是 `null`。`options` 可以是一份，也可以是和 `urls` 一一对应的数组；`concurrency` 默认 4 |
| `fetch(url, options)` | 返回 `{ status, body }`，非 2xx 状态码不会抛出错误 |
| `status(url, options)` | 只返回状态码，不下载内容 |
| `finalUrl(url, options)` | 跟随跳转后的最终地址 |
| `parseHtml(html, baseUrl)` | 解析一段 HTML |
| `cookies(url)` | 这个地址现在会带上的 Cookie，格式 `a=1; b=2`（包括在登录窗口里登录后得到的） |
| `userAgent(desktop)` | 用户在设置里配置的电脑版 / 手机版 UA |

`options` 的字段：

| 字段 | 说明 |
|---|---|
| `desktop` | `true` 用电脑版 UA，默认用手机版 |
| `method` / `body` / `contentType` | POST 等请求，`body` 默认按表单（`application/x-www-form-urlencoded`）发送 |
| `headers` | 额外请求头，例如 `{ Referer: 'https://example.com/' }` |
| `encoding` | 强制按指定编码解码（例如 `'gbk'`），默认自动判断 |

### WebView

用来处理“要执行页面里的脚本才有内容”的网站。需要用户电脑上有 WebView2 运行时，速度也比普通请求慢，能用普通请求就不要用。

| 方法 | 说明 |
|---|---|
| `render(url, { desktop, script, timeout, parse })` | 用 WebView 加载页面，反复执行 `script`（默认返回整个 HTML），把结果交给 `parse(结果)`，直到它返回非空字符串，这个字符串就是 `render` 的返回值。不写 `parse` 时返回第一次拿到的非空结果 |
| `sniff(url, { desktop, script, timeout, validate })` | 用 WebView 打开页面，嗅探页面发出的音频请求并返回它的地址。`validate(地址)` 返回 `true` 的才算；`script` 是可选的触发播放的脚本（宿主也会自动点常见播放器的播放按钮） |

`timeout` 的单位是毫秒，默认 25000。

```js
audio: function (episodeUrl) {
  return this.host.sniff(episodeUrl, { validate: function (u) { return /\.(mp3|m4a)(\?|$)/.test(u); } });
}
```

### 配置、缓存和其它

| 方法 | 说明 |
|---|---|
| `getPref(key, 默认值)` / `setPref(key, value)` | 源自己的配置，宿主会持久保存；每个源的配置互相独立 |
| `readCache(name)` / `writeCache(name, text)` / `cacheAge(name)` | 源自己的缓存文件。`cacheAge` 返回距今多少毫秒，不存在返回 -1 |
| `toast(message)` | 界面提示 |
| `log(message)` | 调试日志，显示在“插件 → 调试日志”里 |
| `progress(info)` | 加载多页章节时报告进度（例如 `'3 / 20'`），传 `null` 表示结束 |
| `sleep(ms)` | 等待 |
| `md5` / `sha256` / `base64Encode` / `base64Decode` / `uuid` / `htmlDecode` | 常用的编码和摘要 |
| `aesEcbDecrypt(data, key)` | AES-ECB 解密：`data` 是 base64，`key` 是十六进制，返回文本，失败返回空字符串 |
| `sourceId` | 这个源的 ID |

## 4. HTML 与 JSON 帮助方法

`getHtml` / `parseHtml` 返回的是元素对象，用 CSS 选择器查找：

| 方法 | 说明 |
|---|---|
| `select(selector)` | 所有匹配的元素，是普通的数组，可以直接 `map` / `filter` |
| `first(selector)` | 第一个匹配的元素。找不到时返回一个“空元素”，继续调用它的方法不会出错，只是都返回空；用 `exists()` 判断有没有找到 |
| `text()` / `ownText()` | 元素里的文字 / 只算元素自己的文字，不含子元素 |
| `attr(name)` | 属性值，没有时为空字符串 |
| `absUrl(name)` | 把 `href`、`src` 这类属性补成完整的网址 |
| `html()` / `outerHtml()` | 里面的 / 包括自己的 HTML |
| `tag()` / `hasClass(name)` | 标签名 / 有没有某个 class |
| `children()` / `parent()` / `byId(id)` | 子元素数组 / 父元素 / 按 id 在整个文档里找 |

处理 JSON 时有几个全局函数，路径不存在时不会出错：

| 函数 | 说明 |
|---|---|
| `at(obj, 'data.list.0.name')` | 按路径取值，数组下标可以是负数（`-1` 是最后一个），不存在返回 `null` |
| `str(obj, path)` | 取成字符串，不存在返回空字符串 |
| `int(obj, path, 默认值)` | 取成整数 |
| `items(obj, path)` | 取数组，不存在返回空数组 |
| `regexInt(text, /第(\d+)页/, 默认值)` | 用正则取第一个分组并转成整数 |
| `parseJson(text)` | 解析 JSON，兼容 JSONP |

```js
var json = this.host.getJson('https://example.com/api/search?q=' + encodeURIComponent(keywords));
var books = items(json, 'data.list').map(function (b) {
  return { bookUrl: 'https://example.com/book/' + str(b, 'id'), title: str(b, 'name'), coverUrl: str(b, 'cover') };
});
```

## 5. 可选功能

在源里加上对应的属性或方法，宿主会自动识别：

| 写法 | 作用 |
|---|---|
| `audioHeaders(audioUrl)` | 播放音频时附加的请求头（例如 `Referer` 防盗链），返回 `{ 名: 值 }`。也可以直接在 `audio` 里返回 `{ url, headers }` |
| `coverHeaders(coverUrl)` | 加载封面时附加的请求头。先判断封面地址是不是自己网站的，不是就返回 `null` |
| `loginUrl` + `loginDesktop` | 插件页面展开这个源后会出现“登录”，登录在弹出的网页窗口里完成，之后普通请求会自动带上登录后的 Cookie |
| `checkLogin()` | 确认登录状态：已登录返回昵称（拿不到昵称时返回空字符串），没登录时抛出错误，提示里要带“登录”两个字。登录窗口关闭后宿主会调用它，并在插件页面显示“已登录 · 昵称” |
| `config` | 源自己的设置项，插件页面出现“设置”。值用 `this.host.getPref(key)` 读取，都是字符串 |
| `searchVerificationUrl(keywords)` | 搜索需要人机验证的网站：返回验证页面的地址。搜索失败或没有结果时，搜索页会在这个源旁显示“验证”，用户在弹出的网页里通过验证后重新搜索。`searchVerificationDesktop` 决定用不用电脑版打开（默认 `true`），`searchDelaySeconds` 用于网站限制两次搜索最小间隔的情况 |
| `updateEpisodes(bookUrl, known)` | 增量更新章节，见下面 |
| `onPlaybackState(state)` | 接收播放状态：`playing` / `paused` / `stopped` / `error` |

设置项的写法：

```js
config: [
  { key: 'quality', label: '音质', type: 'select', options: ['标准', '高品质'], default: '标准' },
  { key: 'skipAds', label: '跳过广告', type: 'switch', default: 'true' },
  { key: 'token', label: 'Token', type: 'text', default: '' }
]
```

### 章节很多的书

章节分很多页的网站，把 `multipleEpisodePages` 设为 `true`。第一次打开一本书时，宿主会先用 `loadFullPages = false` 调用 `bookDetail`，这时只取第一页马上返回，让用户能立刻开始听；随后再用 `loadFullPages = true` 调用一次，取全部章节。加载过程中用 `this.host.progress('3 / 20')` 报告进度，结束时传 `null`。

书架里的书再次打开时，如果源提供了 `updateEpisodes(bookUrl, known)`，宿主会把已经缓存的章节（`known`，按网站上的顺序）传进来，只需要取新增的部分，返回完整的 `{ episodes, ... }`；返回 `null` 表示这次没法增量更新，宿主会改用 `bookDetail` 整本重新加载。

网站限制访问频率时，在多页之间用 `this.host.sleep` 隔开，不要并发着抓。

## 6. 调试

- “插件”页展开一个源，点“测试”：依次测试分类、搜索、详情和音频解析，结果和 `host.log` 的输出都在调试日志里（“插件”页右上角的文档图标）；
- 脚本有语法错误或加载时出错，导入会失败并提示第几行；已经安装的旧版本不会被覆盖；
- 修改脚本后重新导入 zip 即可，立即生效，不用重启。也可以点“导入 → 打开插件目录”，直接改安装好的脚本，改完重启程序。

程序内置的广播电台（云听、我的电台）就是一个脚本插件，用到的都是本文介绍的写法：接口签名、并发请求、缓存，以及带 `live` 标记的直播流。

## 7. 打包与安装

把 `manifest.json` 和脚本一起压缩成 zip，然后在“插件”页点“导入 → 从文件导入 zip…”，或者把 zip 直接拖到“插件”页上；也可以用“从网址导入”，填 zip 的下载地址。

导入后的位置：`%AppData%\TingShu\plugins\插件名\`（便携模式为程序目录下 `data\plugins`）。在“插件包”里点删除会移除整个插件和它包含的所有源；只想停用某个源，关掉它的开关即可。

脚本插件只能通过宿主提供的能力访问网络，但它拿得到你在登录窗口里登录后的 Cookie。**只导入你信任的来源提供的插件**。

# 二、DLL 插件

DLL 插件是一个普通的 .NET 8 类库，引用 `TingShu.Sdk`，继承 `SourceBase` 编写；一个插件程序集里可以包含任意多个源。

完整示例：[sources/ShunSources.Plugin](../sources/ShunSources.Plugin)，一个插件里包含了 19 个源，覆盖了本文提到的大部分写法。

## 1. 创建项目

```bash
dotnet new classlib -n MySources -f net8.0
```

编辑 `MySources.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <!-- 显示在插件页面上 -->
    <Version>1.0.0</Version>
    <Company>作者名</Company>
  </PropertyGroup>
  <ItemGroup>
    <!-- 引用发布目录 sdk 文件夹中的 TingShu.Sdk.dll；Private=false 表示不复制（由宿主提供） -->
    <Reference Include="TingShu.Sdk">
      <HintPath>path\to\sdk\TingShu.Sdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <!-- HTML 解析器，同样由宿主提供，不要复制 -->
    <PackageReference Include="AngleSharp" Version="1.8.3" ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
```

在本解决方案内开发时，也可以像示例插件一样直接 `ProjectReference` 到 `TingShu.Sdk`（加 `Private=false`、`ExcludeAssets=runtime`）。

`TingShu.Sdk` 和 `AngleSharp` 由宿主提供并共享，插件目录里不要放这两个 dll；插件自己的其它依赖放在插件 dll 旁边即可。

## 2. 编写一个源

继承 `SourceBase`，需要一个公开的无参构造函数：

```csharp
using TingShu.Sdk;

public sealed class MySite : SourceBase
{
    public override string Id => "0f9c0b5c2d7e4c1b9a0e6f3a2b1c4d5e"; // 用 Guid.NewGuid().ToString("N") 生成，发布后不要改
    public override string Name => "我的站点";
    public override string Url => "https://example.com";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await Host.GetHtmlAsync($"https://example.com/search?q={Uri.EscapeDataString(keywords)}&p={page}", ct: ct);
        var books = doc.Select(".item").Select(e => new Book(
            coverUrl: e.SelectFirst("img").AbsUrl("src"),
            bookUrl: e.SelectFirst("a").AbsUrl("href"),
            title: e.SelectFirst("h3").Text())).ToList();
        var total = int.TryParse(doc.SelectFirst(".total").Text(), out var t) ? t : page;
        return new SearchResult(books, total);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await Host.GetHtmlAsync(bookUrl, ct: ct);
        var episodes = doc.Select("#playlist a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList();
        return new BookDetail(episodes, doc.SelectFirst(".intro").Text());
    }

    public override AudioExtractor GetAudioExtractor() =>
        AudioExtractor.Html(doc => doc.SelectFirst("audio")?.AbsUrl("src"));
}
```

所有方法都在后台线程执行，网络请求直接 `await` 即可。请把 `ct` 传给每个请求：用户离开页面或超时时会取消。

### SourceBase 成员

| 成员 | 说明 |
|---|---|
| `Id` | 源的唯一 ID，书架、设置、章节缓存都以它为准，发布后不要改 |
| `Name` / `Url` / `Description` | 名称、网址、说明（显示在插件页面） |
| `Group` | 分组，默认“听书” |
| `IsSearchable` / `IsDiscoverable` | 是否参与聚合搜索 / 是否出现在“发现”页，默认都是 true |
| `IsMultipleEpisodePages` | 章节分多页，见第 6 节 |
| `EnabledByDefault` | 首次导入时是否启用，默认 true |
| `SearchAsync(keywords, page, ct)` | 搜索，返回 `SearchResult(books, totalPage)`；不知道总页数时，确定有下一页就返回 `page + 1` |
| `GetCategoryMenusAsync(ct)` | 分类菜单：`CategoryMenu(标题, CategoryTab[])`，每个标签带一个地址 |
| `GetCategoryPageAsync(url, ct)` | 分类的一页书：`CategoryPage(books, currentPage, totalPage, currentUrl, nextUrl)`，`nextUrl` 为空表示没有下一页 |
| `GetBookDetailAsync(bookUrl, loadEpisodes, loadFullPages, ct)` | 详情与章节列表。`BookDetail` 中除章节外的字段（简介、作者、演播、封面、标题、状态）不为空时会补充到书籍信息里 |
| `GetAudioExtractor()` | 把章节地址解析成音频地址的方式，见第 3 节 |
| `Host` | 宿主提供的能力，见第 4 节 |

分类标签的地址不一定是网址，可以是任意字符串，宿主只会把它原样传回 `GetCategoryPageAsync`，例如 `my-search:关键词#1`。

## 3. 音频提取器

`GetAudioExtractor()` 决定章节地址（`Episode.Url`）如何变成可播放的音频地址：

| 写法 | 适用场景 |
|---|---|
| `AudioExtractor.Direct` | 章节地址本身就是音频 |
| `AudioExtractor.Html(doc => ..., desktop)` | 请求章节页面，从 HTML 里取 |
| `AudioExtractor.Json(json => ..., desktop)` | 请求章节地址返回 JSON，从中取 |
| `AudioExtractor.WebView(result => ..., desktop, script)` | 用 WebView 打开页面，反复执行 `script`，直到 `result` 解析出地址（默认脚本返回整个 HTML） |
| `AudioExtractor.WebViewSniff(validate, desktop, script)` | 用 WebView 打开页面，嗅探页面发出的音频请求；`validate` 判断哪个请求是音频，`script` 是可选的触发播放脚本（宿主也会自动点击常见播放器的播放按钮） |
| `AudioExtractor.Custom(async (url, ct) => ...)` | 完全自己处理，可以组合普通请求、`Host.RenderAsync`、`Host.SniffMediaAsync` |

播放地址带时效签名的，用 `Custom` 在播放时现取。WebView 类方式需要用户电脑上有 WebView2 运行时，速度也比普通请求慢，能用普通请求就不要用 WebView。

宿主通过本地代理播放音频，会附带 UA、`IAudioHeaders` 提供的请求头，并且支持拖动进度；服务器不支持分段下载时，代理会在后台下载整个文件再按需提供。宿主播放前会先探测地址，404 / 410 会提示“音频文件已失效”。

## 4. 宿主能力（Host）

| 成员 | 说明 |
|---|---|
| `GetStringAsync(url, options, ct)` | 请求文本，自动识别 GBK / UTF-8 等编码；非 2xx 状态码会抛出 `HttpRequestException`，消息里带状态码 |
| `GetHtmlAsync(url, options, ct)` | 请求并解析 HTML，文档已设置 BaseUrl，可以直接 `AbsUrl("href")` |
| `GetJsonAsync(url, options, ct)` | 请求并解析 JSON |
| `ParseHtml(html, baseUrl)` | 解析一段 HTML |
| `RenderAsync(url, desktop, script, timeout, ct)` | 用 WebView 加载页面，等 `script` 返回非空结果（默认返回整个 HTML），适合要执行 JS 才有内容的页面 |
| `SniffMediaAsync(url, desktop, validate, timeout, ct, script)` | 用 WebView 打开页面并嗅探音频地址 |
| `GetWebViewCookiesAsync(url)` | 读取 WebView 中的 Cookie（例如登录后），格式 `a=1; b=2` |
| `GetPref(key)` / `SetPref(key, value)` | 源自己的配置，宿主会持久保存 |
| `Toast(message)` | 界面提示 |
| `Log(message)` | 调试日志，显示在“插件 → 调试日志”里 |
| `ReportProgress(info)` | 加载多页章节时报告进度（例如 `"3 / 20"`），传 `null` 表示结束 |
| `GetCacheDir(subDir)` | 源独立的缓存目录 |
| `Http` | 宿主共享的 `HttpClient`（自动解压、共享 Cookie、跟随系统代理） |
| `DesktopUserAgent` / `MobileUserAgent` | 用户在设置里配置的 UA |

`RequestOptions` 常用字段：

| 字段 | 说明 |
|---|---|
| `Desktop` | true 用电脑版 UA，false（默认）用手机版 UA；`RequestOptions.Pc` / `RequestOptions.Mobile` 是快捷写法 |
| `Method` / `Body` / `ContentType` | POST 等请求，`Body` 默认按表单（`application/x-www-form-urlencoded`）发送 |
| `Headers` | 额外请求头，例如 `Referer`、`Origin` |
| `Encoding` | 强制按指定编码解码（例如 `"gbk"`），默认自动判断 |
| `UseWebViewCookies` | 把 WebView 里的 Cookie（登录、人机验证之后）同步给这次请求 |

## 5. 可选接口

按需实现，宿主会自动识别：

| 接口 | 作用 |
|---|---|
| `IAudioHeaders` | 播放音频时附加请求头（例如 `Referer` 防盗链） |
| `ICoverHeaders` | 加载封面时附加请求头。务必先判断封面地址属于自己，再修改 headers 并返回 true |
| `ILoginSource` | 需要登录的源：提供 `LoginUrl`（`LoginDesktop` 决定用电脑版还是手机版打开）。插件页面会出现“登录”按钮，登录在 WebView 中完成，之后请求时用 `UseWebViewCookies` 带上 Cookie |
| `ISearchVerification` | 搜索需要人机验证：搜索失败或没有结果时，搜索页在这个源旁显示“验证”按钮，用户在弹出的网页里通过验证后窗口自动关闭并重新搜索。`IsVerificationPassed(标题)` 判断是否已通过，`SearchDelaySeconds` 用于网站限制两次搜索最小间隔的情况 |
| `IIncrementalEpisodes` | 增量更新章节，见第 6 节 |
| `IConfigurableSource` | 插件页面出现“设置”按钮，`GetConfigItems()` 返回设置项：`ConfigItem.Text` / `Switch` / `Select` / `Button`。值用 `Host.GetPref(key)` 读取 |
| `ICertificateNameMismatchHosts` | 网站证书有效但域名不匹配（例如 CDN 配置错误）时，声明这些主机名。宿主只对这些主机、且只在“仅域名不匹配”时放行，其它证书错误仍会拒绝 |
| `IPlaybackStateListener` | 接收播放状态：`playing` / `paused` / `stopped` / `error` |
| `ISourceProvider` | 插件入口：程序集里有实现它的类时，宿主只加载它返回的源；没有时自动加载所有公开的 `SourceBase` 子类 |

## 6. 章节很多的书

### 分页章节

章节分很多页的网站，把 `IsMultipleEpisodePages` 设为 true，并按 `loadFullPages` 区分：

- `loadFullPages == false`：只返回第一页章节，要快；
- `loadFullPages == true`：返回全部章节，逐页加载时用 `Host.ReportProgress($"{i} / {total}")` 报告进度，结束时 `Host.ReportProgress(null)`。

第一次打开这类书（本地没有章节缓存）时，宿主会先用 `false` 取第一页立即显示，用户可以马上开始播放，再用 `true` 在后台加载全部章节。

### 增量更新（IIncrementalEpisodes）

宿主会为书架上和播放过的书缓存章节列表。默认每次打开详情页都会整本重新加载；章节很多、或者网站限制访问频率时，实现 `IIncrementalEpisodes`，宿主在有缓存时改为调用：

```csharp
Task<BookDetail?> UpdateEpisodesAsync(string bookUrl, IReadOnlyList<Episode> known, CancellationToken ct);
```

- `known` 是缓存的章节，按网站原始顺序（用户开了“倒序”也会先翻回来）；
- 返回包含完整章节列表的 `BookDetail`；返回 `null` 表示无法增量更新，宿主会退回整本加载。

常见做法：先看详情页上的“最新章节”能否和缓存接上，能接上就只追加新章节；接不上（更新太多，或者上次没加载完整）就从缓存断开的那一页接着加载，不从头来。示例见 `XiaoaiTing2.cs`。

### 访问频率限制

部分网站对访问频率限制很严，返回 HTTP 429（请求过于频繁）后会封一段时间，封禁期间继续请求可能延长封禁。逐页加载时：

- 页与页之间留出间隔，不要并发；
- 遇到 429 等一段时间再试，重试几次仍不行就先返回已加载的部分并用 `Host.Toast` 说明，配合 `IIncrementalEpisodes` 让下次接着加载；
- 调试时注意控制请求次数，测试多了自己会先被封。

## 7. HTML 与 JSON 帮助方法

HTML（AngleSharp 的扩展方法）：

| 方法 | 说明 |
|---|---|
| `Select(selector)` / `SelectFirst(selector)` | 查找所有 / 第一个元素（CSS 选择器） |
| `Text()` | 去掉首尾空白、合并连续空白的文本；元素为 null 时返回空字符串 |
| `OwnText()` | 只取元素自身的文本，不含子元素 |
| `Attr(name)` | 读取属性，不存在时返回空字符串 |
| `AbsUrl(name)` | 读取属性并转成绝对地址 |

`Text()`、`OwnText()`、`Attr()`、`AbsUrl()` 可以在 null 上调用，返回空字符串，所以 `doc.SelectFirst(".intro").Text()` 不需要判空。

JSON（`System.Text.Json.Nodes.JsonNode` 的扩展方法），路径用点分隔，数组下标直接写数字：

```csharp
var json = await Host.GetJsonAsync(url, ct: ct);
var name = json.Str("data.list.0.name");
var count = json.Int("data.total");
foreach (var item in json.Items("data.list")) { ... }
```

## 8. 调试

- “插件”页展开一个源，点“测试”：依次测试分类、搜索、详情和音频解析，结果和 `Host.Log` 的输出都在调试日志里；
- 修改插件后在插件页面重新导入即可。宿主从内存加载 DLL，不会锁定文件；不过已经加载的程序集要重启程序才会被替换。

## 9. 打包与安装

- 单个 dll：在“插件”页点“导入 → 从文件导入”直接选择；
- 有其它依赖：把插件 dll 和依赖放在同一个目录，打成 `MySources.zip` 导入（zip 内可以有一层同名目录）；
- 也可以用“从网址导入”，填 .dll 或 .zip 的下载地址。

导入后的位置：`%AppData%\TingShu\plugins\MySources\MySources.dll`（便携模式为程序目录下 `data\plugins`）。宿主加载插件目录下的 dll，以及每个子目录中与目录同名的 dll，其余 dll 视为依赖。

在“插件包”里点删除会移除整个插件和它包含的所有源（DLL 插件要重启后才彻底移除）；只想停用某个源，关掉它的开关即可。

## 10. 安全提示

DLL 插件是普通 .NET 代码，拥有与本程序相同的权限。**只安装你信任的来源提供的 DLL 插件**。
