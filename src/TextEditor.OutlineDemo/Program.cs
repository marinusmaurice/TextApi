using TextEditor.Core;
using TextEditor.Core.Folding;
using TextEditor.Core.Outline;

// ─────────────────────────────────────────────────────────────────────────────
// Document outline — demo
//
// Loads a C#-like source file, builds the FoldingModel, then derives the
// outline tree and prints it with tree-drawing characters, matching what IDEs
// display in their "Outline" / "Structure" side panels.
// ─────────────────────────────────────────────────────────────────────────────

const string Source = """
    namespace Acme
    {
        public class UserService
        {
            private readonly ICache _cache;

            public UserService(ICache cache)
            {
                _cache = cache;
            }

            public User? Find(int id)
            {
                if (_cache.TryGet(id, out var user))
                {
                    return user;
                }

                return null;
            }

            public IEnumerable<User> GetActive()
            {
                foreach (var u in _cache.All())
                {
                    if (u.IsActive)
                    {
                        yield return u;
                    }
                }
            }

            private static class Helpers
            {
                public static string Format(User u)
                {
                    return $"{u.Name} ({u.Id})";
                }
            }
        }
    }
    """;

var doc = new TextDocument();
doc.Load(Source);

var foldingModel = doc.GetFoldingModel();
foldingModel.UpdateRegions(new BraceFoldingStrategy());

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Document outline");
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine($"  {foldingModel.Regions.Count} fold region(s) detected\n");

var outline = OutlineProvider.GetOutline(foldingModel);

PrintTree(outline, "", true);

Console.WriteLine("\n─── All nodes (depth-first, with metadata) ───────────");
PrintFlat(outline);

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("  Done.");

// ─── Helpers ─────────────────────────────────────────────────────────────────

static void PrintTree(IReadOnlyList<OutlineNode> nodes, string prefix, bool isRoot)
{
    for (int i = 0; i < nodes.Count; i++)
    {
        var node  = nodes[i];
        bool last = i == nodes.Count - 1;

        string connector = isRoot ? "  " : (last ? "└─ " : "├─ ");
        string childPfx  = isRoot ? "  " : (last ? "   " : "│  ");

        Console.WriteLine($"{prefix}{connector}[{node.StartLine,2}–{node.EndLine,2}] {node.Label}");
        if (node.Children.Count > 0)
            PrintTree(node.Children, prefix + childPfx, false);
    }
}

static void PrintFlat(IReadOnlyList<OutlineNode> roots)
{
    void Visit(OutlineNode n)
    {
        string indent = new string(' ', n.Depth * 2);
        Console.WriteLine($"  depth={n.Depth}  line {n.StartLine,2}–{n.EndLine,2}  {indent}{n.Label}");
        foreach (var c in n.Children) Visit(c);
    }
    foreach (var r in roots) Visit(r);
}
