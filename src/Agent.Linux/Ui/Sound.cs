using System.Buffers.Binary;
using System.Diagnostics;
using Silk.NET.SDL;
using SdlApi = Silk.NET.SDL.Sdl;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Interface sounds for the Game Mode UI. A console UI that responds silently feels inert, and on a
/// Deck the user is often not looking closely at what focus is doing.
///
/// Two sources, in order:
///
/// 1. <b>SteamOS's own Game Mode sounds</b>, read from the user's existing Steam install. Reading
///    files already on the machine is not redistribution — these are Valve's assets and are
///    deliberately NOT bundled. When they are present the UI sounds exactly like the rest of Game
///    Mode, which is the whole point.
/// 2. <b>Synthesised fallback</b> for a Linux desktop, a Deck whose Steam layout has moved, or a
///    dev box. Generated from oscillators at startup, so it adds zero bytes to the tarball and
///    raises no licensing question at all.
///
/// Playback goes through SDL's audio device — already loaded for windowing and input, so this costs
/// no new dependency. Everything is best-effort: a machine with no audio device, no Steam, or no
/// working SDL audio backend degrades to silence rather than failing to start the UI.
/// </summary>
static class Sound
{
    public enum Cue { Navigate, Activate, Back, Toggle }

    private const int Rate = 48000;
    private const int Channels = 2;
    private const float Volume = 0.55f;

    /// <summary>
    /// Sounds are ignored briefly after start-up. ImGui settles initial nav focus over the first few
    /// frames, and without this the UI chirps at the user before they have touched anything.
    /// </summary>
    private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(350);

    private static uint _device;
    private static readonly Dictionary<Cue, byte[]> _clips = new();
    private static readonly Stopwatch _since = new();
    private static bool _initialised;

    public static bool Available => _device != 0;
    public static bool Muted { get; set; }

    /// <summary>Where the clips came from, for the Settings screen to be honest about.</summary>
    public static string Source { get; private set; } = "none";

    /// <summary>Why audio is unavailable, if it is. Shown in Settings so the user is not left guessing.</summary>
    public static string? Unavailable { get; private set; }

    private static unsafe string SdlError(SdlApi sdl)
    {
        try
        {
            var raw = sdl.GetErrorS();
            return string.IsNullOrWhiteSpace(raw) ? "no detail" : raw;
        }
        catch { return "no detail"; }
    }

    public static void Init(bool muted)
    {
        Muted = muted;
        if (_initialised) return;
        _initialised = true;

        try
        {
            var sdl = SdlApi.GetApi();

            // The window only initialises video; audio is a separate subsystem.
            const uint SdlInitAudio = 0x00000010;
            if (sdl.InitSubSystem(SdlInitAudio) != 0)
            {
                // Report SDL's reason. The usual cause on a stripped-down Linux box is that neither
                // libpulse nor libasound is installed, so SDL has no backend to load — which is
                // indistinguishable from "no sound card" unless the error is printed.
                Console.Error.WriteLine($"Interface sounds off: SDL audio unavailable ({SdlError(sdl)}).");
                Unavailable = "SDL audio backend unavailable";
                return;
            }

            if (!OpenDevice(sdl)) return;

            LoadClips();
            _since.Restart();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Interface sounds off: " + ex.Message);
        }
    }

    private static unsafe bool OpenDevice(SdlApi sdl)
    {
        // AUDIO_S16LSB. SDL's format constants are a bitfield, not an enum, and are not surfaced as
        // named members by the binding.
        const ushort AudioS16Lsb = 0x8010;

        var desired = new AudioSpec
        {
            Freq = Rate,
            Format = AudioS16Lsb,
            Channels = Channels,
            Samples = 1024,
        };

        AudioSpec obtained;
        _device = sdl.OpenAudioDevice((byte*)null, 0, &desired, &obtained, 0);
        if (_device == 0)
        {
            Console.Error.WriteLine($"Interface sounds off: no audio device ({SdlError(sdl)}).");
            Unavailable = "no audio output device";
            return false;
        }

        sdl.PauseAudioDevice(_device, 0);   // 0 = start playing queued audio
        return true;
    }

