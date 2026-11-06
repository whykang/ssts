# 四端通用插件开发指南（Android / iOS / Mac / Windows）

程序本身不带任何源，所有源都由插件提供。四端通用插件是一个 **ZIP 压缩包**，里面是一个 `manifest.json` 和若干 JavaScript 脚本；
一个插件里可以注册任意多个源。在 App 的“插件”页点 `+` →“从文件导入 zip”或“从网址导入”即可安装，重新导入同名插件就是升级。

> Android、iOS、Mac 和 Windows 现已使用同一套 JavaScript 插件格式与接口。同一个 ZIP 插件包可在四端导入，无需分别制作平台专用版本。请保持 `manifest.json`、源的固定 ID 和接口返回结构一致。

本文内嵌完整书籍源、电台直播与按日期/时间排序的回听源、搜索验证和增量更新示例。
MCP 获取指南即可读取全部代码，无需访问本地仓库文件。

## 1. 插件包结构

```
MySources.zip
├─ manifest.json
├─ common.js
└─ sites.js
```

zip 里可以多套一层同名目录。`manifest.json`：

```json
{
  "name": "MySources",
  "version": "1.0.0",
  "author": "作者名",
  "description": "显示在插件页面上的说明",
  "scripts": ["common.js", "sites.js"]
}
```

`scripts` 按顺序执行，所有脚本共用一个全局作用域（前面的脚本定义的函数，后面的可以直接用）。
没有 `manifest.json` 时，按文件名顺序执行包里的所有 `.js`。通过 MCP 创建新草稿时必须显式提供清单。
新书源的 ID 应生成一次后固定写入脚本，不要复用本指南中的示例 ID，也不要每次运行随机生成。发布更新时提升插件版本，保留原有源 ID。

## 2. 编写一个完整书籍源

下面是可以单独作为草稿的两个文件。示例域名 `api.example.invalid` 和返回字段是教学用约定，
制作真实插件时需替换为目标网站的公开接口及实际字段。示例所用函数全部定义在正文内，不依赖仓库里的其他脚本。

`manifest.json`：

```json
{
  "name": "GuideLibrary",
  "version": "1.0.0",
  "scripts": ["source.js"]
}
```

`source.js`（假设分类接口返回 `{items:[{id,title,cover,author}],page,pages}`，
详情接口返回 `{title,intro,cover,author,artist,status}`，章节接口返回 `{items:[{id,title}],pages}`，
音频接口返回 `{url,type}`）：

```js
// GUIDE_EXAMPLE: library-source
(function () {
  var API = 'https://api.example.invalid';
  var REFERER = 'https://www.example.invalid/';
  function get(host, path) {
    return host.getJson(API + path, { headers: { Referer: REFERER } });
  }
  function idFrom(url, prefix) {
    if (url.indexOf(prefix) !== 0) throw new Error('不认识的地址');
    return url.substring(prefix.length);
  }
  function result(data) {
    var list = (data.items || []).map(function (row) {
      return { bookUrl: 'guide:book:' + row.id, title: row.title,
        coverUrl: row.cover || '', author: row.author || '' };
    });
    return { books: list, currentPage: Number(data.page) || 1,
      totalPage: Number(data.pages) || 1 };
  }
  registerSource({
    id: '615d42e6839d487aac45feb80d957e16',
    name: '教学书籍源', url: REFERER, multipleEpisodePages: true,
    search: function (keywords, page) {
      return result(get(this.host, '/search?q=' + encodeURIComponent(keywords) + '&page=' + (page || 1)));
    },
    categoryMenus: function () {
      return [{ title: '分类', tabs: [
        { title: '最新', url: 'guide:category:latest:1' }
      ] }];
    },
    categoryPage: function (url) {
      var args = idFrom(url, 'guide:category:').split(':');
      var page = Number(args[1]) || 1;
      var data = result(get(this.host, '/categories/' + encodeURIComponent(args[0]) + '?page=' + page));
      data.nextUrl = page < data.totalPage ? 'guide:category:' + args[0] + ':' + (page + 1) : null;
      return data;
    },
    bookDetail: function (url, loadEpisodes, loadFullPages) {
      var host = this.host, id = idFrom(url, 'guide:book:');
      var data = get(host, '/books/' + encodeURIComponent(id));
      var detail = { title: data.title, intro: data.intro || '', coverUrl: data.cover || '',
        author: data.author || '', artist: data.artist || '', status: data.status || '', episodes: [] };
      if (!loadEpisodes) return detail;
      var page = 1, pages = 1, seen = {};
      try {
        do {
          host.progress(page + ' / ' + pages);
          var response = get(host, '/books/' + encodeURIComponent(id) + '/episodes?page=' + page);
          pages = Number(response.pages) || 1;
          (response.items || []).forEach(function (row) {
            if (!row.id || seen[row.id]) return;
            seen[row.id] = true;
            detail.episodes.push({ title: row.title, url: 'guide:episode:' + row.id });
          });
          page++;
          if (!loadFullPages) break;
          if (page <= pages) host.sleep(150);
        } while (page <= pages);
      } finally { host.progress(null); }
      return detail;
    },
    audio: function (url) {
      var id = idFrom(url, 'guide:episode:');
      // 每次播放重新取地址，避免把短期签名 URL 当作稳定章节 ID。
      var data = get(this.host, '/episodes/' + encodeURIComponent(id) + '/audio');
      if (!/^https?:\/\//.test(data.url || '')) throw new Error('未获取到有效音频');
      var headers = { Referer: REFERER };
      if (data.type === 'hls') headers['X-Media-Type'] = 'hls';
      return { url: data.url, headers: headers };
    }
  });
})();
```

