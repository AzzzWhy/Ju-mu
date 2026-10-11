# Jumu 0.4.5

## 简体中文

- 修复关闭程序后无法继续上次工作的问题：正常关闭主窗口时自动保存工作快照，下次启动恢复原文、校正内容、素材袋、选项树和导出设置。
- 关闭时会提交主编辑区尚未应用的修改；保存失败会提示错误并保持窗口打开。
- 「保存工程」仍可另存 `.jumu` 文件用于备份或分享；通过启动参数打开指定文件时优先打开该文件。
- 素材袋和选项树窗口的编辑仍需点击「应用」，才会写入当前工程。此功能覆盖正常关闭，不提供异常退出或断电恢复。
- Windows x64：解压后运行 `Jumu.exe`，无需安装 .NET。Mac ARM64：解压后打开 `Jumu.app`；仅 ad-hoc 签名，未经 Apple 公证。
- 98 项核心测试通过，桌面构建通过。自动保存与恢复的窗口交互尚未实机验证。

## English

- Save the current project when the main window closes normally, and restore it on the next launch, including source text, corrections, the text bag, menus, and export settings.
- Commit pending main-editor changes before closing. A save failure reports an error and keeps the window open.
- Save Project still writes a separate `.jumu` file for backups or sharing. An explicit startup file takes priority over the previous session.
- Apply changes in the bag/menu dialogs to include them in the project. Recovery covers normal closing, not crashes or power loss.
- Windows x64: extract and run `Jumu.exe`; no .NET installation required. Mac ARM64: open `Jumu.app`; ad-hoc signed, not Apple-notarized.
- All 98 core tests and the desktop build passed. Window interactions for automatic save/restore have not been tested manually.
