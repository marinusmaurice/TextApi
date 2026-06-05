# JSON Serialization

`OperationMapper` serializes and deserializes operations to and from JSON. This is the bridge between external systems (LLMs, APIs, config files) and the TextAPI execution engine.

**Navigation:** [Overview](index.md) | [Operations](operations.md) | [Examples](examples-pipeline.md) | [Pipeline](pipeline.md)

## JSON Format Rules

- Every operation object must have a `"type"` field with the operation's type string (e.g. `"REPLACE_ALL"`).
- All other property names are **camelCase** (e.g. `caseSensitive`, not `CaseSensitive`).
- Type string matching is **case-sensitive** and must be exact.
- Unknown fields are silently ignored.
- Missing optional fields use their defaults.

## Two JSON Formats

### Bare Array

A JSON array of operation objects:

```json
[
  { "type": "REPLACE_ALL", "find": "foo", "replace": "bar" },
  { "type": "TRIM_TRAILING_WHITESPACE" },
  { "type": "SORT_LINES", "descending": true }
]
```

### Pipeline Object

A JSON object with an `"operations"` array (useful when you want to add pipeline-level metadata later):

```json
{
  "operations": [
	{ "type": "REPLACE_ALL", "find": "foo", "replace": "bar" },
	{ "type": "TRIM_TRAILING_WHITESPACE" }
  ]
}
```

## OperationMapper API

| Method | Description |
|---|---|
| `FromJson(string json)` | Parse a bare array. Returns `IReadOnlyList<IDocumentOperation>`. Throws `JsonException` if root is not an array or a type is unknown. |
| `FromPipelineJson(string json)` | Parse a pipeline object (`{"operations":[...]}`). Throws `JsonException` if the key is missing. |
| `ToJson(IDocumentOperation op)` | Serialize one operation to a JSON object string. |
| `ToPipelineJson(IEnumerable<IDocumentOperation> ops)` | Serialize a list of operations to a pipeline object string. |

## Deserializing

```csharp
using TextAPI.Operations.Serialization;

// Bare array
var ops = OperationMapper.FromJson("""
[
  {"type":"REPLACE_ALL","find":"TODO","replace":"DONE"},
  {"type":"SORT_LINES"}
]
""");

// Pipeline object
var ops2 = OperationMapper.FromPipelineJson(jsonString);

// Run them
var result = new DocumentPipeline(doc).Add(ops).Execute();
```

## Serializing

```csharp
// Single op
var op   = new ReplaceAllOperation { Find = "a", Replace = "b", CaseSensitive = false };
string json = OperationMapper.ToJson(op);
// {"type":"REPLACE_ALL","find":"a","replace":"b","caseSensitive":false,"wholeWord":false}

// List of ops as pipeline JSON
IDocumentOperation[] ops = [
	new AppendOperation  { Text = " World" },
	new PrependOperation { Text = "Hello" }
];
string pipeline = OperationMapper.ToPipelineJson(ops);
```

## Usage with DocumentPipeline

```csharp
// From external source (API, file, LLM output)
string externalJson = File.ReadAllText("operations.json");

// Deserialize and execute
var ops = OperationMapper.FromPipelineJson(externalJson);
var result = new DocumentPipeline(doc)
	.Add(ops)
	.Execute();
```