`X-Media-Type: hls` 是给宿主的类型提示。普通请求选项及音频返回的 `headers` 都是对象，
不是拼接后的字符串。不要把 `audio` 的临时地址写回 `episode.url`。只做分类浏览的源可省略 `search` 方法。

所有方法都在后台线程执行，而且是**同步**的：网络请求直接拿返回值，不需要 `await` / 回调。
Android 端使用 Rhino（ES5 加上部分 ES6 支持）。编写四端通用插件时，建议采用 ES5 语法，避免依赖 `class`、`async`、展开运算符、可选链等语法或平台专有能力。
脚本只能使用宿主提供的对象，不能访问 Java 类和文件系统。

### 源对象的成员

| 成员 | 说明 |
|---|---|
| `id` | 源的唯一 ID，书架、设置、章节缓存都以它为准，发布后不要改 |
| `name` / `url` / `description` | 名称、网址、说明（显示在插件页面） |
| `group` | 分组，默认“听书” |
| `multipleEpisodePages` | 章节分多页，见第 6 节 |
| `enabledByDefault` | 首次导入时是否启用，默认 true |
| `search(keywords, page)` | 搜索，返回 `{ books, totalPage }`；不知道总页数时，确定有下一页就返回 `page + 1`。不写这个方法就不参与聚合搜索 |
| `categoryMenus()` | 分类菜单：`[{ title, tabs: [{ title, url }] }]`。不写就不出现在“发现”页 |
| `categoryPage(url)` | 分类的一页书：`{ books, currentPage, totalPage, nextUrl }`，`nextUrl` 为空表示没有下一页 |
| `bookDetail(bookUrl, loadEpisodes, loadFullPages)` | 详情与章节：`{ episodes, intro, author, artist, coverUrl, title, status }`，除章节外的字段不为空时会补充到书籍信息里 |
| `audio(episodeUrl)` | 把章节地址解析成音频地址，见第 3 节。不写表示章节地址本身就是音频 |
| `host` | 宿主注入的能力，见第 4 节 |

书：`{ coverUrl, bookUrl, title, author, artist, intro, status }`，`bookUrl` 在同一个源内必须唯一。
章节：`{ title, url, isFree, live, broadcastDate, startTime }`，`isFree` 默认 true；`live: true` 表示这是直播流（电台），宿主会按直播来播放：不显示进度、不能拖动和下载。

广播回听可提供 `broadcastDate`（`YYYY-MM-DD`，节目所属日期）和 `startTime`（Unix 毫秒，节目开始时间）。宿主优先用日期字段按天分组，直播单独显示；没有日期字段时，可用开始时间确定日期。旧云听插件的“11月1日 08:00 节目名”标题仍兼容按天分组。普通小说无需填写这两个字段。

分类标签的地址不一定是网址，可以是任意字符串，宿主只会把它原样传回 `categoryPage`，例如 `my-search:关键词#1`。

### 电台直播、回听及按日期/时间排列（完整示例）

