# 小条节拍器 · Mini Metronome

一个 Windows 桌面节拍器。追剧时缩成右上角的置顶小横条，随手开关、调速；需要细调时再展开。

![迷你横条](docs/images/mini.png)

## 功能

- **180 × 30 的迷你横条**：始终置顶，首次显示在右上角；按住任意位置都可拖动，包括按钮和速度数字。拖动时不会触发按钮，轻点仍执行原有操作。
- **随手控制**：开始 / 暂停、加减 BPM、滚轮调速；按住 Shift 每次调整 5 BPM。
- **大窗口**：顶部标题与旁边空白处可拖动；顶部「置顶」按钮开关置顶，并显示当前状态。
- **节奏设置**：20–300 BPM、每小节 1–12 拍、每拍 1/2/3/4 细分、首拍重音。
- **声音**：木质、清脆、电子三种音色；独立音量和静音。
- **敲击测速**：以目标节奏连续点击至少 3 次，自动计算 BPM。
- **全局快捷键**：Ctrl + Alt + 空格开关，↑ / ↓ 调速，M 切换窗口模式。
- 记住参数与小条位置，下次打开时不自动发声。完全本地运行。

窗口尺寸使用 Windows 的逻辑像素，会随系统显示缩放比例变化。

![大窗口](docs/images/full.png)

## 使用

从仓库的 **Releases** 下载 `MiniMetronome-v1.2-win-x64.exe`，退出旧版后双击运行。Windows 64 位免安装版，无需另装 .NET 运行环境。

详细操作见 [使用说明](docs/usage.md)。

## 构建

需要 Windows 和 .NET 10 SDK。在仓库根目录运行：

```powershell
dotnet build src/MiniMetro/MiniMetro.csproj -c Release
```

打包为包含运行环境的单文件 EXE：

```powershell
dotnet publish src/MiniMetro/MiniMetro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
```

## 检查

```powershell
dotnet run --project src/MiniMetro.Tests/MiniMetro.Tests.csproj -c Release
```

自动检查覆盖节拍数量、细分、静音和暂停、Windows 音频连续输出、标题拖动区域、小条任意位置拖动、按钮单击与拖动的区分、置顶按钮、窗口切换、全屏层级以及布局。检查会短暂显示窗口并播放测试节拍，完成后恢复原有设置文件。

## 实现

- C# / WPF，无第三方软件包依赖。
- Windows `waveOut` 共享音频输出，48 kHz 单声道 PCM。
- 依据音频样本推进节拍，界面计时器只负责指示灯。
- 队列包含 8 个 10 毫秒音频块；开关和调速的听觉响应可能延后约一个队列长度。界面闪烁是近似指示。
- 小条可覆盖普通无边框全屏窗口；独占全屏或其他播放器的置顶层可能遮挡它。

## 目录

```text
src/MiniMetro/          桌面程序
src/MiniMetro.Tests/    自动检查
docs/                  使用说明与界面预览
```
