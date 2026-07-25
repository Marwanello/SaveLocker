using System.Buffers.Binary;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Minimal RIFF/WAVE reader, enough to load SteamOS's UI sounds and hand SDL exactly the format its
/// device was opened with.
///
/// Hand-rolled for the same reason <see cref="Screenshot"/>'s PCM-adjacent encoder and
/// <see cref="Art"/>'s PNG decoder are: uncompressed WAV is a trivial, fully specified container,
/// and pulling an audio library into a self-contained binary to read four short clips is a poor
/// trade. SDL's own <c>SDL_LoadWAV</c> would work, but converting its output to the device spec
/// means driving <c>SDL_AudioCVT</c> through pointer-heavy bindings — more fragile than this.
/// </summary>
static class Wav
{
    /// <summary>
    /// Decode to interleaved 16-bit little-endian PCM at the requested rate and channel count.
    /// Handles PCM and IEEE-float sources at any rate; resampling is linear, which is inaudible on
    /// a 30 ms UI blip and not worth a windowed-sinc for.
    /// </summary>
    public static byte[] DecodeToDeviceFormat(byte[] wav, int targetRate, int targetChannels)
    {
        var (samples, channels, rate) = Decode(wav);
        if (samples.Length == 0) return Array.Empty<byte>();

        // Channels first: downmix or fan out, so resampling only ever handles one layout.
        var frames = samples.Length / channels;
        var mixed = new float[frames * targetChannels];
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < targetChannels; c++)
            {
                float value;
                if (channels == targetChannels) value = samples[f * channels + c];
                else if (channels == 1) value = samples[f];                       // mono to all
                else
                {
                    // Average every source channel. Simple, and correct enough for UI stingers.
                    var sum = 0f;
                    for (int sc = 0; sc < channels; sc++) sum += samples[f * channels + sc];
                    value = sum / channels;
                }
                mixed[f * targetChannels + c] = value;
            }
        }

        if (rate != targetRate)
            mixed = Resample(mixed, targetChannels, rate, targetRate);

        var pcm = new byte[mixed.Length * sizeof(short)];
        for (int i = 0; i < mixed.Length; i++)
        {
            var clamped = Math.Clamp(mixed[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(
                pcm.AsSpan(i * sizeof(short)), (short)(clamped * short.MaxValue));
        }
        return pcm;
    }

    private static float[] Resample(float[] input, int channels, int fromRate, int toRate)
    {
        var inFrames = input.Length / channels;
        var outFrames = (int)((long)inFrames * toRate / fromRate);
        if (outFrames <= 0) return Array.Empty<float>();

        var output = new float[outFrames * channels];
        var step = (double)inFrames / outFrames;

        for (int f = 0; f < outFrames; f++)
        {
            var src = f * step;
            var i0 = (int)src;
            var i1 = Math.Min(i0 + 1, inFrames - 1);
            var frac = (float)(src - i0);

            for (int c = 0; c < channels; c++)
                output[f * channels + c] =
                    input[i0 * channels + c] * (1f - frac) + input[i1 * channels + c] * frac;
        }
        return output;
    }

    private static (float[] Samples, int Channels, int Rate) Decode(byte[] wav)
    {
        if (wav.Length < 12 ||
            wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' ||
            wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
            throw new InvalidDataException("Not a RIFF/WAVE file.");

        int channels = 0, rate = 0, bits = 0, format = 0;
        var pos = 12;

        while (pos + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(pos + 4));
            var body = pos + 8;
            if (size < 0 || body + size > wav.Length) break;

            if (id == "fmt ")
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14));

                // WAVE_FORMAT_EXTENSIBLE stores the real format in a GUID; its first two bytes are
                // the same tag, so read through to it rather than rejecting the file.
                if (format == 0xFFFE && size >= 26)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 24));
            }
            else if (id == "data")
            {
                if (channels <= 0 || rate <= 0)
                    throw new InvalidDataException("WAVE data chunk before fmt.");
                return (ReadSamples(wav.AsSpan(body, size), format, bits), channels, rate);
            }

            pos = body + size + (size & 1);   // chunks are word-aligned
        }

        throw new InvalidDataException("WAVE file has no data chunk.");
    }

    private static float[] ReadSamples(ReadOnlySpan<byte> data, int format, int bits)
    {
        const int PcmInteger = 1, PcmFloat = 3;

        if (format == PcmFloat && bits == 32)
        {
            var count = data.Length / 4;
            var result = new float[count];
            for (int i = 0; i < count; i++)
                result[i] = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]));
            return result;
        }

        if (format != PcmInteger)
            throw new NotSupportedException($"WAVE format {format} is not supported.");

        switch (bits)
        {
            case 16:
            {
                var count = data.Length / 2;
                var result = new float[count];
                for (int i = 0; i < count; i++)
                    result[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]) / 32768f;
                return result;
            }
            case 24:
            {
                var count = data.Length / 3;
                var result = new float[count];
                for (int i = 0; i < count; i++)
                {
                    var b = i * 3;
                    var value = data[b] | (data[b + 1] << 8) | ((sbyte)data[b + 2] << 16);
                    result[i] = value / 8388608f;
                }
                return result;
            }
            case 8:
            {
                // 8-bit WAV is unsigned, centred on 128 — unlike every other depth.
                var result = new float[data.Length];
                for (int i = 0; i < data.Length; i++) result[i] = (data[i] - 128) / 128f;
                return result;
            }
            default:
                throw new NotSupportedException($"{bits}-bit WAVE is not supported.");
        }
    }
}