同一个广播源可同时提供直播与回听，返回普通的 `episodes` 数组即可：

- `live: true`：直播项，单独放在直播入口，不填写回听日期和开始时间。
- `broadcastDate`：节目所属日期，严格使用 `YYYY-MM-DD`，由电台时区和节目单决定。
- `startTime`：实际节目开始时间，Unix **毫秒**数值；接口若返回 Unix 秒，需乘 `1000`。
- 标题可带 `HH:mm`，便于用户查找；日期分组依靠字段，不能只在标题里写日期。
- 插件按日期从新到旧、同日开始时间从早到晚排序后返回。宿主按天组织目录，同日保留传入顺序。
- `group: '广播'` 用于书源分类，日期/时间字段用于章节目录分组。`endTime` 不属于此处的章节分组协议。
- 日期字段优先；只提供开始时间时，宿主可能按设备时区推算日期。填写两者可避免跨时区时分错天。
- 跨午夜节目使用真实开始日期计算 `startTime`，所属分组日仍以节目单给出的 `broadcastDate` 为准。
- 稳定节目 ID 放入 `url`；播放时再解析签名地址。不要用数组下标、节目标题或临时签名 URL 标识回听节目。

`manifest.json`：

```json
{"name":"GuideRadio","version":"1.0.0","scripts":["radio.js"]}
```

下面假设接口如下，真实网站需按其返回结构适配：

- `/stations` → `{items:[{id,name,cover}]}`。
- `/stations/1` → `{name,intro,cover}`。
- `/stations/1/days` → `{days:["2026-11-06","2026-11-05"]}`。
- `/stations/1/schedule?day=2026-11-06` → `{items:[{id,title,date:"2026-11-06",time:"08:00"}]}`；
  `date` 是实际开始日，`day` 是节目所属分组日。若存在 `startTimeMs`，优先使用接口的毫秒时间。
- `/stations/1/live`、`/replays/节目ID/audio` → `{url,type}`；`type` 可为 `hls`。

`radio.js`：

```js
// GUIDE_EXAMPLE: radio-source
(function () {
  var API = 'https://radio.example.invalid';
  var REFERER = 'https://radio.example.invalid/';
  function get(host, path) {
    return host.getJson(API + path, { headers: { Referer: REFERER } });
  }
  // 示例电台为 UTC+8，不能依赖手机/电脑当前时区，也不依赖 Date.parse 的实现差异。
  function startMs(day, time) {
    var d = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day || '');
    var t = /^(\d{2}):(\d{2})$/.exec(time || '');
    if (!d || !t || Number(t[1]) > 23 || Number(t[2]) > 59) throw new Error('节目时间无效');
    var base = Date.UTC(Number(d[1]), Number(d[2]) - 1, Number(d[3]));
    var check = new Date(base);
    if (check.getUTCFullYear() !== Number(d[1]) || check.getUTCMonth() !== Number(d[2]) - 1 || check.getUTCDate() !== Number(d[3])) throw new Error('节目日期无效');
    return base + (Number(t[1]) * 60 + Number(t[2]) - 8 * 60) * 60000;
  }
  function stationId(url) {
    var prefix = 'guide-radio:station:';
    if (url.indexOf(prefix) !== 0) throw new Error('电台地址无效');
    return url.substring(prefix.length);
  }
  registerSource({
    id: '48a3adfb740c4bc691ac80a03d648518',
    name: '教学电台源', url: REFERER, group: '广播', multipleEpisodePages: true,
    config: [{ type: 'select', key: 'days', label: '回听天数', default: '7', options: ['7', '30'] }],
    categoryMenus: function () {
      return [{ title: '电台', tabs: [{ title: '全部电台', url: 'guide-radio:stations' }] }];
    },
    categoryPage: function () {
      var data = get(this.host, '/stations');
      return { books: (data.items || []).map(function (row) {
        return { title: row.name, coverUrl: row.cover || '', bookUrl: 'guide-radio:station:' + row.id };
      }), currentPage: 1, totalPage: 1, nextUrl: null };
    },
    bookDetail: function (url, loadEpisodes, loadFullPages) {
      var host = this.host, id = stationId(url);
      var data = get(host, '/stations/' + encodeURIComponent(id));
      var detail = { title: data.name, intro: data.intro || '', coverUrl: data.cover || '', episodes: [] };
      if (!loadEpisodes) return detail;
      detail.episodes.push({ title: '直播', url: 'guide-radio:live:' + id, live: true });
      var keep = Number(host.getPref('days', '7')) === 30 ? 30 : 7;
      var days = (get(host, '/stations/' + encodeURIComponent(id) + '/days').days || []).slice();
      days.sort().reverse();
      days = days.slice(0, loadFullPages ? keep : 1);
      var replays = [], seen = {};
      try {
        for (var i = 0; i < days.length; i++) {
          var day = days[i];
          host.progress((i + 1) + ' / ' + days.length);
          var rows = get(host, '/stations/' + encodeURIComponent(id) + '/schedule?day=' + encodeURIComponent(day)).items || [];
          rows.forEach(function (row) {
            if (!row.id || seen[row.id]) return;
            seen[row.id] = true;
            var actualDay = row.date || day;
            var start = Number(row.startTimeMs) || startMs(actualDay, row.time);
            replays.push({ title: (row.time ? row.time + ' ' : '') + row.title,
              url: 'guide-radio:replay:' + row.id, live: false, isFree: true,
              broadcastDate: day, startTime: start });
          });
          if (i + 1 < days.length) host.sleep(150);
        }
      } finally { host.progress(null); }
      replays.sort(function (a, b) {
        if (a.broadcastDate !== b.broadcastDate) return a.broadcastDate < b.broadcastDate ? 1 : -1;
        return a.startTime - b.startTime;
      });
      detail.episodes = detail.episodes.concat(replays);
      return detail;
    },
    audio: function (url) {
      var livePrefix = 'guide-radio:live:', replayPrefix = 'guide-radio:replay:', path;
      if (url.indexOf(livePrefix) === 0) path = '/stations/' + encodeURIComponent(url.substring(livePrefix.length)) + '/live';
      else if (url.indexOf(replayPrefix) === 0) path = '/replays/' + encodeURIComponent(url.substring(replayPrefix.length)) + '/audio';
      else throw new Error('节目地址无效');
      var data = get(this.host, path);
      if (!/^https?:\/\//.test(data.url || '')) throw new Error('原站暂无可用音频');
      var headers = { Referer: REFERER };
      if (data.type === 'hls') headers['X-Media-Type'] = 'hls';
      return { url: data.url, headers: headers };
    }
  });
})();
```

