Outstanding features — priority order
1
Cursor + selection model
Caret position, selection anchor/active, MoveWordLeft/Right, SelectAll, SelectLine. Everything else builds on this.
2
Word boundary operations
GetWordAt(offset), GetWordBoundaryLeft/Right. Needed by cursor model, search (whole-word), scripting shell.
3
Multi-cursor + multi-selection
Multiple independent cursors, all edits as one undo unit. VS Code's signature feature. Hard to retrofit.
4
Scripting shell / macro runner
Parse + execute text commands (INSERT, DELETE_LINE, REPLACE ALL, GOTO). All primitives exist — just needs a runner.
5
Incremental syntax highlighting
StateTable per line — only re-tokenise changed lines. Current tokeniser re-scans from scratch every call.
6
Diff engine
Myers or patience diff between two document versions. Needed for git integration and scripting shell "what changed".
7
Bracket matching + auto-indent
FindMatchingBracket(offset), smart indent on newline. Depends on syntax layer being solid.
8
Code folding model
FoldRegion(startLine, endLine, label). Separate from text model, driven by syntax layer.
9
Encoding detection + BOM handling
Currently assumes UTF-8. Real files come as UTF-16 LE/BE, Latin-1, Windows-1252. Load path needs a CharsetDetector.
10
LSP client
Separate process, async JSON-RPC. Autocomplete, hover, go-to-definition, diagnostics. Significant scope — save for last.
11
Large-file streaming load
Paged/lazy load for files >500MB. Specialist use case — current full-load strategy is fine for <200MB.
Container timings ÷3 for dev machine estimate. VS Code figures derived from Peng Lyu 2018 blog — no raw ms published, chart-proportion estimates only. GetLine comparison is the clearest win: flat buffer O(n) single-pass vs VS Code's acknowledged per-call weakness after many edits.

Where you genuinely beat VS Code now (not just match it):
GetLine after many edits is the standout — 25ms vs their ~300ms. The flat buffer approach (materialise once, slice by index) beats their per-call piece-tree lookup which was their own acknowledged weakness. ReplaceAll is another clear win — 73ms O(n) single-pass rewrite vs their O(n log n) sequential approach.
The three red items (1–3) are the critical path. Cursor/selection model → word boundaries → multi-cursor. These three together are what turns a buffer engine into something a front-end can actually drive. Without a cursor model you can't build a real editor on top of this — you'd have to implement cursor tracking externally, which defeats the purpose. Once those three are in, items 4–6 (scripting shell, incremental highlighting, diff) follow naturally because they all consume the cursor/selection API.
Items 7–11 are genuinely optional for your stated goal of a programmable text manipulation API. A scripting shell doesn't need bracket matching or LSP — those are UI features for a human typing in a text box. If the target is "Notepad++ macros but faster and on 100MB files", you could ship after items 1–4.