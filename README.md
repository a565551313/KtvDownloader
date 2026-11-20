# KtvDownloader

> 📘 **接手本项目请先读 [docs/技术交接文档.md](docs/技术交接文档.md)**
> —— 包含项目来源、6 组外部接口完整参数、13 个已知坑、服务层 API 清单。

麦动 KTV 曲库下载器 —— 基于 .NET Framework 4.8 + WinForms 的桌面工具。
支持搜索歌曲、批量下载、音轨分离（原唱/伴奏）、试听预览、歌单与收藏。

> ⚠️ **使用前请读[「合法性与免责」](#合法性与免责)**。

## 环境要求

| 项 | 说明 |
|---|---|
| 操作系统 | Windows 10 / 11（**64 位**，工程锁定 x64） |
| .NET Framework | 4.8（系统自带） |
| Visual Studio | 2022 或更高（用于打开工程、拖拽界面） |
| 曲库 `muse.db` | 约 983MB，程序内可一键从官方服务下载（约 418MB 压缩包） |
| `ffmpeg.exe` | **不在仓库里**，需自行放入 `tools/`（转码与音轨分离才用得到） |
| libVLC | 由 NuGet 自动还原（`VideoLAN.LibVLC.Windows`，约 198MB），预览播放用 |

磁盘空间：曲库更新时峰值需要约 **3GB** 空闲（下载包 + 解压出的曲库 + 旧曲库备份），
详见交接文档 §9.5。

## 快速开始

### 1. 打开工程

双击 `KtvDownloader.sln`，用 Visual Studio 打开。

### 2. 编译

```
生成 → 生成解决方案（Ctrl+Shift+B）
```

或用仓库自带的脚本（自动定位 MSBuild，并先结束残留进程 —— 交接文档 §坑13）：

```powershell
.\scripts\build.ps1                     # Release
.\scripts\build.ps1 -Configuration Debug
.\scripts\build.ps1 -Version 1.2.3      # 打指定版本号
.\scripts\run-tests.ps1                 # 构建 + 跑单元测试
```

手工命令行等价：

```powershell
msbuild KtvDownloader.sln -t:restore;build -p:Configuration=Release -p:Platform="Any CPU" -m
```

输出：`src\KtvDownloader\bin\Release\net48\KtvDownloader.exe`

> **仓库里没有 `tools\ffmpeg.exe`**（47MB，已被 .gitignore 排除），但缺它**也能编译**：
> csproj 里的引用带了 `Condition="Exists('..\..\tools\ffmpeg.exe')"`。
> 没有 ffmpeg 时搜索、下载、预览都正常，只是转 MP4 与音轨分离不可用；
> `MediaService` 会按「配置路径 → `{exe目录}\tools` → `{exe目录}` → 系统 PATH」的顺序自动查找。

### 3. 运行

首次运行会提示配置曲库路径（也可以直接让程序下载官方曲库）。之后可通过 `文件 → 设置` 调整。

配置项：

| 项 | 说明 |
|---|---|
| 曲库 | `muse.db` 的完整路径 |
| 下载目录 | 歌曲输出位置 |
| ffmpeg | `ffmpeg.exe` 路径，留空则自动查找（改完立即生效，不会打断正在下载的任务） |
| 同时下载数 | 1-8，默认 2（改完立即生效） |
| 默认清晰度 | 480 / 720 / 1080（源站多数只有 480p） |
| 音轨处理 | 保留双轨 / 仅原唱 / 仅伴奏 / 各一份 |
| 每页条数 | 10-500，默认 50 |
| 预览时长 | 0 = 完整下载，否则 10-600 秒，默认 60 |

`config.json` 里还可以直接改 `LogLevel`（`Debug` / `Info` / `Warn` / `Error`），
设成 `Warn` 能少写约七成日志行。

配置文件是**原子写入**的（`.tmp` + `File.Replace`），进程被强杀也不会把它留成 0 字节；
非法值会被 `AppConfig.Normalize()` 夹回合法范围。

### 4. 界面能做什么

- **搜索**：按歌名 / 按歌手，支持「歌名 歌手」组合写法（顺序写反也能纠正）。
  按歌手搜索同时扫**关联表**和**歌名**，所以「发如雪（Cover：周杰伦）」这类
  歌手字段标着「佚名」的翻唱版也能搜到 —— 一线歌手大多没有原唱授权，
  只查关联表的话搜「周杰伦」只能出 2 首
- **浏览**：榜单、新歌、分类（语种 / 标签）、收藏
- **预览**：双击结果即可试听，播放中可实时切换原唱 / 伴奏（libVLC，不转码，首屏接近 0 秒）。
  双音轨的歌切音轨，老式单音轨的歌切左右声道，界面自动判断
- **下载**：多选批量入队，每首歌独立进度；队列右键可取消、打开所在目录、收藏
- **收藏**：搜索结果或队列右键 →「添加收藏 / 取消收藏」，按歌号存本地，曲库更新后依然有效
- **曲库更新**：`工具 → 检查更新`，带进度与取消
- **叠加层**：`文件 → 叠加层设置（水印 / 片头）`，往画面上叠台标图片或文字卡片，
  可拖动定位、九宫格预设、指定出现/消失的时间段（例：`0:10` 出现、`0:15` 消失）。
  **预览时只是叠在上面看效果，不改动文件；只有下载时才真烧进视频**

## 目录结构

```
KtvDownloader/
├── KtvDownloader.sln
├── .editorconfig                 # 代码规范（VS 自动应用）
├── .gitignore
├── README.md
├── docs/技术交接文档.md           # 交接文档（接口清单 / 17 个坑 / API 说明）
├── scripts/
│   ├── build.ps1                 # 构建（vswhere 定位 MSBuild + 处理坑13）
│   └── run-tests.ps1             # 构建 + 跑单元测试
├── .github/workflows/windows-ci.yml
├── src/KtvDownloader/
│   ├── KtvDownloader.csproj      # SDK-style, net48, x64
│   ├── Program.cs                # 入口 + 全局异常兜底（含未观察的 Task 异常）
│   ├── Properties/AssemblyInfo.cs # 只放 ComVisible / Guid / InternalsVisibleTo
│   ├── Forms/                    # 窗体（.cs + .Designer.cs 分离）
│   │   ├── MainForm.cs           # 主界面
│   │   ├── PreviewForm.cs        # 预览播放（libVLC）
│   │   ├── OverlayEditorForm.cs  # 叠加层编辑器（纯代码构建，无 Designer）
│   │   ├── SettingsForm.cs / PlaylistForm.cs / CategoryForm.cs / DatabaseUpdateForm.cs
│   ├── Controls/                 # BrowseToolBar / PaginationBar / PaginationMath
│   │                             # OverlayRenderer / OverlayDesignSurface / OverlayLayeredWindow
│   ├── Models/                   # 纯数据模型（Song / DownloadTask / AppConfig ...）
│   ├── Services/
│   │   ├── Interfaces/           # 服务契约
│   │   ├── SongSearchService / PlaylistService        # 曲库检索
│   │   ├── SongUrlService                             # 取址协议
│   │   ├── DownloadService / DownloadQueue            # 下载与并发
│   │   ├── MediaService / FfmpegArguments             # ffmpeg 封装与参数拼装
│   │   ├── TsDecryptService                           # Thunder 解密
│   │   ├── PreviewService                             # 预览准备（不转码）
│   │   ├── DatabaseUpdateService                      # 曲库更新
│   │   ├── SearchQuery                                # 「歌名 歌手」解析
│   │   ├── SongFileNaming                             # 产物命名规则（写入与反查共用）
│   │   ├── VocalChannelRule                           # 单音轨卡拉OK左右声道规则（下载/预览共用）
│   │   ├── SingerNameLookup                           # 批量补歌手名
│   │   ├── OverlayGeometry / OverlayFilterBuilder     # 叠加层定位与滤镜图（预览/烧录共用）
│   │   ├── FfmpegFilterText / FfmpegProgressParser    # 滤镜转义 / -progress 解析
│   │   ├── OverlayAssetResolver / FontFileResolver    # 读图片尺寸 / 字体名→字体文件
│   │   └── OverlayTextPresets                         # KTV 文字样式预设
│   └── Infrastructure/
│       ├── Http/                 # CleanHttpClient（防风控裸 Socket）/ RetryPolicy / HttpResult
│       ├── Media/VlcMediaPlayer  # libVLC 封装
│       ├── Security/RsaPkcs1Encryptor
│       ├── Config/               # ConfigStore / MiniJson / ProtocolCredentials /
│       │                         # DatabasePathResolver / FavoriteStore / OverlayStore
│       ├── Logging/              # FileLogger（缓冲写 + 级别过滤）/ LogLevel
│       └── Extensions/CommonExtensions
├── tests/KtvDownloader.Tests/    # MSTest 单元测试
└── tools/                        # ffmpeg.exe 放这里（不在仓库中）
```

## 架构

```
Forms (UI)
    ↓ 只调用接口
Services (业务逻辑，不引用 WinForms)
    ↓
Infrastructure (HTTP / 媒体 / 配置 / 日志)
    ↑
Models (纯 POCO，无依赖)
```

**约束**：
- UI 层不写业务逻辑、不直接发 HTTP
- Service 层不引用 `System.Windows.Forms`
- 所有服务通过接口注入，便于测试
- 容易写错又难以发现的逻辑（ffmpeg 参数、文件命名、分页计算、JSON 解析）
  一律抽成**纯函数**放在独立类里，好让单元测试直接断言

## 关键技术要点

### 1. 防风控的 HTTP 通道

歌曲源只放行「干净」请求：**只带 `Host` 与 `Accept: */*`**。
一旦带上 `User-Agent` 等默认头，立即返回 403。

因此 `CleanHttpClient` 不用 `HttpClient`（无法完全去掉默认头），
而是手写 HTTP/1.1 报文，通过裸 Socket 发送：IPv4 / IPv6 有序尝试、
连接与读取共用同一个截止时间、取消时直接关 socket、读取出错时保留已收到的部分响应体。

### 2. 取址协议

```
GET {host}/i.php?appid=...&mac={MAC}_{SN}&sn={SN}&time={unix}&ver=4&vn=...&sign=MD5(整串+APP_KEY)
  → {"code":200,"token":"..."}

GET {host}/music/do.php?appid=...&device=...&ish265=0&ls=1&musicno={歌号}&resolution=720&sn=...&time=...&token=...&sign=MD5(整串+SDK_KEY)
  → {"code":200,"data":"http://.../7300654.ts?token=..."}
```

- 参数顺序固定，sign 是整串拼盐做 MD5
- 只试 `ls=1`
- 403 是限流而非 token 过期，等 3 秒同 token 重试

所有端点、appid / appkey / sdkkey、设备号、UA、RSA 公钥都集中在
`Infrastructure/Config/ProtocolCredentials.cs`，
可以用 `%AppData%\KtvDownloader\credentials.json` 覆盖（换源站或换设备号时不用改代码重新编译）。

### 3. TS 解密

源站下发的 `.ts` 是 Thunder 加密：
- 前 512 字节私有头，头[500..512] 是签名（`THUNDERCRYP3` / `HHCMUSECRYP1` / `HHCMUSECRYP2`）
- 密钥按 `头[53] & 0x0F` 从内置表取
- 分段 **AES-256**-ECB 解密（密钥表每项 64 个十六进制字符 = 32 字节）

**判定"是否加密"只看签名**，不看首字节是不是 `0x47`：
曲库里还有 1,157 首 `.mp3` 与 8,488 首 `.ls/.kls`，它们首字节当然不是 TS 同步字节，
一旦误判就会被送去解密、然后整首歌报"下载失败"。非 TS 内容一律原样透传。

### 4. 转 MP4 必须重编码音频

TS 内音轨是 **mp2**。直接 `-c copy` 塞进 MP4 会被标成 mp3，
多数播放器解不出 → **画面正常但没声音**。

正确做法：
```
-c:v copy -c:a aac -b:a 192k
```

### 5. 伴奏在原视频的第二条音轨里

曲库 `accomp` 字段只是**声道标记**，不是伴奏文件。
双音轨的歌曲，真正的伴奏在同一个 ts 的第 2 条音轨：
- 第 1 条音轨 = 原唱
- 第 2 条音轨 = 伴奏

用 `-map 0:a:0` / `-map 0:a:1` 分别提取。

**老式单音轨歌曲**靠左右声道区分，由 `accomp` 决定谁是原唱：

| accomp | 左声道 | 右声道 |
|---|---|---|
| `1`（及未标注的 `0`） | 原唱 | 伴奏 |
| `2` | 伴奏 | 原唱 |

这张表在代码里只有**一份**：`Services/VocalChannelRule.cs`。
下载（ffmpeg pan 滤镜）和预览（libVLC 声道切换）都问它，
避免两边各写一遍 `accomp == 2` 之后漂移，出现"预览里是原唱、下载下来变伴奏"。

拆分用 pan 滤镜，**两个输出声道都必须显式赋值**，否则没赋值的那一路 gain 为 0（静音）：

```
pan=stereo|c0=c1|c1=c1     # 取右声道，复制成双声道
```

### 6. 预览用 libVLC，不转码

libVLC 原生支持 MPEG-TS + mp2，解密后的 `.ts` 可以直接播，
首屏接近 0 秒；音轨切换是实时 API 调用，播放中就能切原唱/伴奏。

音轨 id **只能问 libVLC 要**（`libvlc_audio_get_track_description`），
不能用「当前 id + 序号」递推 —— TS 的音轨 PID 不保证连续（实测有 257/259 这种跳号），
猜出来的 id 会让切换静默失败。

**探测也不能只探一次。** libVLC 打开 TS 是异步的，要先解出 PAT/PMT 才知道有几条音轨；
固定延时（曾经用的是 800ms 一次性定时器）在慢机器上会拿到空链表，
把双音轨的歌误判成单音轨，下拉框直接灰掉。现在改成 300ms 轮询：
拿到 ≥2 条立刻收工；只有 1 条时要求"已开始播放"且连续 3 轮都如此才认定单音轨，
并改走 `libvlc_audio_set_channel` 切左右声道（这类歌切音轨没有意义，只有一条）。

预览片段会缓存复用，缓存目录有容量上限（512MB / 200 个文件），超出自动淘汰最久未用的。

### 7. 叠加层：预览是"软"的，下载才真烧

水印图片和片头文字卡片是**同一个东西**，只是时间窗口不同：
台标不设窗口 = 全程显示，片头卡片设成 `[0, 5]`，片尾致谢设成 `[55, 60]`。
正片画面照常播、正片声音照常放，卡片只是叠在上面。

**预览时不碰视频文件**：GDI+ 把配置画到一张位图上，用 `UpdateLayeredWindow`
合成到视频上方（`Controls/OverlayLayeredWindow.cs`）。
这里不能用普通透明控件 —— WinForms 的 `BackColor = Transparent` 是靠
"让父控件先画背景"模拟的，而 libVLC 是直接往 `panelVideo` 的原生 HWND 上画的，
不经过它的 `Paint`，于是透明区域只会露出黑色（docs 坑 14）。

**下载时才真烧**：在**已经产出的 MP4** 上再过一遍 ffmpeg。
第一步 TS → MP4 用 `-c:v copy`（视频不重编码，只换容器），音频 mp2 → AAC；
第二步才把视频解码、叠加、重编码一次，而因为中间产物已经是 AAC，音频直接 `-c:a copy`。
两步加起来视频**总共只编码一遍**，和"一步到位"耗时相同，还省掉一代音频损失。

> ⚠️ **烧录 = 整首重编码。** 哪怕水印只出现 5 秒，H.264 的帧间预测也决定了
> 每一帧都要解码再编码。一首 4 分钟 720p 的歌，`libx264 -preset veryfast`
> 大概 1–3 分钟（对比原来的几秒）。批量烧录时建议把并发数降到 1。
>
> `SplitBoth` 是个例外：原唱版和伴奏版的画面完全相同（两个抽取路径都是 `-c:v copy`），
> 所以**只烧第一个**，剩下的把烧好的视频流直接拷过来配上各自的音轨，秒级完成 ——
> 否则等于把同一段视频重编码两次，画质还多损失一代。

**编码器默认「自动」**：烧录时按 `h264_nvenc` → `h264_qsv` → `h264_amf` → `h264_mf`
的顺序**逐个真跑一次 64×64 微编码实测**，第一个通过的就用（比 libx264 快 5–10 倍），
全都不通过才回落 libx264。

> ⚠️ `ffmpeg -encoders` 里列出来 **≠** 真能用 —— 那只说明这个构建编译进了该编码器。
> 没有对应显卡、驱动太旧、nvenc 并发会话数被占满时，ffmpeg 会在初始化编码器那一刻
> 失败退出，用户看到的只有「下载失败」。所以必须实测，用退出码说话。
>
> 显式选了 `libx264` 就**尊重这个选择**，不会再自动切到硬编（否则「我明明关了它又自己开回来」
> 没法解释）；显式选的硬编实测不通过则回落 libx264 + 警告，而不是让整次下载失败。
>
> ⚠️ **烧录失败不算下载失败。** 保留没烧录的成品 + 记 WARN + 在进度消息里说清楚。
> 用户等了几分钟把歌下下来，因为一张水印图被删了就整首丢掉，这个代价不对等。

坐标只有 `Services/OverlayGeometry.cs` **一个来源**，预览和烧录都调它 ——
两边各算一套迟早出现"预览在右上角、烧出来跑到中间"。
所有像素尺寸都以 **720p 为基准**存储，渲染时按实际视频高度等比缩放，
这样同一份配置在 480p / 720p / 1080p 的片源上看起来一致。

文字**刻意不走转义**：落到临时文件由 `drawtext=textfile=` 读，配 `expansion=none`，
于是 `:` `'` `%` `\` 都不需要处理（docs 坑 15）。临时文件必须 UTF-8 无 BOM、换行必须是 LF，
否则每行末尾会渲染出一个豆腐块。

> **不承诺像素级所见即所得。** 预览用 GDI+ 排版、成品用 libfreetype，
> 字距和描边会有几个像素的差（ffmpeg 的 `borderw` 只向外扩，GDI+ 的 pen 以路径为中心线，
> 所以预览用 `pen.Width = 2 × borderw` 去对齐）。
> 界面上给了「**试烧样片**」：`-t N` 真跑一遍 ffmpeg，几秒出一个小样片，
> 用户自己确认效果再批量下载。样片时长会自动覆盖到最后一个时间窗口的结束点，
> 否则用户设了"10–15 秒"的片头、样片只烧前 5 秒，什么都看不到，会以为功能坏了。

配置存在 `%AppData%\KtvDownloader\overlay.jsonl`，**一行一个元素**（扁平 JSON）。
不用 `config.json` 是因为 `MiniJson` 只解析扁平键值对，要存数组套对象就得给它加递归解析 ——
而它在 ConfigStore、曲库更新、取址协议里到处在用，改坏了影响面太大。
附带好处是用户能拿记事本直接改，`#` 开头是注释。

## 测试

`tests/KtvDownloader.Tests/` 是 MSTest 工程，只测**纯逻辑**，不连曲库、不联网、不播放：

| 覆盖 | 钉住的是什么 |
|---|---|
| `FfmpegArgumentsTests` | pan 滤镜两个声道都赋值、音频必须转 AAC、0 音轨不映射、MSVCRT 引用规则 |
| `TsDecryptServiceTests` | 只按签名判加密（mp3 不误判）、密钥是 32 字节（AES-256）、分段规则 |
| `RetryPolicyTests` | HttpClient 超时要重试、用户取消不重试、退避封顶 |
| `DownloadQueueTests` | `ActiveTaskCount` 结束后归零、并发不超上限、并发数热调整不打断在跑的任务 |
| `MiniJsonTests` | `http:\/\/` 必须反转义、坏输入返回 null 而不抛 |
| `SongFileNamingTests` | 写入与反查共用同一套命名、能认出旧命名（不含歌号）的文件 |
| `ConfigStoreTests` | 往返一致、不留 `.tmp`、坏文件回落默认且记日志、非法值被夹回 |
| `FileLoggerTests` | 级别过滤、ERROR 立刻落盘、并发写不丢行 |
| `SearchQueryTests` | 「歌手 歌名」写反能纠正、候选去重（少查几次库） |
| `PaginationMathTests` | 总数未知时不让用户跳到编出来的页码 |
| `VocalChannelRuleTests` | 单音轨切声道时左右不能弄反、伴奏轨未知时的猜测要带提示 |
| `OverlayGeometryTests` | 预览与烧录算出同一套坐标、720p 基准缩放往返不漂、拖拽反解回锚点+边距、时间窗口是闭区间 |
| `OverlayFilterBuilderTests` | `enable` 必须带单引号、图片支链在前主干在后、文本走 `textfile` 不转义、构建器不许改传入的配置 |
| `OverlayPipelineTests` | 烧录命令行的音轨映射与 `-t` 位置、`out_time_ms` 其实是微秒、探测不到流信息绝不能当"没有音轨"、配置往返 |

跑测试：

```powershell
.\scripts\run-tests.ps1
.\scripts\run-tests.ps1 -Filter "FullyQualifiedName~FfmpegArguments"
```

> 测试宿主必须跑在 **x64**：被测工程锁定 x64（libVLC 只发布 x64 原生库），
> 32 位宿主加载会直接 `BadImageFormatException`。脚本与 CI 都已带上 `/Platform:x64`。

主工程通过 `Properties/AssemblyInfo.cs` 里的 `InternalsVisibleTo("KtvDownloader.Tests")`
开放了几处测试钩子（解密签名与密钥、预览缓存键、歌手名归一化、
`ffmpeg -i` 输出解析、样片时长建议），这样测试不必去连真曲库、真源站，
**也不必装 ffmpeg** —— 探测解析和滤镜图构建都是纯函数，喂字符串进去就能验。

## 持续集成

`.github/workflows/windows-ci.yml`：在 `windows-latest` 上 restore → msbuild → vstest，
上传构建产物与 `.trx` 测试结果。工程开了 `GenerateDocumentationFile`，
`<see cref="..."/>` 写错会以 CS1574 的形式在构建里暴露出来（CS1591 已屏蔽，避免噪音淹掉真问题）。

## 日志

位置：`%AppData%\KtvDownloader\logs\yyyyMMdd.log`，按天切分，保留 14 天。

写入是**缓冲**的（常驻 `StreamWriter`），刷盘时机：WARN/ERROR 立刻刷、
攒够 64 行、跨天、显式 `Flush()`、程序退出。级别由 `config.json` 的 `LogLevel` 控制。

启动日志里带版本号（来自 csproj 的 `<Version>`，CI 可用 `-p:Version=x.y.z` 覆盖），
拿到一份日志能分清是哪次构建。

## 配置文件

| 文件 | 位置 | 说明 |
|---|---|---|
| `config.json` | `%AppData%\KtvDownloader\` | 主配置，原子写入 |
| `favorites.txt` | `%AppData%\KtvDownloader\` | 收藏的歌号，一行一个 |
| `overlay.jsonl` | `%AppData%\KtvDownloader\` | 叠加层配置（水印 / 片头卡片），一行一个元素，原子写入。`#` 开头是注释，可用记事本直接改 |
| `credentials.json` | `%AppData%\KtvDownloader\` | 可选，覆盖端点/appid/设备号等（不存在则用内置默认值） |
| `preview-cache/` | `%AppData%\KtvDownloader\` | 预览片段缓存，自动淘汰 |

## 开发说明

### 调整界面布局

所有窗体都遵循 `*.cs` + `*.Designer.cs` 分离：
- 在 VS 中双击 `.cs` 文件打开设计器
- 拖拽控件后，布局代码自动写入 `Designer.cs`
- 业务逻辑写在 `.cs` 中，不会被设计器覆盖

### 添加新服务

1. 在 `Services/Interfaces/` 定义接口
2. 在 `Services/` 实现
3. 在 `MainForm.InitializeServices()` 里注入

> 注意 `MainForm` 里有三个不同的初始化入口，别用错：
> - `InitializeServices()` —— **只在启动时**完整构建；
> - `RebuildDatabaseServices()` —— 只换曲库相关的服务（曲库更新后、改了曲库路径时）；
> - `ApplyConfigurationChanges()` —— 保存设置后原地热调整（并发数、ffmpeg 路径、日志级别）。
>
> 保存设置时如果去调 `InitializeServices()`，会 Dispose 掉下载队列，
> 而队列的 Dispose 等于 `CancelAll()` —— 用户只是改了个下载目录，正在下的歌就全没了。

### 版本号

只在 `src/KtvDownloader/KtvDownloader.csproj` 的 `<Version>` 里维护一处。
`Properties/AssemblyInfo.cs` 里**不要**再写 `AssemblyVersion` 等特性
（工程开了 `GenerateAssemblyInfo`，两边都写会撞出 CS0579 编译不过）。

### 代码规范

工程根目录的 `.editorconfig` 会在 VS 中自动应用：
- 4 空格缩进
- 私有字段 `_camelCase`
- `using` 按系统优先排序
- 始终使用大括号

## 合法性与免责

本项目是**个人学习用途**的技术研究，用于理解 KTV 曲库同步协议与加密 TS 的封装格式。

- 曲库、歌曲音视频的版权归原作者与麦动（Muse）所有，本项目不分发任何受版权保护的内容；
- 程序内置的 appid / 密钥 / 设备号来自上游开源项目，仅用于与官方服务完成协议握手；
  如需替换，改 `%AppData%\KtvDownloader\credentials.json` 即可，不必改代码；
- 下载得到的文件请仅用于个人已获授权的场合，**不要**再分发、不要用于任何商业用途；
- 频繁请求可能触发源站风控甚至账号/设备封禁，请自行控制并发与频率；
- 使用本项目产生的一切后果由使用者自行承担。

如果你不是该曲库的合法使用方，请不要使用本项目。