此例先显示直播和最新一天，后台全量加载时补齐配置范围内的其他日期。
例如输入节目单包含 11 月 6 日 09:00、08:00 及 11 月 5 日 18:00，返回顺序为：
直播 → 11 月 6 日组（08:00、09:00）→ 11 月 5 日组（18:00）。
旧插件未提供日期字段时继续沿用宿主原有目录逻辑；旧云听的日期标题兼容保留。新插件应显式填字段。

## 3. 音频提取

`audio(episodeUrl)` 返回音频地址字符串，或者 `{ url, headers }`。常见写法：

| 场景 | 写法 |
|---|---|
| 章节地址本身就是音频 | 不写 `audio` |
| 从章节网页的 HTML 里取 | `this.host.getHtml(url).first('audio').absUrl('src')` |
| 章节地址返回 JSON | `str(this.host.getJson(url), 'data.url')` |
| 要执行页面脚本才有地址 | `this.host.render(url, { desktop, script, parse })`：用 WebView 打开页面，反复执行 `script`（默认返回整个 HTML），直到 `parse(结果)` 返回非空 |
| 嗅探页面发出的音频请求 | `this.host.sniff(url, { desktop, validate, script })`：`validate(地址)` 判断哪个请求是音频（默认按扩展名），`script` 是可选的触发播放脚本（宿主也会自动点击常见播放器的播放按钮） |

播放地址带时效签名的，就在 `audio` 里每次现取。WebView 方式比普通请求慢得多，能用普通请求就不要用 WebView。
取不到时直接 `throw new Error('原因')`，原因会显示给用户。

播放时宿主会附带 UA 和 `audioHeaders` 提供的请求头；播放前会先探测地址，404 / 410 会提示“音频文件已失效”。

### 常见音频解析代码（四选一合并到源的 audio）

下面的四个方法各自可用于源的 `audio`，例如 `audio: GuideAudioExtractors.jsonAudio`。
网页选择器、JSON 字段及媒体域名需按真实站点修改，优先使用普通 HTTP 请求。

