# Jumu · Ren’Py Script Importer

[简体中文](README.md) | [English](README.en.md)

**Turn novels and scripts into dialogue files for Ren’Py.**

Jumu is an offline desktop tool for visual novel creators. Import a TXT, DOCX, or Markdown manuscript, then split it by paragraph or sentence. New manuscripts default to narration/action/other, preserving the original wording. Optional dialogue and speaker detection uses Chinese and English punctuation, quotation marks, and dialogue cues. Review lines individually or in batches, build branches in a visual choice-tree editor, and export a `.rpy` file.

## Highlights

- **Two import modes:** Novel and Script, with paragraph, sentence, or reading-length splitting.
- **Optional auto-detection:** Dialogue and speakers are not inferred by default; enable them with the “自动识别对白与角色” checkbox.
- **Human-in-the-loop editing:** Correct line type, speaker, and text; use speaker shortcuts, drag-to-select, and batch review.
- **Visual branching:** Add, move, and delete choices, including nested branches.
- **Local workflow:** Save an editable `.jumu` project and export a standalone Ren’Py `.rpy` script. Manuscript processing does not require an internet connection.

## Basic workflow

Import or paste a manuscript → choose a mode and parse → review the results and choice trees → export the `.rpy` file. Place it in your Ren’Py project’s `game` folder and `call` the exported label from your existing entry point.

Windows x64 package: [Download Jumu 0.3.3](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.3.3/Jumu-0.3.3-win-x64.zip), extract it, and run `Jumu.exe`. Verify it against [SHA256SUMS-0.3.3.txt](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.3.3/SHA256SUMS-0.3.3.txt). This build has not yet been tested on a physical Windows machine.

Jumu relies on offline rules and **cannot understand every narrative context**. Complex quotations, omitted speakers, and branch content still need human review. It does not generate sprites, backgrounds, audio, or other presentation assets.

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The desktop app uses Avalonia; the first build restores its NuGet dependencies.

```sh
dotnet run --project tests/Scribe.Tests -c Release
dotnet run --project src/Scribe.Desktop -c Release
```

The parser and exporter live in `src/Scribe.Core`, the UI in `src/Scribe.Desktop`, and automated tests in `tests/Scribe.Tests`. Sample novel and script inputs are in `samples`. Run `build-windows.ps1` on Windows for a self-contained executable, or `bash build-macos.sh` on Apple Silicon macOS for an `.app` and ZIP. The Mac script only applies a local ad-hoc signature; public distribution still requires Developer ID signing and notarization. See the [verification notes](验证记录.md) for tested scope.

## License

Jumu's source code is licensed under the [MIT License](LICENSE). Keep the copyright and license notices when using, modifying, or redistributing it. See the [third-party notices](THIRD-PARTY-NOTICES.md) for dependencies.
