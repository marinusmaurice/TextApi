using TextAPI.Core;
using TextAPI.Core.Folding;
using TextAPI.Core.StickyScroll;

// ─────────────────────────────────────────────────────────────────────────────
// Sticky scroll context provider — demo
//
// Simulates scrolling a large C#-like class through several viewport positions
// and prints the sticky-scroll header chain at each position, just like VS Code
// shows at the top of the editor.
// ─────────────────────────────────────────────────────────────────────────────

const string Source = """
    namespace Acme.Services
    {
        public class OrderService
        {
            private readonly IRepository _repo;

            public OrderService(IRepository repo)
            {
                _repo = repo;
            }

            public Order? GetOrder(int id)
            {
                if (id <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(id));
                }

                return _repo.Find(id);
            }

            public IEnumerable<Order> GetAll()
            {
                foreach (var order in _repo.All())
                {
                    if (order.IsActive)
                    {
                        yield return order;
                    }
                }
            }
        }
    }
    """;

var doc = new TextDocument();
doc.Load(Source);

var foldingModel = doc.GetFoldingModel();
foldingModel.UpdateRegions(new BraceFoldingStrategy());

// Print the full document with line numbers so we can reason about positions.
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Source document (line numbers 0-based)");
Console.WriteLine("═══════════════════════════════════════════════════════");
string[] lines = Source.Split('\n');
for (int i = 0; i < lines.Length; i++)
    Console.WriteLine($"  {i,2}: {lines[i]}");

Console.WriteLine($"\nFold regions detected: {foldingModel.Regions.Count}");
foreach (var r in foldingModel.Regions)
    Console.WriteLine($"  [{r.StartLine,2}–{r.EndLine,2}] \"{r.Label}\"");

// Simulate scrolling through 5 viewport positions.
int[] viewports = [0, 8, 14, 22, 25];

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("  Sticky-scroll context at each viewport position");
Console.WriteLine("═══════════════════════════════════════════════════════");

foreach (int firstVisible in viewports)
{
    var ctx = StickyScroll.GetContext(foldingModel, firstVisible);

    Console.WriteLine($"\nfirstVisibleLine = {firstVisible}  (showing: \"{Truncate(lines[Math.Min(firstVisible, lines.Length - 1)], 40)}\")");

    if (ctx.Count == 0)
    {
        Console.WriteLine("  [no sticky headers — at the top or between scopes]");
    }
    else
    {
        Console.WriteLine("  Sticky headers (outermost first):");
        foreach (var entry in ctx)
            Console.WriteLine($"    line {entry.DocumentLine,2}: {entry.Label}");
    }
}

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("  Done.");

static string Truncate(string s, int max)
    => s.Length <= max ? s : s[..max] + "…";
