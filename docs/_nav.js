// Inject shared nav and highlight active link
(function () {
  var html = '<nav class="sidebar">'
    + '<div class="sidebar-logo">TextAPI</div>'
    + '<div class="sidebar-section">Getting Started</div>'
    + '<a href="index.html">Overview</a>'
    + '<a href="quickstart.html">Quick Start</a>'
    + '<a href="scenarios.html">Scenarios &amp; Cookbook</a>'
    + '<div class="sidebar-section">Core API</div>'
    + '<a href="textdocument.html">TextDocument</a>'
    + '<a href="cursor.html">Cursors</a>'
    + '<a href="search.html">Search</a>'
    + '<a href="decorations.html">Decorations</a>'
    + '<a href="undo-redo.html">Undo / Redo</a>'
    + '<a href="encoding-eol.html">Encoding &amp; EOL</a>'
    + '<a href="diff.html">Diff</a>'
    + '<a href="advanced.html">Advanced Features</a>'
    + '<div class="sidebar-section">Operations Layer</div>'
    + '<a href="operations.html">All 27 Operations</a>'
    + '<a href="pipeline.html">Pipeline &amp; Rollback</a>'
    + '<a href="serialization.html">JSON Serialization</a>'
    + '<div class="sidebar-section">Scripting</div>'
    + '<a href="scripting.html">Script DSL</a>'
    + '<a href="repl.html">C# REPL</a>'
    + '<div class="sidebar-section">Examples</div>'
    + '<a href="examples.html">Examples Hub</a>'
    + '<a href="examples-textdocument.html">TextDocument</a>'
    + '<a href="examples-operations.html">All 27 Operations</a>'
    + '<a href="examples-pipeline.html">Pipeline &amp; Serialization</a>'
    + '<a href="examples-cursor.html">Cursors</a>'
    + '<a href="examples-search.html">Search, Decorations &amp; Diff</a>'
    + '<a href="examples-scripting.html">Script DSL &amp; REPL</a>'
    + '<a href="examples-advanced.html">Advanced Features</a>'
    + '</nav>';

  var div = document.createElement('div');
  div.innerHTML = html;
  document.body.insertBefore(div.firstElementChild, document.body.firstChild);

  var cur = location.pathname.split('/').pop() || 'index.html';
  if (!cur) cur = 'index.html';
  document.querySelectorAll('.sidebar a').forEach(function (a) {
    if (a.getAttribute('href') === cur) a.classList.add('active');
  });
})();
