using TextEditor.Core.Language;

namespace TextEditor.Core.Cursor;

/// <summary>
/// Single-caret cursor with selection support for <see cref="TextDocument"/>.
///
/// Coordinate model
/// ───────────────
///   AnchorOffset — the fixed end of the selection (where Shift-click began, or == Active when
///                  there is no selection).
///   ActiveOffset — the moving end of the selection (where the caret visually sits).
///   SelectionStart/End — always min/max of the two; safe to pass directly to Insert/Delete/GetText.
///
/// Preferred-column
/// ────────────────
///   Up/Down movement preserves a goal visual column (_preferredColumn) so that
///   pressing Up/Down repeatedly through short lines snaps back to the original column
///   when a longer line is reached.  Any horizontal movement or direct positioning resets it.
///
/// VS Code movement conventions
/// ────────────────────────────
///   MoveLeft/Right with an active selection: collapses to the selection edge without moving further.
///   MoveWordLeft/Right: skip non-word chars then word chars (left), or word chars then non-word (right).
/// </summary>
public sealed class TextCursor
{
    private readonly TextDocument _doc;
    private int _anchorOffset;
    private int _activeOffset;
    private int _preferredColumn = -1;   // -1 = "not set, recompute from current column"

    // ── Construction ──────────────────────────────────────────────────────

    /// <param name="doc">The document this cursor is bound to.</param>
    /// <param name="offset">Initial caret position (clamped to [0, doc.Length]).</param>
    public TextCursor(TextDocument doc, int offset = 0)
    {
        _doc          = doc;
        _anchorOffset = _activeOffset = Clamp(offset);
    }

    /// <summary>The document this cursor operates on.</summary>
    public TextDocument Document => _doc;

    // ── Position ──────────────────────────────────────────────────────────

    /// <summary>The fixed (anchor) end of the selection.</summary>
    public int AnchorOffset => _anchorOffset;

    /// <summary>The moving end of the selection — where the caret visually sits.</summary>
    public int ActiveOffset => _activeOffset;

    /// <summary>Alias for <see cref="ActiveOffset"/>.</summary>
    public int CaretOffset => _activeOffset;

    /// <summary>Zero-based line index of the caret.</summary>
    public int CaretLine => _doc.OffsetToPosition(_activeOffset).Line;

    /// <summary>Zero-based column index of the caret.</summary>
    public int CaretColumn => _doc.OffsetToPosition(_activeOffset).Column;

    // ── Selection ─────────────────────────────────────────────────────────

    /// <summary>True when the anchor and active offsets differ.</summary>
    public bool HasSelection => _anchorOffset != _activeOffset;

    /// <summary>Left edge of the selection (≤ SelectionEnd).</summary>
    public int SelectionStart => Math.Min(_anchorOffset, _activeOffset);

    /// <summary>Right edge of the selection (≥ SelectionStart).</summary>
    public int SelectionEnd => Math.Max(_anchorOffset, _activeOffset);

    /// <summary>The selected text, or <see cref="string.Empty"/> when there is no selection.</summary>
    public string SelectedText => HasSelection
        ? _doc.GetText(SelectionStart, SelectionEnd - SelectionStart)
        : string.Empty;

    // ── Direct positioning ────────────────────────────────────────────────

    /// <summary>Move the caret to <paramref name="offset"/>, collapsing any selection.</summary>
    public void MoveTo(int offset)
    {
        _doc.FlushUndoGroup();
        _anchorOffset = _activeOffset = Clamp(offset);
        _preferredColumn = -1;
    }

    /// <summary>
    /// Extend (or start) a selection by moving the active end to <paramref name="offset"/>.
    /// The anchor stays where it is.
    /// </summary>
    public void SelectTo(int offset)
    {
        _doc.FlushUndoGroup();
        _activeOffset    = Clamp(offset);
        _preferredColumn = -1;
    }

    /// <summary>Explicitly set both anchor and active, forming an arbitrary selection.</summary>
    public void SetSelection(int anchor, int active)
    {
        _doc.FlushUndoGroup();
        _anchorOffset    = Clamp(anchor);
        _activeOffset    = Clamp(active);
        _preferredColumn = -1;
    }

    /// <summary>Collapse the selection to its left edge.</summary>
    public void CollapseToStart() => MoveTo(SelectionStart);

    /// <summary>Collapse the selection to its right edge.</summary>
    public void CollapseToEnd() => MoveTo(SelectionEnd);

    // ── Horizontal movement ───────────────────────────────────────────────