```js
// GUIDE_EXAMPLE: audio-extractors
var GuideAudioExtractors = (function () {
  function wrapped(url, referer, type) {
    if (!/^https?:\/\//.test(url || '')) throw new Error('未获取到可用音频');
    var headers = { Referer: referer };
    if (type === 'hls' || /\.m3u8(?:[?#]|$)/i.test(url)) headers['X-Media-Type'] = 'hls';
    return { url: url, headers: headers };
  }
  return {
    htmlAudio: function (episodeUrl) {
      var doc = this.host.getHtml(episodeUrl);
      return wrapped(doc.first('audio[src], audio source[src]').absUrl('src'), episodeUrl);
    },
    jsonAudio: function (episodeUrl) {
      var data = this.host.getJson(episodeUrl);
      // 示例接口：{data:{url:'https://...',type:'hls'}}。
      return wrapped(data.data && data.data.url, episodeUrl, data.data && data.data.type);
    },
    renderedAudio: function (episodeUrl) {
      var host = this.host;
      var url = host.render(episodeUrl, { desktop: false, timeout: 20000,
        parse: function (html) {
          return host.parseHtml(html, episodeUrl).first('audio[src], audio source[src]').absUrl('src') || null;
        }
      });
      return wrapped(url, episodeUrl);
    },
    sniffedAudio: function (episodeUrl) {
      var url = this.host.sniff(episodeUrl, { desktop: false, timeout: 20000,
        validate: function (candidate) {
          // 按真实媒体域名及请求特征筛选，避免把广告音频当成节目。
          return /^https:\/\/media\.example\.invalid\/.*\.(?:mp3|m4a|m3u8)(?:[?#]|$)/i.test(candidate);
        }
      });
      return wrapped(url, episodeUrl);
    }
  };
})();
```

## 4. 宿主能力（this.host）

| 成员 | 说明 |
|---|---|
| `getString(url, options)` | 请求文本，自动识别 GBK / UTF-8 等编码；非 2xx 状态码会抛出错误，`e.status` 是状态码 |
| `getHtml(url, options)` | 请求并解析 HTML，返回文档元素（见第 7 节） |
| `getJson(url, options)` | 请求并解析 JSON（兼容 JSONP） |
| `getStrings(urls, options, concurrency)` | 并发请求多个地址，返回文本数组，失败的位置是 `null` |
| `finalUrl(url, options)` | 跟随跳转后的最终地址 |
| `fetch(url, options)` | 请求并返回 `{ status, body }`；和 `getString` 不同，非 2xx 状态码不会抛出错误（有的接口把错误原因放在响应内容里） |
| `status(url, options)` | 只返回 HTTP 状态码，不下载内容（探测音频地址能不能访问时用，记得带上 `Range` 头） |
| `parseHtml(html, baseUrl)` | 解析一段 HTML |
| `render(url, { desktop, script, parse, timeout })` | 用 WebView 加载页面，见第 3 节。`timeout` 单位毫秒，默认 25000 |
| `sniff(url, { desktop, validate, script, timeout })` | 用 WebView 打开页面并嗅探音频地址 |
| `cookies(url)` | 某个地址当前的 Cookie，格式 `a=1; b=2` |
| `getPref(key, 默认值)` / `setPref(key, value)` | 源自己的配置，宿主会持久保存 |
| `toast(message)` | 界面提示 |
| `log(message)` | 调试日志，显示在插件页面的“日志”里 |
| `progress(info)` | 加载多页章节时报告进度（例如 `"3 / 20"`），传 `null` 表示结束 |
| `sleep(毫秒)` | 等待 |
| `readCache(name)` / `writeCache(name, text)` / `cacheAge(name)` | 源独立的缓存文件；`cacheAge` 返回距今多少毫秒，不存在为 -1 |
| `base64Decode` / `base64Encode` / `md5` / `sha256` / `uuid()` / `htmlDecode` | 常用工具 |
| `aesEcbDecrypt(data, keyHex)` | AES-ECB 解密（PKCS7 填充）：`data` 是 base64，`keyHex` 是十六进制密钥，返回文本；失败返回空字符串 |
| `userAgent(desktop)` | 用户在设置里配置的 UA |

请求选项 `options`：

