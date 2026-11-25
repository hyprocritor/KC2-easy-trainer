# 项目介绍

这个项目是游戏 [《完全痴○2 清楚なJKを汚し尽くす》](https://www.dlsite.com/maniax/work/=/product_id/RJ01678591.html) 的修改器。可以方便加快游戏进度。
This Project is the trainer for game [《完全痴○2 清楚なJKを汚し尽くす》](https://www.dlsite.com/maniax/work/=/product_id/RJ01678591.html).

This Project is using  MIT license.

# KanzenChikan2 进度面板

**本插件由 OpenAI Codex 开发，并由 OpenAI Codex 整理为此源码项目。**

当前源码对应插件 **v1.3.0**。在 Windows x64 的 KanzenChikan2 Unity IL2CPP 版本中，按 **F8** 开关中文面板，按住标题栏可以拖动。

- 经验、开发度、快感和声音警戒倍率可独立调整，每次启动都从 1.0 倍开始。
- 当前值可手动编辑。总警戒值、基础发现风险和五个视觉感知槽可锁定。
- 中文字形随插件嵌入。字形来自 Noto Sans SC，授权见 [字体许可证](assets/OFL-NotoSansSC.txt)。

## 下载与安装

[下载 KC2 Easy Trainer v1.3.0](https://github.com/hyprocritor/KC2-easy-trainer/releases/download/v1.3.0/KC2-easy-trainer-v1.3.0.zip)

1. 下载 Windows x64 的 Unity IL2CPP 版 [BepInEx 6.0.0-be.793](https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip)。
2. 将 BepInEx ZIP 的内容解压到游戏根目录，使 `winhttp.dll` 和 `KanzenChikan2.exe` 位于同一级。
3. 启动游戏一次。首次启动需要生成 IL2CPP 互操作文件，可能会慢一些。进入主菜单后退出游戏。
4. 将修改器 ZIP 解压到游戏根目录，保留其中的 `BepInEx/plugins/` 路径。再次启动游戏，按 **F8** 打开面板。

[BepInEx 官方安装指南](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)

## 项目文件

- [src](src) 包含插件入口、游戏变量与倍率、面板及字形渲染源码。
- [assets](assets) 包含插件嵌入的字形和字体许可证。
- [tests](tests) 包含离线数值和字形检查。
- [tools](tools) 包含 Windows 字形生成工具和用于重建现有字形的字体子集。

## 构建和检查

安装 .NET 6 SDK。游戏目录需要已经安装 BepInEx 6 Windows x64 IL2CPP，并至少启动过一次以生成 `BepInEx/interop` 中的程序集。

从本文件夹打开 PowerShell，按实际路径设置 `$gameDir`。

```powershell
$gameDir = 'F:\Games\KanzenChikan2'
dotnet build .\KanzenChikan2.ProgressPanel.csproj -c Release "-p:GameDir=$gameDir"
dotnet run --project .\tests\ProgressPanel.Checks.csproj -c Release "-p:GameDir=$gameDir"
```

插件文件生成在 `bin/Release/net6.0/KanzenChikan2.ProgressPanel.dll`。退出游戏后可把它复制到游戏的 `BepInEx/plugins`。

## 重建字形

构建插件时直接读取 `assets/ChineseAtlas.gz`。新增中文文本时，使用覆盖新字符的字体重新生成字形。仓库自带的字体子集仅覆盖当前界面的字符。

```powershell
$uiSource = Join-Path $env:TEMP 'KanzenChikan2-PanelGlyphs.txt'
$preview = Join-Path $env:TEMP 'KanzenChikan2-ChineseAtlas-preview.png'
(Get-Content -Raw -LiteralPath '.\src\ProgressPanel.cs','.\src\ProgressPanel.Vision.cs') -join "`n" |
    Set-Content -LiteralPath $uiSource -Encoding utf8
$font = (Resolve-Path -Path '.\tools\fonts\NotoSansSC-subset.*').Path
dotnet run --project .\tools\BakeChineseAtlas.csproj -c Release -- $font $uiSource .\assets\ChineseAtlas.gz $preview
```
