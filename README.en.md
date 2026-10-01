# Jumu · Ren’Py Script Importer

[简体中文](README.md) | [English](README.en.md)

**Turn novels and scripts into dialogue files for Ren’Py.**

Jumu is an offline desktop tool for visual novel creators. Import a TXT, DOCX, or Markdown manuscript, then split it by paragraph or sentence. New manuscripts default to narration/action/other, preserving the original wording. Optional dialogue and speaker detection uses Chinese and English punctuation, quotation marks, and dialogue cues. Review lines individually or in batches, build branches in a visual choice-tree editor, and export a `.rpy` file.

## Highlights

- **Two import modes:** Novel and Script, with paragraph, sentence, or reading-length splitting.
- **Optional auto-detection:** Dialogue and speakers are not inferred by default; enable them with the “自动识别对白与角色” checkbox.
- **Human-in-the-loop editing:** Correct line type, speaker, and text; use speaker shortcuts, drag-to-select, batch review, and move individual lines. Main lines and bag texts support bulk deletion, right-click deletion, and a persistent right-side delete button, with confirmation and undo. External incoming references block the entire deletion.
- **Automatic character identifiers:** Export auto-defines a `Character` for each speaker, using valid names directly and safely converting complex names. Lines without a speaker use `s`. Explicit mappings to existing character variables still take precedence. Check for identifier collisions with variables already in your Ren’Py project.
- **Visual branching:** Add, move, and delete choices, including nested branches.
- **Whole-text bag and story jumps:** Create, paste, or import complete texts, or stash the entire corrected manuscript. Each text is one card, not a bag of individual sentences. Edit the full text by default; per-line corrections remain available for speakers and types. Drop a card onto a choice to link a jump, or target a main-story line.
- **Local workflow:** Save an editable `.jumu` project and export a standalone Ren’Py `.rpy` script. Manuscript processing does not require an internet connection.
- **Lightweight motion (0.4.1):** Smooth button hover/press, selection colors, card highlights, and drag opacity, with brief window/editor fades. Toggle “轻量动效” in the footer to disable motion for the current session without affecting editing or export.

## Basic workflow

Import or paste a manuscript → choose a mode and parse → review the results and choice trees → export the `.rpy` file into your Ren’Py project’s `game` folder. If the project already has `label start`, add `call imported_story` (or your configured story label) there. For a new project without `start`, enable startup-label generation in export settings. Jumu also checks neighboring `.rpy` scripts when exporting into `game` and prompts if a startup entry is missing.

Windows x64 package: [Download Jumu 0.4.3](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.4.3/Jumu-0.4.3-win-x64.zip), extract it, and run `Jumu.exe`. Apple Silicon Mac package: [Download Jumu 0.4.3](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.4.3/Jumu-0.4.3-mac-arm64.zip). Verify them against [SHA256SUMS-0.4.3.txt](https://github.com/AzzzWhy/Ju-mu/releases/download/v0.4.3/SHA256SUMS-0.4.3.txt). The Windows build has not yet been tested on a physical Windows machine or with Ren’Py 8.5.3. The Mac build is only locally ad-hoc signed and is not Apple-notarized.

Jumu relies on offline rules and **cannot understand every narrative context**. Complex quotations, omitted speakers, and branch content still need human review. It does not generate sprites, backgrounds, audio, or other presentation assets.

### Fragment bag and jumps

1. Open “素材袋” (Text Bag) and choose New / Import Text. Type, paste, or import TXT, DOCX, or Markdown. The entire text becomes one card regardless of its internal dialogue count. New text defaults to narration.
2. Stash the entire corrected manuscript with its handle or button, preserving speakers, review states, and menus; the main route may be empty. Drop a card onto the upper/lower half of the manuscript card to prepend/append the whole text. Restore buttons are also available. Importing another manuscript prompts to replace the main text while retaining the bag.
3. In the choice-tree window, drop a bag card onto a yellow choice node. The branch destination dropdown shows collapsed bag texts followed by the current manuscript. Select a whole text directly, or click ▸ to expand and choose an internal position. Fragment continuations use the same picker.
4. Clicking a card opens a full-text editor. Blank lines separate internal dialogue blocks; editing existing blocks preserves speakers and links. Edits that would remove referenced positions are blocked. Expand per-line corrections for speaker/type editing. A branch plays its own content first, then jumps and continues at the destination. Configure a text's continuation or end the story. Unconnected drafts are not exported or played.

Apply the bag/tree changes, then save the `.jumu` project; Cancel discards the window's edits. Moving preserves sentence identities and attached menus. Unlink incoming jumps before deleting referenced text. Version 0.4.0 saves project format 2 and can read older projects; older apps cannot read the new format, so keep backups.

The current version is **0.4.3**, including the whole-text bag, bulk/right-click deletion, persistent delete buttons, and collapsed jump destinations. Older versions remain available in [Releases](https://github.com/AzzzWhy/Ju-mu/releases).

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The desktop app uses Avalonia; the first build restores its NuGet dependencies.

```sh
dotnet run --project tests/Scribe.Tests -c Release
dotnet run --project src/Scribe.Desktop -c Release
```

The parser and exporter live in `src/Scribe.Core`, the UI in `src/Scribe.Desktop`, and automated tests in `tests/Scribe.Tests`. Sample novel and script inputs are in `samples`. Run `build-windows.ps1` on Windows for a self-contained executable, or `bash build-macos.sh` on Apple Silicon macOS for an `.app` and ZIP. The Mac script only applies a local ad-hoc signature; public distribution still requires Developer ID signing and notarization. See the [verification notes](验证记录.md) for tested scope.

## License

Jumu's source code is licensed under the [MIT License](LICENSE). Keep the copyright and license notices when using, modifying, or redistributing it. See the [third-party notices](THIRD-PARTY-NOTICES.md) for dependencies.
