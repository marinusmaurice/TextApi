namespace TextAPI.Core.EOL;

public enum EolStyle { Lf, CrLf, Cr, Mixed }

/// <summary>
/// High-performance EOL registry.
/// PERF: single-pass detect+normalise, vectorised CountLf, pooled stream writer.
/// </summary>
public sealed class EolRegistry
{
    public EolStyle OriginalStyle { get; internal set; } = EolStyle.Lf;
    public EolStyle SaveStyle     { get; set; }         = EolStyle.Lf;

    public char[] NormaliseToCharArray(ReadOnlySpan<char> raw)
    {
        int lf = 0, cr = 0, crlf = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\r') { if (i + 1 < raw.Length && raw[i+1] == '\n') { crlf++; i++; } else cr++; }
            else if (raw[i] == '\n') lf++;
        }
        int total = lf + cr + crlf;
        OriginalStyle = total == 0 ? EolStyle.Lf : crlf == total ? EolStyle.CrLf : cr == total ? EolStyle.Cr : lf == total ? EolStyle.Lf : EolStyle.Mixed;
        SaveStyle = OriginalStyle == EolStyle.Mixed ? EolStyle.Lf : OriginalStyle;

        if (cr == 0 && crlf == 0) return raw.ToArray();   // fast path: pure LF, one copy

        var result = new char[raw.Length - crlf];
        int w = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\r') { result[w++] = '\n'; if (i+1 < raw.Length && raw[i+1] == '\n') i++; }
            else result[w++] = c;
        }
        return result;
    }

    public static int NormaliseInsertInto(ReadOnlySpan<char> input, Span<char> output)
    {
        int w = 0;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '\r') { output[w++] = '\n'; if (i+1 < input.Length && input[i+1] == '\n') i++; }
            else output[w++] = c;
        }
        return w;
    }

    // net8: MemoryExtensions.Count uses SSE2/AVX2 vectorisation
    public static int CountLf(ReadOnlySpan<char> span) => span.Count('\n');
    public static int CountLfInArray(char[] buf, int start, int length) => new ReadOnlySpan<char>(buf, start, length).Count('\n');

    public string EolSequence => SaveStyle switch { EolStyle.CrLf => "\r\n", EolStyle.Cr => "\r", _ => "\n" };

    public async Task WriteToStreamAsync(IEnumerable<ReadOnlyMemory<char>> pieces, Stream stream, System.Text.Encoding encoding, System.Threading.CancellationToken ct = default)
    {
        if (SaveStyle == EolStyle.Lf)
        {
            foreach (var mem in pieces)
            {
                // Can't use Span in async — work via array segment
                var arr   = mem.ToArray();
                var bytes = encoding.GetBytes(arr);
                await stream.WriteAsync(bytes, ct);
            }
            return;
        }
        bool isCrLf = SaveStyle == EolStyle.CrLf;
        char[] outBuf  = System.Buffers.ArrayPool<char>.Shared.Rent(65536);
        byte[] byteBuf = System.Buffers.ArrayPool<byte>.Shared.Rent(encoding.GetMaxByteCount(65536));
        try
        {
            foreach (var mem in pieces)
            {
                // Copy to local array segment — Span not allowed across awaits
                var arr = mem.ToArray();
                int pos = 0;
                while (pos < arr.Length)
                {
                    int w = 0, chunk = Math.Min(arr.Length - pos, outBuf.Length / 2);
                    for (int i = pos; i < pos + chunk; i++)
                    {
                        if (arr[i] == '\n') { if (isCrLf) outBuf[w++] = '\r'; outBuf[w++] = isCrLf ? '\n' : '\r'; }
                        else outBuf[w++] = arr[i];
                    }
                    int bc = encoding.GetBytes(outBuf, 0, w, byteBuf, 0);
                    await stream.WriteAsync(byteBuf.AsMemory(0, bc), ct);
                    pos += chunk;
                }
            }
        }
        finally { System.Buffers.ArrayPool<char>.Shared.Return(outBuf); System.Buffers.ArrayPool<byte>.Shared.Return(byteBuf); }
    }

    public string RestoreEol(string lf) => SaveStyle switch { EolStyle.CrLf => lf.Replace("\n", "\r\n"), EolStyle.Cr => lf.Replace("\n", "\r"), _ => lf };
    public static EolStyle Detect(ReadOnlySpan<char> text)
    {
        int lf=0,cr=0,crlf=0;
        for(int i=0;i<text.Length;i++){if(text[i]=='\r'){if(i+1<text.Length&&text[i+1]=='\n'){crlf++;i++;}else cr++;}else if(text[i]=='\n')lf++;}
        int t=lf+cr+crlf; if(t==0)return EolStyle.Lf; if(crlf==t)return EolStyle.CrLf; if(cr==t)return EolStyle.Cr; if(lf==t)return EolStyle.Lf; return EolStyle.Mixed;
    }
    public string NormaliseOnLoad(string raw) => new string(NormaliseToCharArray(raw.AsSpan()));
    public static string NormaliseInsert(string text)
    {
        if (!text.Contains('\r')) return text;
        Span<char> buf = text.Length <= 4096 ? stackalloc char[text.Length] : new char[text.Length];
        int len = NormaliseInsertInto(text.AsSpan(), buf);
        return new string(buf[..len]);
    }
}
