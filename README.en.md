# Jumu · Ren’Py Script Importer

[简体中文](README.md) | [English](README.en.md)

**Turn novels and scripts into dialogue files for Ren’Py.**

Jumu is an offline desktop tool for visual novel creators. Import a TXT, DOCX, or Markdown manuscript, then split it by paragraph or sentence. Jumu uses Chinese and English punctuation, quotation marks, and dialogue cues to identify speech, narration, and stage directions. Review lines individually or in batches, build branches in a visual choice-tree editor, and export a `.rpy` file.

## Highlights

- **Two import modes:** Novel and Script, with paragraph, sentence, or reading-length splitting.
- **Human-in-the-loop editing:** Correct line type, speaker, and text; use speaker shortcuts, drag-to-select, and batch review.
- **Visual branching:** Add, move, and delete choices, including nested branches.
- **Local workflow:** Save an editable `.jumu` project and export a standalone Ren’Py `.rpy` script. Manuscript processing does not require an internet connection.

## Basic workflow

Import or paste a manuscript → choose a mode and parse → review the results and choice trees → export the `.rpy` file. Place it in your Ren’Py project’s `game` folder and `call` the exported label from your existing entry point.

Jumu relies on offline rules and **cannot understand every narrative context**. Complex quotations, omitted speakers, and branch content still need human review. It does not generate sprites, backgrounds, audio, or other presentation assets.

## License

Jumu is licensed under the [MIT License](LICENSE). Keep the copyright and license notices when using, modifying, or redistributing it. Third-party dependencies remain subject to their own licenses.
