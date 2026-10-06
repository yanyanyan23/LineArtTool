# LineArtTool 线稿生成器

把彩色图片转成线稿/绘画风格的 Windows 单文件小工具。
**免安装、零依赖** —— Windows 10/11 自带的 .NET Framework 4.x 即可运行。

![效果示例](示例-线稿.png)

## ✨ 特性

- **10 种风格**：XDoG 线稿 / 铅笔素描 / 钢笔线稿 / 白描 / 蓝图晒图 / 粉笔黑板 / 漫画网点 / 木刻版画 / 铅笔排线 / 淡彩上色稿
- **4 种墨色**：灰黑 / 钢笔蓝 / 复古棕 / 淡紫
- **透明底输出**：线条带 alpha 通道的 PNG，可直接垫在颜色图层下面上色
- **实时预览**：细节 / 线宽 / 锐度 / 阈值 / 硬度 / 暗部调子，全部滑杆实时可调
- **三种用法**：图形界面 / 拖图到图标 / 命令行批处理

## 🚀 快速开始

下载 `LineArtTool.exe`，双击运行：

| 方式 | 操作 |
|------|------|
| 图形界面 | 双击 exe → 打开或拖入图片 → 左侧选风格、调参数 → 「保存图片…」（按原图分辨率输出）|
| 快速批量 | 把图片拖到 exe 图标上 → 自动生成 `原名_线稿.png` |
| 命令行 | `LineArtTool.exe -i 输入.png -o 输出.png --style 0-9` |

## ⌨️ 命令行参数

```text
LineArtTool.exe -i 输入.png -o 输出.png [选项]
```

| 参数 | 说明 | 默认 |
|------|------|------|
| `--style 0-9` | 风格（按上表顺序）| 0 |
| `--ink 0-3` | 墨色：灰黑 / 钢笔蓝 / 复古棕 / 淡紫 | 0 |
| `--alpha` | 透明底 PNG（垫色上色用）| 关 |
| `--sigma` | 细节精细度 | 1.0 |
| `--k` | 线宽 | 1.6 |
| `--p` | 锐度 | 20 |
| `--eps` | 出线阈值（越大线越多）| 0.02 |
| `--phi` | 线条硬度 | 25 |
| `--shadow` | 暗部调子强度 | 0.85 |
| `--thr` | 暗部判定阈值 | 0.42 |
| `--invert` | 黑底白线 | 关 |
| `--nosp` | 关闭去杂点 | 开 |

批量示例（cmd）：

```cmd
for %f in (D:\图片\*.png) do LineArtTool.exe -i "%f"
```

## 🔨 从源码构建

双击 `build.cmd`，或手动执行：

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /codepage:65001 /win32manifest:app.manifest /win32icon:app.ico /out:LineArtTool.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll LineArtTool.cs
```

不需要安装任何 SDK —— 用的是 Windows 系统自带的 .NET 编译器（csc.exe）。

## 📁 文件说明

| 文件 | 说明 |
|------|------|
| `LineArtTool.exe` | 主程序（单文件）|
| `LineArtTool.cs` | C# 源码 |
| `build.cmd` | 一键编译脚本 |
| `app.ico` / `gen-icon.cs` | 程序图标及图标生成工具 |
| `示例-*.png` | 输出效果示例 |

## 📝 备注

- 界面参数会自动保存在 `%APPDATA%\LineArtTool\settings.txt`
- 输入支持 PNG / JPG / BMP / GIF / TIF，输出 PNG / JPG / BMP
- 超大图（超过 4000 万像素）会自动缩放后处理
- 算法核心为 XDoG（高斯差分 + 软阈值），暗部保留与网点/排线均为程序化生成，不依赖任何 AI 模型
