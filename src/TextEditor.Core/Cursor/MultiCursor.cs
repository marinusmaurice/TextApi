using TextEditor.Core.Commands;

namespace TextEditor.Core.Cursor;

/// <summary>
/// Manages N independent cursors over a single <see cref="TextDocument"/>.
///
/// Movement operations apply to every cursor in parallel.
/// Edit operations apply to every cursor as a SINGLE undo unit (one Undo() call
/// reverts all of them), with offsets adjusted bottom-to-top so that applying
/// an edit at a high offset never shifts the starting points of lower edits.
///
/// Invariants maintained after every public call:
///   1. The cursor list is sorted ascending by SelectionStart (then SelectionEnd descending).
///   2. No two cursors have overlapping [SelectionStart, SelectionEnd) ranges,
///      and no two collapsed cursors share the same offset.
///   3. Count ≥ 1 at all times.
///   4. PrimaryIndex is always a valid index into the list.
/// </summary>
public sealed class MultiCursor
{
    private readonly TextDocument    _doc;
    private readonly List<TextCursor> _cursors;
    private int _primaryIndex;

    // ── Construction ───────────────────────────────────────────────────────

    public MultiCursor(TextDocument doc)
    {
        _doc          = doc;
        _cursors      = [new TextCursor(doc, 0)];
        _primaryIndex = 0;
    }

    // ── Collection management ──────────────────────────────────────────────

    /// <summary>Number of active cursors.</summary>
    public int Count => _cursors.Count;

    /// <summary>
    /// The primary cursor — the last one added, or index 0 after a merge.
    /// Use this when the UI needs exactly one caret to follow (scrolling, status bar).
    /// </summary>
    public TextCursor Primary => _cursors[_primaryIndex];

    /// <summary>All active cursors in ascending-offset order.</summary>
    public IReadOnlyList<TextCursor> All => _cursors;

    /// <summary>Add a new collapsed cursor at <paramref name="offset"/>. It becomes Primary.</summary>
    public void AddCursor(int offset)
    {
        _cursors.Add(new TextCursor(_doc, offset));
        _primaryIndex = _cursors.Count - 1;
        SortAndMerge();
    }

    /// <summary>Add a cursor with an explicit selection. It becomes Primary.</summary>
    public void AddCursor(int anchor, int active)
    {
        var c = new TextCursor(_doc, anchor);
        c.SelectTo(active);
        _cursors.Add(c);
        _primaryIndex = _cursors.Count - 1;
        SortAndMerge();
    }

    /// <summary>
    /// Remove the cursor at list <paramref name="index"/>.
    /// No-op when there is only one cursor (always keeps at least one).
    /// </summary>
    public void RemoveCursor(int index)
    {
        if (_cursors.Count == 1) return;
        _cursors.RemoveAt(index);
        if (_primaryIndex >= _cursors.Count)
            _primaryIndex = _cursors.Count - 1;
        else if (_primaryIndex > index)
            _primaryIndex--;
    }

    /// <summary>Remove all cursors and replace with a single collapsed cursor at offset 0.</summary>
    public void Clear()
    {
        _cursors.Clear();
        _cursors.Add(new TextCursor(_doc, 0));
        _primaryIndex = 0;
    }

    /// <summary>
    /// Replace all cursors with a single collapsed cursor at <paramref name="offset"/>.
    /// The new cursor becomes Primary.
    /// </summary>
    public void SetSingle(int offset)
    {
        _cursors.Clear();
        _cursors.Add(new TextCursor(_doc, offset));
        _primaryIndex = 0;
    }

    // ── Horizontal movement ────────────────────────────────────────────────

    public void MoveLeft(int count = 1)   { foreach (var c in _cursors) c.MoveLeft(count);   SortAndMerge(); }
    public void MoveRight(int count = 1)  { foreach (var c in _cursors) c.MoveRight(count);  SortAndMerge(); }
    public void MoveToLineStart()         { foreach (var c in _cursors) c.MoveToLineStart();  SortAndMerge(); }
    public void MoveToLineEnd()           { foreach (var c in _cursors) c.MoveToLineEnd();    SortAndMerge(); }
    public void MoveToDocumentStart()     { foreach (var c in _cursors) c.MoveToDocumentStart(); SortAndMerge(); }
    public void MoveToDocumentEnd()       { foreach (var c in _cursors) c.MoveToDocumentEnd();   SortAndMerge(); }
    public void MoveWordLeft()            { foreach (var c in _cursors) c.MoveWordLeft();      SortAndMerge(); }
    public void MoveWordRight()           { foreach (var c in _cursors) c.MoveWordRight();     SortAndMerge(); }

    // ── Horizontal selection ───────────────────────────────────────────────