| 字段 | 说明 |
|---|---|
| `desktop` | true 用电脑版 UA，false（默认）用手机版 UA |
| `method` / `body` / `contentType` | POST 等请求，`body` 默认按表单（`application/x-www-form-urlencoded`）发送 |
| `headers` | 额外请求头，例如 `Referer`、`Origin`、`Cookie` |
| `encoding` | 强制按指定编码解码（例如 `"gbk"`），默认自动判断 |

**Cookie 是共享的**：普通请求和 App 里的 WebView（登录页、验证页、`render` / `sniff`）用同一份 Cookie，
用户登录或通过人机验证之后，后续请求自动带上，不需要额外处理。

## 5. 可选成员

| 成员 | 作用 |
|---|---|
| `audioHeaders(audioUrl)` | 播放音频时附加请求头（例如 `Referer` 防盗链），返回 `{ ... }` 或 `null` |
| `coverHeaders(coverUrl)` | 加载封面时附加请求头，返回 `{ ... }` 或 `null`。先判断封面地址属于自己 |
| `loginUrl` / `loginDesktop` | 需要登录的源：插件页面会出现“登录”按钮，在 App 内的网页里完成登录 |
| `checkLogin()` | 配合 `loginUrl`：登录页关闭后宿主会调用它确认是否登录成功。已登录时返回昵称（或 `true`），插件页会显示“已登录”；没登录时抛出错误。不写这个方法，宿主就不知道登录状态，只显示“登录”按钮 |
| `searchVerificationUrl(keywords)` | 搜索需要人机验证：搜索失败或没有结果时，搜索页会出现“验证”按钮，验证页识别到书籍结果后直接返回；无法识别时由用户手动完成并仅重搜一次。配合 `searchVerificationDesktop`（默认 true）、`searchDelaySeconds`（网站限制两次搜索最小间隔时用） |
| `searchVerificationResult(html, url, keywords)` | 可选：只解析当前验证页的 HTML，明确识别到结果时返回 `{ verified: true, books, totalPage }`，否则返回 `null`。不发起网络请求；结果会直接展示，无需再次搜索。完整示例见下文 |
| `updateEpisodes(bookUrl, known)` | 增量更新章节，见第 6 节 |
| `config` | 设置项数组，插件页面会出现“设置”按钮：`{ type: 'text' | 'switch' | 'select', key, label, default, options }`，值用 `this.host.getPref(key)` 读取 |
| `onPlaybackState(state)` | 接收播放状态：`playing` / `paused` / `stopped` / `error` |

### 搜索验证后直接返回结果（可合并的方法对象）

下面定义的 `GuideVerifiedSearch` 不额外注册源。将它的四个方法/属性合并进真实源的
`registerSource({...})`，并替换域名和选择器即可。示例结果结构是
`#results .book` 内含 `a.title`、`img`，下一页由 `a.next` 标识。

```js
// GUIDE_EXAMPLE: verified-search
var GuideVerifiedSearch = (function () {
  var SITE = 'https://search.example.invalid/';
  function urlFor(keywords, page) {
    return SITE + 'search?q=' + encodeURIComponent(keywords) + '&page=' + (page || 1);
  }
  function parse(host, html, url, page) {
    var document = host.parseHtml(html, url), books = [], seen = {};
    document.select('#results .book').forEach(function (item) {
      var a = item.first('a.title'), bookUrl = a.absUrl('href');
      if (bookUrl.indexOf(SITE) !== 0 || !a.text() || seen[bookUrl]) return;
      seen[bookUrl] = true;
      books.push({ title: a.text(), bookUrl: bookUrl, coverUrl: item.first('img').absUrl('src') });
    });
    return { books: books, totalPage: document.first('a.next').exists() ? page + 1 : page };
  }
  return {
    searchVerificationDesktop: false,
    searchVerificationUrl: function (keywords) { return urlFor(keywords, 1); },
    search: function (keywords, page) {
      page = page || 1;
      var url = urlFor(keywords, page), html = this.host.getString(url);
      var result = parse(this.host, html, url, page);
      if (!result.books.length && this.host.parseHtml(html, url).first('.captcha').exists()) {
        throw new Error('请点击该书源旁的验证按钮');
      }
      return result;
    },
    searchVerificationResult: function (html, url, keywords) {
      if (url.indexOf(SITE + 'search?') !== 0) return null;
      var q = /[?&]q=([^&#]*)/.exec(url), value;
      if (!q) return null;
      try { value = decodeURIComponent(q[1].replace(/\+/g, ' ')); }
      catch (e) { return null; }
      if (value !== keywords) return null;
      var page = Number((/[?&]page=(\d+)/.exec(url) || [0, 1])[1]);
      var result = parse(this.host, html, url, page);
      if (!result.books.length) return null;
      // 只解析可见验证页；此方法不要调用 getString/getJson/render 或重新提交搜索。
      return { verified: true, books: result.books, totalPage: result.totalPage };
    }
  };
})();
```