    /// <summary>
    /// Move left by <paramref name="count"/> grapheme clusters, collapsing any selection.
    /// When count == 1 and a selection is active, collapses to SelectionStart without moving further
    /// (VS Code behaviour for the Left arrow key).
    /// </summary>
    public void MoveLeft(int count = 1)
    {
        if (HasSelection && count == 1) { CollapseToStart(); return; }
        int offset = _activeOffset;
        for (int i = 0; i < count && offset > 0; i++)
            offset = GetPreviousClusterOffset(offset);
        MoveTo(offset);
    }

    /// <summary>
    /// Move right by <paramref name="count"/> grapheme clusters, collapsing any selection.
    /// When count == 1 and a selection is active, collapses to SelectionEnd without moving further.
    /// </summary>
    public void MoveRight(int count = 1)
    {
        if (HasSelection && count == 1) { CollapseToEnd(); return; }
        int offset = _activeOffset;
        for (int i = 0; i < count && offset < _doc.Length; i++)
            offset = GetNextClusterOffset(offset);
        MoveTo(offset);
    }

    /// <summary>Move to column 0 of the current line.</summary>
    public void MoveToLineStart()
    {
        int line = _doc.OffsetToPosition(_activeOffset).Line;
        MoveTo(_doc.PositionToOffset(line, 0));
    }

    /// <summary>Move to just after the last character on the current line (before the '\n', if any).</summary>
    public void MoveToLineEnd()
    {
        int line = _doc.OffsetToPosition(_activeOffset).Line;
        MoveTo(_doc.PositionToOffset(line, _doc.GetLine(line).Length));
    }

    /// <summary>Move to offset 0.</summary>
    public void MoveToDocumentStart() => MoveTo(0);

    /// <summary>Move to the end of the document.</summary>
    public void MoveToDocumentEnd() => MoveTo(_doc.Length);

    /// <summary>Move left to the start of the previous (or current) word.</summary>
    public void MoveWordLeft() => MoveTo(WordLeft(_activeOffset));

    /// <summary>Move right past the current word and any trailing non-word characters.</summary>
    public void MoveWordRight() => MoveTo(WordRight(_activeOffset));

    // ── Horizontal selection ──────────────────────────────────────────────

    /// <summary>Extend selection left by <paramref name="count"/> grapheme clusters.</summary>
    public void SelectLeft(int count = 1)
    {
        int offset = _activeOffset;
        for (int i = 0; i < count && offset > 0; i++)
            offset = GetPreviousClusterOffset(offset);
        SelectTo(offset);
    }

    /// <summary>Extend selection right by <paramref name="count"/> grapheme clusters.</summary>
    public void SelectRight(int count = 1)
    {
        int offset = _activeOffset;
        for (int i = 0; i < count && offset < _doc.Length; i++)
            offset = GetNextClusterOffset(offset);
        SelectTo(offset);
    }

    /// <summary>Extend selection to column 0 of the current line.</summary>
    public void SelectToLineStart()
    {
        int line = _doc.OffsetToPosition(_activeOffset).Line;
        SelectTo(_doc.PositionToOffset(line, 0));
    }

    /// <summary>Extend selection to the end of the current line.</summary>
    public void SelectToLineEnd()
    {
        int line = _doc.OffsetToPosition(_activeOffset).Line;
        SelectTo(_doc.PositionToOffset(line, _doc.GetLine(line).Length));
    }

    /// <summary>Extend selection to the start of the document.</summary>
    public void SelectToDocumentStart() => SelectTo(0);

    /// <summary>Extend selection to the end of the document.</summary>
    public void SelectToDocumentEnd() => SelectTo(_doc.Length);

    /// <summary>Extend selection left by one word.</summary>
    public void SelectWordLeft() => SelectTo(WordLeft(_activeOffset));

    /// <summary>Extend selection right by one word.</summary>
    public void SelectWordRight() => SelectTo(WordRight(_activeOffset));

    // ── Bulk selection ────────────────────────────────────────────────────

    /// <summary>Select the entire document (anchor=0, active=Length).</summary>
    public void SelectAll() => SetSelection(0, _doc.Length);

    /// <summary>
    /// Select the line at <paramref name="lineIndex"/> (or the caret's current line when null).
    /// The selection includes the trailing '\n' so that deleting it removes the whole line.
    /// On the last line (no trailing newline) the selection extends to the end of the document.
    /// </summary>
    public void SelectLine(int? lineIndex = null)
    {
        int line = lineIndex ?? CaretLine;
        line = Math.Clamp(line, 0, Math.Max(0, _doc.LineCount - 1));
        int start = _doc.PositionToOffset(line, 0);
        int end   = line + 1 < _doc.LineCount
            ? _doc.PositionToOffset(line + 1, 0)
            : _doc.Length;
        SetSelection(start, end);
    }