    public void SelectLeft(int count = 1)  { foreach (var c in _cursors) c.SelectLeft(count);          SortAndMerge(); }
    public void SelectRight(int count = 1) { foreach (var c in _cursors) c.SelectRight(count);         SortAndMerge(); }
    public void SelectToLineStart()        { foreach (var c in _cursors) c.SelectToLineStart();         SortAndMerge(); }
    public void SelectToLineEnd()          { foreach (var c in _cursors) c.SelectToLineEnd();           SortAndMerge(); }
    public void SelectToDocumentStart()    { foreach (var c in _cursors) c.SelectToDocumentStart();     SortAndMerge(); }
    public void SelectToDocumentEnd()      { foreach (var c in _cursors) c.SelectToDocumentEnd();       SortAndMerge(); }
    public void SelectWordLeft()           { foreach (var c in _cursors) c.SelectWordLeft();            SortAndMerge(); }
    public void SelectWordRight()          { foreach (var c in _cursors) c.SelectWordRight();           SortAndMerge(); }

    // ── Bulk selection ─────────────────────────────────────────────────────

    /// <summary>
    /// Select the entire document at every cursor.
    /// All cursors become identical and collapse to one.
    /// </summary>
    public void SelectAll()
    {
        foreach (var c in _cursors) c.SelectAll();
        SortAndMerge();   // all identical → merge to 1
    }

    /// <summary>Each cursor selects its current line.</summary>
    public void SelectLine() { foreach (var c in _cursors) c.SelectLine(); SortAndMerge(); }

    /// <summary>Each cursor selects the word under its caret.</summary>
    public void SelectWordAtCaret() { foreach (var c in _cursors) c.SelectWordAtCaret(); SortAndMerge(); }

    /// <summary>
    /// Replace all cursors with one collapsed cursor per line from
    /// <paramref name="startLine"/> to <paramref name="endLine"/> (both inclusive),
    /// each placed at <paramref name="column"/> (clamped to line length).
    /// The bottom cursor becomes Primary.
    /// </summary>
    public void AddColumnSelection(int startLine, int endLine, int column)
    {
        _cursors.Clear();
        for (int line = startLine; line <= endLine; line++)
        {
            int lineLen = _doc.GetLine(line).Length;
            int col     = Math.Min(column, lineLen);
            int offset  = _doc.PositionToOffset(line, col);
            _cursors.Add(new TextCursor(_doc, offset));
        }
        _primaryIndex = Math.Max(0, _cursors.Count - 1);
        SortAndMerge();
    }

    // ── Vertical movement ──────────────────────────────────────────────────

    public void MoveUp(int count = 1)    { foreach (var c in _cursors) c.MoveUp(count);    SortAndMerge(); }
    public void MoveDown(int count = 1)  { foreach (var c in _cursors) c.MoveDown(count);  SortAndMerge(); }
    public void SelectUp(int count = 1)  { foreach (var c in _cursors) c.SelectUp(count);  SortAndMerge(); }
    public void SelectDown(int count = 1){ foreach (var c in _cursors) c.SelectDown(count); SortAndMerge(); }

    // ── Editing — all cursors, one undo unit ───────────────────────────────