不要仅凭标题变化、验证码输入框消失或出现“成功”文字就返回 `verified: true`。
明确解析到本次关键词的书籍结果再返回；即使页面残留验证码控件，也可接受这些真实结果。
没有确定结果时返回 `null`，由宿主保留验证页或处理用户的手动完成操作，插件不发起循环搜索。

## 6. 章节很多的书

### 分页章节

章节分很多页的网站，把 `multipleEpisodePages` 设为 true，并按 `loadFullPages` 区分：

- `loadFullPages == false`：只返回第一页章节，要快；
- `loadFullPages == true`：返回全部章节，逐页加载时用 `host.progress(i + ' / ' + total)` 报告进度，结束时 `host.progress(null)`（放在 `finally` 里）。

第一次打开这类书时，宿主会先用 `false` 取第一页立即显示，用户可以马上开始播放，再用 `true` 在后台加载全部章节。

### 增量更新（updateEpisodes）

宿主会为书架上和播放过的书缓存章节列表。默认每次打开详情页都会整本重新加载；章节很多、或者网站限制访问频率时，
实现 `updateEpisodes(bookUrl, known)`：`known` 是缓存的章节（按网站原始顺序），返回包含完整章节列表的详情；返回 `null` 表示无法增量更新，宿主会退回整本加载。
可将下面的 `GuideIncrementalUpdate` 方法加入源对象。这里假设更新接口返回
`{reset:false,episodes:[{title,url}]}`，其 `episodes` 已使用与原目录相同的稳定章节 URL。
接口要求全量重建时返回 `reset:true`，插件返回 `null` 交给宿主重新加载。

```js
// GUIDE_EXAMPLE: incremental-update
function GuideIncrementalUpdate(bookUrl, known) {
  known = known || [];
  if (!known.length) return null;
  var last = known[known.length - 1].url;
  var data = this.host.getJson('https://api.example.invalid/updates?book=' +
    encodeURIComponent(bookUrl) + '&after=' + encodeURIComponent(last));
  if (data.reset === true || !Array.isArray(data.episodes)) return null;
  var merged = known.slice(), seen = {};
  known.forEach(function (episode) { seen[episode.url] = true; });
  data.episodes.forEach(function (episode) {
    if (!episode.url || seen[episode.url]) return;
    seen[episode.url] = true;
    merged.push(episode);
  });
  return { episodes: merged };
}
// 在源对象中设置：updateEpisodes: GuideIncrementalUpdate
```

### 访问频率限制

部分网站返回 HTTP 429 后会封一段时间。逐页加载时页与页之间用 `host.sleep` 留出间隔；遇到 429（`e.status === 429`）等一段时间再试，
重试几次仍不行就先返回已加载的部分并用 `host.toast` 说明，配合 `updateEpisodes` 让下次接着加载。

## 7. HTML 与 JSON 辅助

HTML 元素（`getHtml` / `parseHtml` 的返回值，以及 `select` 出来的每一项）：

| 方法 | 说明 |
|---|---|
| `select(selector)` | 查找所有元素（CSS 选择器），返回普通的 JS 数组，可以 `map` / `filter` / `forEach` |
| `first(selector)` | 查找第一个元素。找不到时返回一个“空元素”，可以继续调用下面的方法 |
| `exists()` | 元素是否存在 |
| `text()` / `ownText()` | 去掉首尾空白、合并连续空白的文本 / 只取元素自身的文本 |
| `attr(name)` / `absUrl(name)` | 读取属性 / 读取属性并转成绝对地址，不存在返回空字符串 |
| `html()` / `outerHtml()` / `tag()` / `hasClass(name)` | 内部 HTML / 完整 HTML / 标签名 / 是否有某个 class |
| `children()` / `parent()` / `byId(id)` | 子元素数组 / 父元素 / 按 id 在整个文档里找 |

