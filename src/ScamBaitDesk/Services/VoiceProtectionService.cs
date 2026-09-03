using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScamBaitDesk.Services;

/// <summary>
/// Captures the local microphone, transforms PCM in real time, and renders it to a user-selected
/// virtual-audio playback endpoint. The matching recording endpoint is then selected manually in VoIP.
/// </summary>
public sealed class VoiceProtectionService : IDisposable
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScamBaitDesk", "voice-protection.json");
    private WaveInEvent? _microphone;
    private WasapiOut? _virtualOutput;
    private BufferedWaveProvider? _outputBuffer;
    private VoiceProtectionSettings _activeSettings = VoiceProtectionSettings.Default;
    private float _previousSample;
    public bool IsRoutingAudio => _virtualOutput?.PlaybackState == PlaybackState.Playing;
    public event EventHandler<float>? LevelChanged;

    public async Task<VoiceProtectionSettings> LoadAsync()
    {
        if (!File.Exists(_path)) return VoiceProtectionSettings.Default;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<VoiceProtectionSettings>(stream) ?? VoiceProtectionSettings.Default;
    }

    public async Task SaveAsync(VoiceProtectionSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<VirtualMicrophoneOutput> FindVirtualMicrophoneOutputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Where(device => LooksLikeVirtualAudioDevice(device.FriendlyName))
            .Select(device => new VirtualMicrophoneOutput(device.ID, device.FriendlyName))
            .OrderBy(device => device.Name)
            .ToList();
    }

    public Task StartVirtualMicrophoneAsync(VoiceProtectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.VirtualMicrophoneOutputId))
            throw new InvalidOperationException("Select a detected virtual microphone output first.");

        StopAudio();
        _activeSettings = settings;
        _previousSample = 0;
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(settings.VirtualMicrophoneOutputId);
        if (!LooksLikeVirtualAudioDevice(device.FriendlyName))
            throw new InvalidOperationException("The selected output is not a recognised virtual-audio endpoint.");

        var inputFormat = new WaveFormat(48000, 16, 1);
        _outputBuffer = new BufferedWaveProvider(inputFormat) { DiscardOnBufferOverflow = true, ReadFully = true, BufferDuration = TimeSpan.FromSeconds(2) };
        _virtualOutput = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
        _virtualOutput.Init(_outputBuffer);
        _microphone = new WaveInEvent { WaveFormat = inputFormat, BufferMilliseconds = 50 };
        _microphone.DataAvailable += OnDataAvailable;
        _virtualOutput.Play();
        _microphone.StartRecording();
        return Task.CompletedTask;
    }

    public void StartMicrophoneTest(VoiceProtectionSettings settings)
    {
        StopAudio();
        _activeSettings = settings;
        _previousSample = 0;
        _microphone = new WaveInEvent { WaveFormat = new WaveFormat(48000, 16, 1), BufferMilliseconds = 50 };
        _microphone.DataAvailable += OnDataAvailable;
        _microphone.StartRecording();
    }

    public void StopAudio()
    {
        if (_microphone is not null)
        {
            _microphone.DataAvailable -= OnDataAvailable;
            _microphone.StopRecording();
            _microphone.Dispose();
            _microphone = null;
        }
        _virtualOutput?.Stop();
        _virtualOutput?.Dispose();
        _virtualOutput = null;
        _outputBuffer = null;
        LevelChanged?.Invoke(this, 0);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var peak = 0f;
        for (var offset = 0; offset + 1 < args.BytesRecorded; offset += 2)
        {
            var processed = ApplyLocalProtection(BitConverter.ToInt16(args.Buffer, offset) / 32768f);
            var pcm = (short)(processed * short.MaxValue);
            args.Buffer[offset] = (byte)pcm;
            args.Buffer[offset + 1] = (byte)(pcm >> 8);
            peak = Math.Max(peak, Math.Abs(processed));
        }
        _outputBuffer?.AddSamples(args.Buffer, 0, args.BytesRecorded);
        LevelChanged?.Invoke(this, peak);
    }

    private static bool LooksLikeVirtualAudioDevice(string name) =>
        name.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase);

    private float ApplyLocalProtection(float sample)
    {
        var strength = _activeSettings.Strength / 100f;
        if (_activeSettings.NoiseSuppressionEnabled && Math.Abs(sample) < 0.018f + (0.035f * strength)) sample = 0;
        var filtered = _activeSettings.Profile switch
        {
            VoiceProfile.Deeper => (sample + (_previousSample * (0.45f * strength))) / (1 + (0.45f * strength)),
            VoiceProfile.Higher => sample - (_previousSample * (0.30f * strength)),
            VoiceProfile.Robotic => MathF.Round(sample * (8 + (24 * strength))) / (8 + (24 * strength)),
            _ => sample
        };
        _previousSample = sample;
        return Math.Clamp(filtered, -1f, 1f);
    }

    public void Dispose() => StopAudio();
}