    /// <summary>
    /// Select the word under the caret.
    /// • If the caret sits on a word character, expands left and right through word characters.
    /// • If the caret sits on whitespace/punctuation, expands through the adjacent non-word, non-newline group.
    /// No-op on an empty document.
    /// </summary>
    public void SelectWordAtCaret()
    {
        var span = WordBoundary.GetWordAt(_doc, _activeOffset);
        if (!span.IsEmpty) SetSelection(span.Start, span.End);
    }

    // ── Vertical movement ─────────────────────────────────────────────────

    /// <summary>
    /// Move up by <paramref name="count"/> lines, preserving the preferred visual column.
    /// At the top of the document the caret moves to offset 0.
    /// </summary>
    public void MoveUp(int count = 1)
    {
        _doc.FlushUndoGroup();
        int col        = CapturePreferredColumn();
        int line       = _doc.OffsetToPosition(_activeOffset).Line;
        int targetLine = Math.Max(0, line - count);
        int targetCol  = Math.Min(col, _doc.GetLine(targetLine).Length);
        _anchorOffset  = _activeOffset = Clamp(_doc.PositionToOffset(targetLine, targetCol));
        // _preferredColumn intentionally preserved for continued vertical movement
    }

    /// <summary>
    /// Move down by <paramref name="count"/> lines, preserving the preferred visual column.
    /// At the bottom of the document the caret moves to the end of the last line.
    /// </summary>
    public void MoveDown(int count = 1)
    {
        _doc.FlushUndoGroup();
        int col        = CapturePreferredColumn();
        int line       = _doc.OffsetToPosition(_activeOffset).Line;
        int targetLine = Math.Min(_doc.LineCount - 1, line + count);
        int targetCol  = Math.Min(col, _doc.GetLine(targetLine).Length);
        _anchorOffset  = _activeOffset = Clamp(_doc.PositionToOffset(targetLine, targetCol));
    }

    /// <summary>Extend selection up by <paramref name="count"/> lines.</summary>
    public void SelectUp(int count = 1)
    {
        _doc.FlushUndoGroup();
        int col        = CapturePreferredColumn();
        int line       = _doc.OffsetToPosition(_activeOffset).Line;
        int targetLine = Math.Max(0, line - count);
        int targetCol  = Math.Min(col, _doc.GetLine(targetLine).Length);
        _activeOffset  = Clamp(_doc.PositionToOffset(targetLine, targetCol));
    }

    /// <summary>Extend selection down by <paramref name="count"/> lines.</summary>
    public void SelectDown(int count = 1)
    {
        _doc.FlushUndoGroup();
        int col        = CapturePreferredColumn();
        int line       = _doc.OffsetToPosition(_activeOffset).Line;
        int targetLine = Math.Min(_doc.LineCount - 1, line + count);
        int targetCol  = Math.Min(col, _doc.GetLine(targetLine).Length);
        _activeOffset  = Clamp(_doc.PositionToOffset(targetLine, targetCol));
    }

    // ── Editing through the cursor ────────────────────────────────────────

    /// <summary>
    /// Insert <paramref name="text"/> at the caret, first deleting any selection.
    /// The caret advances to just after the inserted (and EOL-normalised) text.
    /// This is the primary way to type through the cursor.
    /// </summary>
    public void InsertText(string text)
    {
        int start        = SelectionStart;
        int delLen       = SelectionEnd - SelectionStart;
        int oldLen       = _doc.Length;
        _doc.Replace(start, delLen, text);
        int insertedNorm = _doc.Length - oldLen + delLen;   // accounts for CRLF normalisation
        _anchorOffset    = _activeOffset = Clamp(start + insertedNorm);
        _preferredColumn = -1;
    }

    /// <summary>Delete the current selection. No-op when there is no selection.</summary>
    public void DeleteSelection()
    {
        if (!HasSelection) return;
        int start = SelectionStart;
        _doc.FlushUndoGroup(); // deleting a selection is always its own undo unit
        _doc.Delete(start, SelectionEnd - SelectionStart);
        _anchorOffset    = _activeOffset = Clamp(start);
        _preferredColumn = -1;
    }

    /// <summary>
    /// Delete <paramref name="count"/> grapheme clusters to the LEFT (Backspace key).
    /// Deletes the selection instead when one is active.
    /// </summary>
    public void DeleteLeft(int count = 1)
    {
        if (HasSelection) { DeleteSelection(); return; }
        int target = _activeOffset;
        for (int i = 0; i < count && target > 0; i++)
            target = GetPreviousClusterOffset(target);
        int delLen = _activeOffset - target;
        if (delLen == 0) return;
        _doc.Delete(target, delLen);
        _anchorOffset    = _activeOffset = Clamp(target);
        _preferredColumn = -1;
    }

