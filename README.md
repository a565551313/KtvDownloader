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

- **搜索**：按歌名 / 按歌手，支持「歌名 歌手」组合写法（顺序写反也能纠正）
- **浏览**：榜单、新歌、分类（语种 / 标签）、收藏
- **预览**：双击结果即可试听，播放中可实时切换原唱 / 伴奏（libVLC，不转码，首屏接近 0 秒）
- **下载**：多选批量入队，每首歌独立进度；队列右键可取消、打开所在目录、收藏
- **收藏**：搜索结果或队列右键 →「添加收藏 / 取消收藏」，按歌号存本地，曲库更新后依然有效
- **曲库更新**：`工具 → 检查更新`，带进度与取消

## 目录结构

```
KtvDownloader/
├── KtvDownloader.sln
├── .editorconfig                 # 代码规范（VS 自动应用）
├── .gitignore
├── README.md
├── docs/技术交接文档.md           # 交接文档（接口清单 / 13 个坑 / API 说明）
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
│   │   ├── SettingsForm.cs / PlaylistForm.cs / CategoryForm.cs / DatabaseUpdateForm.cs
│   ├── Controls/                 # BrowseToolBar / PaginationBar / PaginationMath
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
│   │   └── SingerNameLookup                           # 批量补歌手名
│   └── Infrastructure/
│       ├── Http/                 # CleanHttpClient（防风控裸 Socket）/ RetryPolicy / HttpResult
│       ├── Media/VlcMediaPlayer  # libVLC 封装
│       ├── Security/RsaPkcs1Encryptor
│       ├── Config/               # ConfigStore / MiniJson / ProtocolCredentials /
│       │                         # DatabasePathResolver / FavoriteStore
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
| `1`（及其它） | 原唱 | 伴奏 |
| `2` | 伴奏 | 原唱 |

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

预览片段会缓存复用，缓存目录有容量上限（512MB / 200 个文件），超出自动淘汰最久未用的。

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

跑测试：

```powershell
.\scripts\run-tests.ps1
.\scripts\run-tests.ps1 -Filter "FullyQualifiedName~FfmpegArguments"
```

> 测试宿主必须跑在 **x64**：被测工程锁定 x64（libVLC 只发布 x64 原生库），
> 32 位宿主加载会直接 `BadImageFormatException`。脚本与 CI 都已带上 `/Platform:x64`。

主工程通过 `Properties/AssemblyInfo.cs` 里的 `InternalsVisibleTo("KtvDownloader.Tests")`
开放了几处测试钩子（解密签名与密钥、预览缓存键、歌手名归一化），
这样测试不必去连真曲库、真源站。

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