    /// <summary>Insert <paramref name="text"/> at every cursor. One undo step.</summary>
    public void InsertText(string text)
    {
        string norm = Normalize(text);
        ApplyBatchEdit(i =>
        {
            var c = _cursors[i];
            return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, norm);
        }, "Multi-cursor insert");
    }

    /// <summary>Backspace at every cursor. One undo step.</summary>
    public void DeleteLeft(int count = 1) => ApplyBatchEdit(i =>
    {
        var c = _cursors[i];
        if (c.HasSelection)
            return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, "");
        int start = Math.Max(0, c.CaretOffset - count);
        return new EditDesc(i, start, c.CaretOffset - start, "");
    }, "Multi-cursor delete left");

    /// <summary>Delete key at every cursor. One undo step.</summary>
    public void DeleteRight(int count = 1) => ApplyBatchEdit(i =>
    {
        var c = _cursors[i];
        if (c.HasSelection)
            return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, "");
        int delLen = Math.Min(count, _doc.Length - c.CaretOffset);
        return new EditDesc(i, c.CaretOffset, delLen, "");
    }, "Multi-cursor delete right");

    /// <summary>Ctrl+Backspace at every cursor. One undo step.</summary>
    public void DeleteWordLeft() => ApplyBatchEdit(i =>
    {
        var c = _cursors[i];
        if (c.HasSelection)
            return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, "");
        int start = c.WordLeft(c.CaretOffset);
        return new EditDesc(i, start, c.CaretOffset - start, "");
    }, "Multi-cursor delete word left");

    /// <summary>Ctrl+Delete at every cursor. One undo step.</summary>
    public void DeleteWordRight() => ApplyBatchEdit(i =>
    {
        var c = _cursors[i];
        if (c.HasSelection)
            return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, "");
        int end = c.WordRight(c.CaretOffset);
        return new EditDesc(i, c.CaretOffset, end - c.CaretOffset, "");
    }, "Multi-cursor delete word right");

    /// <summary>Delete every cursor's selection. No-op for collapsed cursors. One undo step.</summary>
    public void DeleteSelection() => ApplyBatchEdit(i =>
    {
        var c = _cursors[i];
        return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, "");
    }, "Multi-cursor delete selection");

    // ── Paste — distributed or broadcast ──────────────────────────────────

    /// <summary>
    /// Paste clipboard lines into the document.
    ///
    /// <para><b>Distributed paste</b> — when <paramref name="lines"/>.Count equals
    /// the number of active cursors, each cursor receives exactly one line in
    /// ascending order (top cursor gets <c>lines[0]</c>, etc.).</para>
    ///
    /// <para><b>Broadcast paste</b> — any other count (including a single line, or
    /// a count that does not match the cursor count) joins all lines with '\n' and
    /// inserts the same text at every cursor, identical to
    /// <see cref="InsertText"/>.</para>
    ///
    /// <para>In both cases the entire operation is a single undo step.</para>
    /// </summary>
    public void Paste(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;

        if (lines.Count == _cursors.Count)
        {
            // Distributed paste: cursor[i] gets lines[i].
            // Cursors are already sorted ascending; lines[0] → topmost cursor.
            ApplyBatchEdit(i =>
            {
                var c    = _cursors[i];
                string t = Normalize(lines[i]);
                return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, t);
            }, "Multi-cursor distributed paste");
        }
        else
        {
            // Broadcast paste: join and insert the same text at every cursor.
            string joined = Normalize(string.Join("\n", lines));
            ApplyBatchEdit(i =>
            {
                var c = _cursors[i];
                return new EditDesc(i, c.SelectionStart, c.SelectionEnd - c.SelectionStart, joined);
            }, "Multi-cursor broadcast paste");
        }
    }

    // ── Core batching engine ───────────────────────────────────────────────

    /// <summary>
    /// Core engine for all edit operations.
    /// <para>
    /// Algorithm (bottom-to-top execution, top-to-bottom repositioning):
    ///   1. Compute one edit descriptor per cursor (ascending offset order).
    ///   2. Build commands in descending offset order (bottom cursor first).
    ///      Executing a high-offset edit never shifts the start of a lower-offset edit.
    ///   3. Fire all commands as one <see cref="CompositeCommand"/> via
    ///      <see cref="TextDocument.ExecuteComposite"/>.
    ///   4. Reposition each cursor using a cumulative delta (top-to-bottom).
    ///   5. Sort-and-merge in case any cursors now overlap.
    /// </para>
    /// </summary>
    private void ApplyBatchEdit(Func<int, EditDesc> compute, string description)
    {
        var edits = new EditDesc[_cursors.Count];
        for (int i = 0; i < _cursors.Count; i++)
            edits[i] = compute(i);

        // Build commands bottom-to-top (highest offset first).
        var cmds = new List<IEditorCommand>(_cursors.Count);
        for (int i = edits.Length - 1; i >= 0; i--)
        {
            var e = edits[i];
            if (e.DeleteLen == 0 && e.Text.Length == 0) continue;
            cmds.Add(new ReplaceCommand(_doc.InternalBuffer, e.Start, e.DeleteLen, e.Text));
        }

        if (cmds.Count == 0) return;

        _doc.ExecuteComposite(description, cmds);

        // Reposition cursors top-to-bottom using cumulative offset shift.
        int cumDelta = 0;
        for (int i = 0; i < edits.Length; i++)
        {
            var e        = edits[i];
            int adjStart = e.Start + cumDelta;
            int newCaret = adjStart + e.Text.Length;
            _cursors[e.Idx].SetSelection(newCaret, newCaret);
            cumDelta    += e.Text.Length - e.DeleteLen;
        }

        SortAndMerge();
    }

    // ── Sort and merge ─────────────────────────────────────────────────────

    private void SortAndMerge()
    {
        var primary = _cursors[_primaryIndex];

        // Sort ascending by SelectionStart; ties: wider selection first.
        _cursors.Sort((a, b) =>
        {
            int cmp = a.SelectionStart.CompareTo(b.SelectionStart);
            return cmp != 0 ? cmp : b.SelectionEnd.CompareTo(a.SelectionEnd);
        });

        // Single-pass merge.
        var result = new List<TextCursor>(_cursors.Count) { _cursors[0] };
        for (int i = 1; i < _cursors.Count; i++)
        {
            var last = result[^1];
            var cur  = _cursors[i];

            // Overlap: cur starts before last ends, OR two collapsed cursors at same point.
            bool overlap = cur.SelectionStart < last.SelectionEnd
                || (cur.SelectionStart == last.SelectionEnd
                    && !cur.HasSelection && !last.HasSelection);

            if (overlap)
            {
                int mergedEnd = Math.Max(last.SelectionEnd, cur.SelectionEnd);
                last.SetSelection(last.SelectionStart, mergedEnd);
                if (ReferenceEquals(cur, primary)) primary = last;
            }
            else
            {
                result.Add(cur);
            }
        }

        _cursors.Clear();
        _cursors.AddRange(result);
        _primaryIndex = Math.Max(0, _cursors.IndexOf(primary));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Pre-normalise CRLF so the ReplaceCommand stores the exact final length.</summary>
    private static string Normalize(string text)
        => text.Contains('\r') ? text.Replace("\r\n", "\n").Replace("\r", "\n") : text;

    // Per-cursor edit descriptor used only by ApplyBatchEdit.
    private readonly record struct EditDesc(int Idx, int Start, int DeleteLen, string Text);
}
