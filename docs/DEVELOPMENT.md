# 开发、构建与验证

[返回首页](../README.md) · [使用说明](usage.md) · [常见问题](FAQ.md)

## 环境

Windows x64 和 .NET 10 SDK。应用项目为 WPF `net10.0-windows`，没有第三方 NuGet 软件包依赖；仍需 SDK 自身的 Windows 桌面构建组件。

```powershell
dotnet build src/MiniMetro/MiniMetro.csproj -c Release
```

本次 .NET SDK `10.0.401` 下构建通过，零警告、零错误。普通开发输出不是免运行库的单文件发布，分发命令见首页。

## 目录与行为

| 文件 | 职责 |
| --- | --- |
| `src/MiniMetro/MainWindow.xaml` | 大窗口、小条和控制布局 |
| `MainWindow.xaml.cs` | 参数、窗口状态、拖动与快捷键 |
| `BeatSynth.cs` | 节拍音色和样本生成 |
| `WaveAudio.cs` | Windows waveOut 输出 |
| `App.xaml.cs` | 单实例与窗口入口 |
| `src/MiniMetro.Tests/Program.cs` | 自动检查程序 |

路径未写全的文件位于 `src/MiniMetro/`。音频按样本推进节拍，UI 指示不作为音频精度的计量结果。主窗口和小条共享状态，修改拖动时要保留轻点与移动的区分。

## 测试与发布

```powershell
dotnet run --project src/MiniMetro.Tests/MiniMetro.Tests.csproj -c Release
```

这不是纯无声单元测试：会短暂显示窗口、播放节拍并访问本机设置。需要合适桌面与音频环境时再运行，本轮未执行该检查，不用构建通过代替音频和置顶验证。

```powershell
dotnet publish src/MiniMetro/MiniMetro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
```

分发前检查输出 EXE 的版本及新机器启动。不要把 bin、obj、个人 settings.json 或生成的测试日志加入提交。

## 状态与兼容

设置位于 `%LOCALAPPDATA%\MiniMetro\settings.json`。先备份再做恢复操作，不随文档维护清空设置。全局快捷键必须包含 Ctrl + Alt，可能与其他应用冲突；独占全屏覆盖能力要在实际播放器上验证。
