# KtvDownloader

> 📘 **接手本项目请先读 [docs/技术交接文档.md](docs/技术交接文档.md)**
> —— 包含项目来源、6 组外部接口完整参数、13 个已知坑、服务层 API 清单。

麦动 KTV 曲库下载器 —— 基于 .NET Framework 4.8 + WinForms 的桌面工具。
支持搜索歌曲、批量下载、音轨分离（原唱/伴奏）。

## 环境要求

- Windows 10 / 11
- .NET Framework 4.8（系统自带）
- Visual Studio 2022 或更高（用于打开工程、调整界面布局）
- 曲库文件 `muse.db`（约 983MB，需另行获取）
- `ffmpeg.exe`（转码与音轨分离需要，放在 `tools/` 目录）

## 快速开始

### 1. 打开工程

双击 `KtvDownloader.sln`，用 Visual Studio 打开。

### 2. 编译

```
生成 → 生成解决方案（Ctrl+Shift+B）
```

或在命令行：

```powershell
msbuild KtvDownloader.sln -p:Configuration=Release
```

### 3. 运行

首次运行会提示配置曲库路径。也可以通过 `文件 → 设置` 配置。

配置项：
| 项 | 说明 |
|---|---|
| 曲库 | `muse.db` 的完整路径 |
| 下载目录 | 歌曲输出位置 |
| ffmpeg | `ffmpeg.exe` 路径，留空则自动在 `tools/` 查找 |
| 同时下载数 | 1-8，默认 2 |
| 默认清晰度 | 480 / 720 / 1080（源站多数只有 480p） |
| 音轨处理 | 保留双轨 / 仅原唱 / 仅伴奏 / 各一份 |

## 目录结构

```
KtvDownloader/
├── KtvDownloader.sln
├── .editorconfig                 # 代码规范（VS 自动应用）
├── .gitignore
├── README.md
├── src/KtvDownloader/
│   ├── KtvDownloader.csproj      # SDK-style, net48
│   ├── Program.cs                # 入口 + 全局异常兜底
│   ├── Properties/AssemblyInfo.cs
│   ├── Forms/                    # 窗体（含 .Designer.cs，可用设计器拖拽）
│   │   ├── MainForm.cs / .Designer.cs
│   │   └── SettingsForm.cs / .Designer.cs
│   ├── Controls/                 # 自定义控件
│   ├── Models/                   # 纯数据模型
│   │   ├── Song.cs / Singer.cs / Playlist.cs
│   │   ├── DownloadTask.cs / DownloadProgress.cs
│   │   └── DownloadOptions.cs / AppConfig.cs
│   ├── Services/
│   │   ├── Interfaces/           # 服务契约
│   │   └── *.cs                  # 服务实现
│   └── Infrastructure/
│       ├── Http/CleanHttpClient.cs   # 防风控的裸 Socket HTTP
│       ├── Logging/FileLogger.cs
│       ├── Config/ConfigStore.cs
│       └── Extensions/
├── tests/KtvDownloader.Tests/
├── tools/                        # ffmpeg.exe 放这里
└── docs/
```

## 架构

```
Forms (UI)
    ↓ 只调用接口
Services (业务逻辑，不引用 WinForms)
    ↓
Infrastructure (HTTP / 日志 / 配置)
    ↑
Models (纯 POCO，无依赖)
```

**约束**：
- UI 层不写业务逻辑、不直接发 HTTP
- Service 层不引用 `System.Windows.Forms`
- 所有服务通过接口注入，便于测试

## 关键技术要点

### 1. 防风控的 HTTP 通道

歌曲源只放行「干净」请求：**只带 `Host` 与 `Accept: */*`**。
一旦带上 `User-Agent` 等默认头，立即返回 403。

因此 `CleanHttpClient` 不用 `HttpClient`（无法完全去掉默认头），
而是手写 HTTP/1.1 报文，通过裸 Socket 发送。

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

### 3. TS 解密

源站下发的 `.ts` 是 Thunder 加密：
- 前 512 字节私有头，头[500..512] 是签名
- 密钥按 `头[53] & 0x0F` 从内置表取
- 分段 AES-128-ECB 解密

### 4. 转 MP4 必须重编码音频

TS 内音轨是 **mp2**。直接 `-c copy` 塞进 MP4 会被标成 mp3，
多数播放器解不出 → **画面正常但没声音**。

正确做法：
```
-c:v copy -c:a aac -b:a 192k
```

### 5. 伴奏在原视频的第二条音轨里

曲库 `accomp` 字段只是声道标记，不是伴奏文件。
真正的伴奏在同一个 ts 的第 2 条音轨：
- 第 1 条音轨 = 原唱
- 第 2 条音轨 = 伴奏

用 `-map 0:a:0` / `-map 0:a:1` 分别提取。

## 日志

位置：`%AppData%\KtvDownloader\logs\yyyyMMdd.log`
按天切分，保留 14 天。

## 配置文件

位置：`%AppData%\KtvDownloader\config.json`

## 开发说明

### 调整界面布局

所有窗体都遵循 `*.cs` + `*.Designer.cs` 分离：
- 在 VS 中双击 `.cs` 文件打开设计器
- 拖拽控件后，布局代码自动写入 `Designer.cs`
- 业务逻辑写在 `.cs` 中，不会被设计器覆盖

### 添加新服务

1. 在 `Services/Interfaces/` 定义接口
2. 在 `Services/` 实现
3. 在 `MainForm` 构造时注入

### 代码规范

工程根目录的 `.editorconfig` 会在 VS 中自动应用：
- 4 空格缩进
- 私有字段 `_camelCase`
- `using` 按系统优先排序
- 始终使用大括号