所以 `doc.first('.intro').text()` 不需要判空。

JSON 用全局函数读取，路径用点分隔，数组下标直接写数字：

```js
var json = this.host.getJson(url);
var name = str(json, 'data.list.0.name');   // 字符串，不存在返回 ''
var count = int(json, 'data.total', 0);      // 整数
items(json, 'data.list').forEach(function (item) { /* ... */ });   // 数组，不存在返回 []
at(json, 'data.pageQuery');                  // 任意节点，不存在返回 null
```

另有 `regexInt(文本, /正则(\d+)/, 默认值)` 和 `parseJson(text)`。

## 8. 调试

- **在 Android 源码工程中测试**：`./gradlew testDebugUnitTest -Donline=true` 会加载 `plugins/ShunSources`，逐个源测试分类、搜索、详情和音频解析，
  结果在 `app/build/plugin-report.txt`；加 `-Dsources=哔哩哔哩,酷我畅听` 只跑指定的源。需要 WebView 的音频解析应在目标端应用的实际环境中验证。
- **在应用中**：插件页面点开一个源 →“测试”，结果和 `host.log` 的输出都在“日志”里。

## 9. 打包

以下命令在 Android 源码工程中执行；也可直接用 ZIP 工具打包四端通用插件。

```bash
./gradlew packPlugins
```

会把 `plugins/` 下的每个目录打成 `plugins/dist/<名字>.zip`。也可以直接用任意压缩软件把 `manifest.json` 和脚本压成 zip。

## 10. 安全提示

插件脚本运行在受限环境里（不能访问 Java 类和文件系统），但它可以访问网络、读取 App 内网页的 Cookie。**只导入你信任的来源提供的插件**。

## MCP 局域网开发

安卓、iOS 与 Mac 可在“设置 → MCP 插件开发”中开启 MCP 服务，让 AI 制作草稿、调用目标设备实际引擎测试、安装并导出 ZIP。
通过 MCP 制作和导出的 ZIP 同样可用于 Android、iOS、Mac、Windows 四端。网络、WebView 和播放行为应在目标端验证。
### MCP 实际调用示例

以下是工具名称及对应参数。按顺序调用；`plugin_write.files` 必须包含草稿的全部文件，
不是补丁。下面是可离线校验和测试分类方法的最小草稿，音频地址为教学占位地址：

`plugin_write`：

```json
{
  "id": "guide-demo",
  "files": {
    "manifest.json": "{\"name\":\"GuideDemo\",\"version\":\"1.0.0\",\"scripts\":[\"source.js\"]}",
    "source.js": "registerSource({id:'9fe2b5b492004a3fbd2e9ce1db72576c',name:'最小示例',categoryMenus:function(){return [{title:'分类',tabs:[{title:'示例',url:'demo:books'}]}];},categoryPage:function(){return {books:[{title:'示例书',bookUrl:'demo:book:1'}],currentPage:1,totalPage:1};},bookDetail:function(url,loadEpisodes){return {episodes:loadEpisodes?[{title:'第一集',url:'https://audio.example.invalid/1.mp3'}]:[]};},audio:function(url){return url;}});"
  }
}
```

`plugin_validate`：

```json
{"id":"guide-demo"}
```

`plugin_test`：

```json
{"id":"guide-demo","sourceId":"9fe2b5b492004a3fbd2e9ce1db72576c","method":"categoryMenus","arguments":[]}
```

```json
{"id":"guide-demo","sourceId":"9fe2b5b492004a3fbd2e9ce1db72576c","method":"bookDetail","arguments":["demo:book:1",true,true]}
```

```json
{"id":"guide-demo","sourceId":"9fe2b5b492004a3fbd2e9ce1db72576c","method":"audio","arguments":["https://audio.example.invalid/1.mp3"]}
```

实际网站的请求、目录、音频数据均验证通过后，可调用 `plugin_install`：

```json
{"id":"guide-demo"}
```

`plugin_export` 导出草稿：

```json
{"id":"guide-demo","target":"draft"}
```

工具结果的 `value.base64` 解码后是 ZIP。安装需要应用允许远程安装；测试调用不等于播放验证。
草稿偏好与正式插件隔离，每次 `plugin_test` 都重新加载草稿，不要依赖上一次测试留下的全局变量。