    /// <summary>
    /// Delete <paramref name="count"/> grapheme clusters to the RIGHT (Delete key).
    /// Deletes the selection instead when one is active.
    /// </summary>
    public void DeleteRight(int count = 1)
    {
        if (HasSelection) { DeleteSelection(); return; }
        int target = _activeOffset;
        for (int i = 0; i < count && target < _doc.Length; i++)
            target = GetNextClusterOffset(target);
        int delLen = target - _activeOffset;
        if (delLen == 0) return;
        _doc.Delete(_activeOffset, delLen);
        _anchorOffset    = _activeOffset = Clamp(_activeOffset);
        _preferredColumn = -1;
    }

    /// <summary>Delete the word immediately to the LEFT of the caret (Ctrl+Backspace). Deletes selection if any.</summary>
    public void DeleteWordLeft()
    {
        if (HasSelection) { DeleteSelection(); return; }
        int target = WordLeft(_activeOffset);
        int delLen = _activeOffset - target;
        if (delLen == 0) return;
        _doc.Delete(target, delLen);
        _anchorOffset    = _activeOffset = Clamp(target);
        _preferredColumn = -1;
    }

    /// <summary>Delete the word immediately to the RIGHT of the caret (Ctrl+Delete). Deletes selection if any.</summary>
    public void DeleteWordRight()
    {
        if (HasSelection) { DeleteSelection(); return; }
        int target = WordRight(_activeOffset);
        int delLen = target - _activeOffset;
        if (delLen == 0) return;
        _doc.Delete(_activeOffset, delLen);
        _anchorOffset    = _activeOffset = Clamp(_activeOffset);
        _preferredColumn = -1;
    }

    // ── Word boundary helpers (delegates to WordBoundary — single source of truth) ──

    /// <summary>Returns true if <paramref name="c"/> is a word character (letter, digit, or underscore).</summary>
    public static bool IsWordChar(char c) => WordBoundary.IsWordChar(c);

    /// <summary>
    /// Returns the offset of the start of the word or non-word group immediately to the
    /// left of <paramref name="offset"/>.
    /// </summary>
    public int WordLeft(int offset) => WordBoundary.GetWordBoundaryLeft(_doc, offset);

    /// <summary>
    /// Returns the offset after the word or non-word group immediately to the right of
    /// <paramref name="offset"/>.
    /// </summary>
    public int WordRight(int offset) => WordBoundary.GetWordBoundaryRight(_doc, offset);

    // ── Private ───────────────────────────────────────────────────────────

    private int Clamp(int offset) => Math.Clamp(offset, 0, _doc.Length);

    /// <summary>
    /// Returns the preferred column for vertical movement, computing and caching it from
    /// the current caret position on the first call after a horizontal move.
    /// </summary>
    private int CapturePreferredColumn()
    {
        if (_preferredColumn < 0)
            _preferredColumn = _doc.OffsetToPosition(_activeOffset).Column;
        return _preferredColumn;
    }

    // ── Grapheme-cluster navigation helpers ───────────────────────────────

    /// <summary>
    /// Returns the code-unit offset of the start of the next grapheme cluster
    /// after <paramref name="offset"/>, crossing a newline as a single step when
    /// the cursor is at the end of a line.
    /// </summary>
    private int GetNextClusterOffset(int offset)
    {
        var (line, col) = _doc.OffsetToPosition(offset);
        string lineText = _doc.GetLine(line);
        if (col < lineText.Length)
        {
            // Within the line: advance by one grapheme cluster.
            int nextCol = GraphemeHelper.NextCluster(lineText.AsSpan(), col);
            return _doc.PositionToOffset(line, nextCol);
        }
        // At end of line → step over the newline character (or stay at doc end).
        return Math.Min(_doc.Length, offset + 1);
    }

    /// <summary>
    /// Returns the code-unit offset of the start of the grapheme cluster immediately
    /// before <paramref name="offset"/>, crossing a newline as a single step when
    /// the cursor is at the start of a line.
    /// </summary>
    private int GetPreviousClusterOffset(int offset)
    {
        var (line, col) = _doc.OffsetToPosition(offset);
        if (col > 0)
        {
            string lineText = _doc.GetLine(line);
            int prevCol = GraphemeHelper.PreviousCluster(lineText.AsSpan(), col);
            return _doc.PositionToOffset(line, prevCol);
        }
        // At start of line → step over the preceding newline (or stay at 0).
        return Math.Max(0, offset - 1);
    }
}
