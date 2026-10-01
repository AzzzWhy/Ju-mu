# 句幕 Jumu 0.4.3

## 中文

- 素材袋改为存放整份文本：可新建、粘贴、导入，或整篇收纳已校正正文；每份文本一张卡片。
- 正文与素材支持多选删除、右键删除和右侧固定删除按钮；删除前确认，删除后可撤销。有外部跳转引用时整批阻止删除。
- 分支结束目标优先展示折叠素材与当前正文：可直接跳到整份文本，展开后才显示内部句子位置。
- 完整文本编辑保留角色和跳转锚点，避免改写时静默断开连接。

Windows：下载 `Jumu-0.4.3-win-x64.zip`，完整解压后运行 `Jumu.exe`，无需另装 .NET。Apple Silicon Mac：下载 `Jumu-0.4.3-mac-arm64.zip`。校验值见 `SHA256SUMS-0.4.3.txt`。

核心测试 84/84 通过；macOS 窗口已验证批量/右键删除、撤销、引用保护，以及整份和内部文本跳转选择。Windows 版跨平台构建成功，但未实机运行；本轮没有重跑 Ren’Py SDK。Windows 程序未签名，Mac 仅 ad-hoc 签名、未经 Apple 公证。包内仓库 README 来自发布前测试阶段，下载信息请以仓库最新 README 为准。请先在稿件副本上试用，保留旧工程备份；旧应用不能读取工程格式 2。

## English

- The text bag now stores complete manuscripts as single cards: create, paste, import, or stash the entire corrected main text.
- Main lines and bag texts support bulk deletion, right-click deletion, and persistent right-side delete buttons, with confirmation and undo. External incoming jumps block the entire deletion.
- Branch destinations list collapsed bag texts and the current manuscript first. Select a whole text directly, or expand it to choose an internal line.
- Full-text edits preserve speakers and jump anchors and reject edits that would silently remove linked positions.

Windows: extract `Jumu-0.4.3-win-x64.zip` and run `Jumu.exe`; .NET is bundled. Apple Silicon Mac: use `Jumu-0.4.3-mac-arm64.zip`. Verify against `SHA256SUMS-0.4.3.txt`.

84/84 core tests passed. Bulk/right-click deletion, undo, reference protection, and whole/internal destination selection were verified in the macOS UI. Windows was cross-built but not run on a physical Windows machine; Ren’Py SDK tests were not rerun for this update. Windows is unsigned; macOS is ad-hoc signed and not notarized. Bundled repository READMEs predate publication; use the repository README for current download links. Test on manuscript copies and retain project backups; older apps cannot read project format 2.
