# Encoding & EOL

TextAPI automatically detects encoding (BOM, UTF-8, Windows-1252, Latin-1) and EOL style (LF, CRLF, CR) when loading a file. By default it round-trips both exactly — no silent conversions unless you ask for them.

**Navigation:** [Overview](index.md) | [TextDocument](textdocument.md) | [Examples](examples-textdocument.md)

## Loading Files

```csharp
// Auto-detect encoding and EOL
await doc.LoadFileAsync("/path/to/file.txt");

// Override encoding (skip auto-detection)
await doc.LoadFileAsync("/path/to/file.txt", encoding: System.Text.Encoding.Latin1);
```

## Detected Properties

| Property | Type | Description |
|---|---|---|
| `DetectedEncoding` | `DetectedEncoding?` | Encoding detected on load. Null when loaded from a string. |
| `HasBom` | `bool` | True if the loaded file began with a BOM. |
| `OriginalEolStyle` | `EolStyle` | EOL style detected on load (`LF`, `CRLF`, or `CR`). |
| `SaveEolStyle` | `EolStyle` | EOL used when saving. Defaults to `OriginalEolStyle`. |
| `SaveEncoding` | `Encoding?` | Override encoding for the next save. Null = preserve original. |

## DetectedEncoding

| Property | Type | Description |
|---|---|---|
| `Encoding` | `System.Text.Encoding` | The detected (or defaulted) encoding. |
| `HasBom` | `bool` | True when the stream began with a recognized BOM. |
| `BomLength` | `int` | Number of BOM bytes (0, 2, 3, or 4). |
| `Confidence` | `EncodingConfidence` | Reliability of the detection result. |

## EncodingConfidence Enum

| Value | Meaning |
|---|---|
| `Bom` | Byte-order mark found — definitive. |
| `HighConfidence` | Valid UTF-8 multi-byte sequences observed. |
| `Heuristic` | Bytes in Windows-1252 or Latin-1 range. |
| `Fallback` | All bytes were ASCII; UTF-8 assumed. |

## Saving with Encoding

```csharp
// Use detected encoding and EOL (default)
await doc.SaveFileAsync();

// Change EOL for this save only
doc.SaveEolStyle = EolStyle.LF;
await doc.SaveFileAsync();

// Change encoding for this save only
doc.SaveEncoding = System.Text.Encoding.UTF8;
await doc.SaveFileAsync();

// Reset to defaults
doc.SaveEncoding = null;
doc.SaveEolStyle = doc.OriginalEolStyle;
```

## EolStyle Enum

| Value | Style |
|---|---|
| `LF` | Line Feed (`\n`) — Unix/Linux/Mac |
| `CRLF` | Carriage Return + Line Feed (`\r\n`) — Windows |
| `CR` | Carriage Return (`\r`) — Classic Mac |

## Encoding Detection Algorithm

TextAPI uses a multi-step detection:

1. Check for BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE)
2. Scan for valid UTF-8 multi-byte sequences
3. Fall back to Windows-1252 or Latin-1 if bytes are in valid range
4. Default to UTF-8 for pure ASCII

Confidence levels help determine reliability of the detection.
