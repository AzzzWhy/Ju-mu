# Jumu 0.4.4

## 简体中文

- 新增独立「导入 .rpy」入口，选择全局入口标签后恢复对白、旁白、静态人物姓名与嵌套 menu。
- 静态 jump 转换为选项树/素材连接；其他剧情 label 进入素材袋。支持 Jumu 的 start 启动包装器以及主线的无条件顺序继续。
- 导入先检查、再确认替换整个工程，支持撤销；原 RPY 保存在左侧，重新读取采用结构解析，不进行小说断句。
- 不执行任何 Python。无法可靠转换的语句报错，原工程保留；被省略的顶层初始化与角色样式会明确提示。
- 范围仅为单个 .rpy 源文件，不支持 .rpyc、跨文件跳转、条件/动态逻辑、演出语句、多行字符串、一般 call 或主线无条件循环。文本标签/插值按字面导入，角色代号按姓名重新生成。
- Windows 包为交叉构建，尚未实机验证；Mac 包仅有本地 ad-hoc 签名，未经 Apple 公证。

## English

- Add a dedicated reverse-import action for one .rpy source file, with an entry-label selector; restore narration, dialogue, static speaker names, and nested menus.
- Map static jumps to choice-tree/bag connections, and other story labels to bag texts. Support Jumu startup wrappers and deterministic main-route continuations.
- Validate before replacing the entire project, support undo, retain original source, and re-read scripts structurally rather than splitting them as prose.
- Never execute Python. Unsupported statements fail without replacing the project; omitted top-level initialization and character styles are disclosed.
- Not a complete Ren’Py decompiler: compiled .rpyc, cross-file jumps, conditions/dynamic logic, presentation commands, multiline strings, general calls, and unconditional main-route cycles are unsupported. Text tags/interpolation are literal and speaker identifiers are regenerated.
- Windows is cross-built, not tested on a physical Windows system. Mac is ad-hoc signed, not Apple-notarized.
