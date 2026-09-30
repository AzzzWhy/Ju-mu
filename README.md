# 句幕 · Jumu

[简体中文](README.md) | [English](README.en.md)

**把小说或剧本整理成可在 Ren’Py 中使用的对话脚本。**

句幕是一款离线桌面工具，面向视觉小说创作者。导入 TXT、DOCX 或 Markdown 稿件后，它会按段落或句子拆分文本。新稿件默认全部按「旁白/人物动作或其他」保留原文；需要时可开启基于中英文标点、引号和说话方式的对白与角色自动识别。你可以逐句校正、拖动多选并批量审核，还可以在图形化选项树中安排分支，最后导出 `.rpy` 文件。

## 主要功能

- **两种导入模式：**小说正文和剧本，支持按段落、句子或阅读长度拆分。
- **可选自动识别：**默认不推断对白和说话人；勾选「自动识别对白与角色」后才启用规则识别。
- **人工校正：**修改内容类型、说话人和正文；支持快捷人物姓名、拖动多选和批量确认。
- **可视化分支：**添加、移动、删除选项，并在选项树中组织嵌套分支。
- **本地工作流：**保存 `.jumu` 工程供后续编辑，导出独立的 Ren’Py `.rpy` 脚本；文本处理无需联网。

## 使用方式

导入或粘贴稿件 → 选择模式并解析 → 审核识别结果与选项树 → 导出 `.rpy`，将其放入 Ren’Py 项目的 `game` 文件夹，再从现有入口 `call` 导出的 label。

Windows x64 压缩包：[下载 Jumu 0.3.3](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.3.3/Jumu-0.3.3-win-x64.zip)（解压后运行 `Jumu.exe`）。校验值见 [SHA256SUMS-0.3.3.txt](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.3.3/SHA256SUMS-0.3.3.txt)。该版本已完成跨平台构建，但尚未在真实 Windows 电脑上实机验证。

句幕使用离线规则辅助识别，**不会自动理解所有语境**。复杂引述、省略的说话人及分支内容仍需人工检查；立绘、背景、音效等演出资源也不会自动生成。

## 从源码运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。桌面界面使用 Avalonia；首次构建需要还原 NuGet 依赖。

```sh
dotnet run --project tests/Scribe.Tests -c Release
dotnet run --project src/Scribe.Desktop -c Release
```

核心解析与导出位于 `src/Scribe.Core`，桌面界面位于 `src/Scribe.Desktop`，自动化测试位于 `tests/Scribe.Tests`。`samples` 提供小说和剧本样稿。Windows 可运行 `build-windows.ps1` 生成自包含程序；Apple Silicon Mac 可运行 `bash build-macos.sh` 生成 `.app` 和 ZIP。Mac 脚本只做本地 ad-hoc 签名，正式分发仍需 Developer ID 签名和公证。详细使用和已验证范围见[验证记录](验证记录.md)。

## 开源协议

句幕源码采用 [MIT License](LICENSE)。使用、修改或再发布时请保留版权及协议声明；第三方组件见[许可说明](THIRD-PARTY-NOTICES.md)。
