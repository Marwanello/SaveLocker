namespace SaveLocker.Shared;

/// <summary>
/// Reading an HTTP body under a hard byte cap. A body from the network is unbounded until proven
/// otherwise, and every reader of one in this codebase — the agent's art proxy, the server's image
/// fetch, the manifest download — must refuse a body that keeps growing rather than buffer it whole.
/// The cap is counted as bytes arrive, so a chunked body that declares no length is stopped too.
/// </summary>
public static class HttpBodies
{
    /// <summary>The body as bytes, or null when it exceeds <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadAtMostAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Copy the body into <paramref name="destination"/>, throwing
    /// <see cref="InvalidDataException"/> (the caller keeps whatever it already had) once more than
    /// <paramref name="maxBytes"/> have arrived. Returns the bytes written.</summary>
    public static async Task<long> CopyAtMostAsync(
        Stream body, Stream destination, long maxBytes, string source, CancellationToken ct)
    {
        var chunk = new byte[81920];
        long written = 0;
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            written += read;
            if (written > maxBytes)
                throw new InvalidDataException($"The body from {source} is over the {maxBytes}-byte cap.");
            await destination.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return written;
    }
}