    public static void Play(Cue cue)
    {
        if (Muted || _device == 0) return;
        if (_since.Elapsed < ArmDelay) return;
        if (!_clips.TryGetValue(cue, out var pcm) || pcm.Length == 0) return;

        try
        {
            var sdl = SdlApi.GetApi();
            // Drop whatever is still queued rather than appending. SDL_QueueAudio concatenates, so
            // holding a direction on the D-pad would otherwise build a backlog and the sounds would
            // drift further and further behind the cursor. For UI blips, newest-wins is correct.
            sdl.ClearQueuedAudio(_device);
            unsafe
            {
                fixed (byte* p = pcm) sdl.QueueAudio(_device, p, (uint)pcm.Length);
            }
        }
        catch { /* audio is never worth taking the UI down for */ }
    }

    public static void Shutdown()
    {
        if (_device == 0) return;
        try { SdlApi.GetApi().CloseAudioDevice(_device); } catch { /* best-effort */ }
        _device = 0;
    }

    // ── Clip sourcing ────────────────────────────────────────────────────────────────────────

    private static void LoadClips()
    {
        var dir = FindSteamSoundDir();
        if (dir is not null)
        {
            // Names as SteamOS ships them. If Valve renames these, the file simply is not found and
            // that cue falls through to the synthesised one — never a crash, never silence overall.
            var map = new (Cue Cue, string File)[]
            {
                (Cue.Navigate, "deck_ui_navigation.wav"),
                (Cue.Activate, "deck_ui_default_activation.wav"),
                (Cue.Back, "deck_ui_hide_modal.wav"),
                (Cue.Toggle, "deck_ui_switch_toggle_on.wav"),
            };

            foreach (var (cue, file) in map)
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) continue;
                try
                {
                    var pcm = Wav.DecodeToDeviceFormat(File.ReadAllBytes(path), Rate, Channels);
                    if (pcm.Length > 0) _clips[cue] = pcm;
                }
                catch { /* fall through to the synthesised cue */ }
            }
        }

        var fromSteam = _clips.Count;
        foreach (Cue cue in Enum.GetValues<Cue>())
            if (!_clips.ContainsKey(cue))
                _clips[cue] = Synthesise(cue);

        Source = fromSteam switch
        {
            0 => "synthesised",
            4 => "SteamOS",
            _ => $"SteamOS ({fromSteam}/4) + synthesised",
        };
    }

    /// <summary>
    /// SteamOS keeps the Game Mode UI sounds inside the Steam client's own web UI assets. Every
    /// known Steam root is probed, so a Flatpak Steam or a non-default library layout still works.
    /// </summary>
    private static string? FindSteamSoundDir()
    {
        foreach (var root in SteamRoots.Find())
        {
            var candidate = Path.Combine(root, "steamui", "sounds");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ── Synthesis ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Short oscillator blips with a soft attack and exponential decay. The attack matters: a tone
    /// that starts at full amplitude produces an audible click at the discontinuity, which on small
    /// speakers reads as a fault rather than a UI sound.
    /// </summary>
    private static byte[] Synthesise(Cue cue) => cue switch
    {
        Cue.Navigate => Tone(1180f, 1180f, 0.028f, 0.35f),
        Cue.Activate => Tone(760f, 1240f, 0.070f, 0.55f),
        Cue.Back => Tone(720f, 430f, 0.075f, 0.45f),
        Cue.Toggle => Tone(980f, 980f, 0.040f, 0.45f),
        _ => Array.Empty<byte>(),
    };

    private static byte[] Tone(float startHz, float endHz, float seconds, float gain)
    {
        var frames = (int)(Rate * seconds);
        var pcm = new byte[frames * Channels * sizeof(short)];
        var phase = 0.0;

        for (int i = 0; i < frames; i++)
        {
            var t = i / (float)frames;
            var hz = startHz + (endHz - startHz) * t;
            phase += 2 * Math.PI * hz / Rate;

            // 3 ms raised-cosine attack, exponential decay after it.
            var attackFrames = Rate * 0.003f;
            var attack = i < attackFrames ? 0.5f * (1f - MathF.Cos(MathF.PI * i / attackFrames)) : 1f;
            var envelope = attack * MathF.Exp(-4.5f * t);

            var sample = (short)(Math.Sin(phase) * envelope * gain * Volume * short.MaxValue);
            for (int c = 0; c < Channels; c++)
            {
                var offset = (i * Channels + c) * sizeof(short);
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset), sample);
            }
        }
        return pcm;
    }
}
