// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ManagedBass;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.StreamAudio;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using TSLib;
using TSLib.Audio;
using TSLib.Audio.Opus;

namespace osu.Game.Tournament.Audio
{
    internal sealed class TeamSpeakAudioReceiver : IAudioPassiveConsumer, IDisposable
    {
        private const int sample_rate = 48000;

        static TeamSpeakAudioReceiver()
        {
            NativeLibrary.SetDllImportResolver(typeof(OpusDecoder).Assembly, (name, assembly, _) =>
            {
                if (name != "libopus")
                    return IntPtr.Zero;

                if (OperatingSystem.IsWindows())
                {
                    string path = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, "libopus.dll");
                    return NativeLibrary.TryLoad(path, out IntPtr library) ? library : IntPtr.Zero;
                }

                if (OperatingSystem.IsMacOS())
                {
                    string path = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                        ? "/opt/homebrew/lib/libopus.dylib"
                        : "/usr/local/lib/libopus.dylib";

                    return NativeLibrary.TryLoad(path, out IntPtr library) ? library : IntPtr.Zero;
                }

                return OperatingSystem.IsLinux() && NativeLibrary.TryLoad("libopus.so.0", out IntPtr linuxLibrary)
                    ? linuxLibrary
                    : IntPtr.Zero;
            });
        }

        internal static void CheckDecoder()
        {
            using var decoder = OpusDecoder.Create(sample_rate, 2);
        }

        private readonly object sync = new object();
        private readonly Dictionary<ClientId, Speaker> speakers = new Dictionary<ClientId, Speaker>();
        private readonly AudioManager audio;
        private readonly AudioMixer mixer;
        private readonly Bindable<double> mixerVolume;
        private readonly VolumeEffectParameters volumeEffect;
        private readonly byte[] pcm = new byte[5760 * 2 * sizeof(short)];
        private readonly float[] output = new float[5760 * 2];
        private bool decodeErrorLogged;

        public AudioPacketReader Input { get; }
        public bool Active => true;

        public TeamSpeakAudioReceiver(AudioManager audio, AudioMixer mixer, BindableDouble volume)
        {
            this.audio = audio;
            this.mixer = mixer;
            mixerVolume = volume.GetBoundCopy();
            volumeEffect = new VolumeEffectParameters { Gain = (float)mixerVolume.Value };
            mixer.AddEffect(volumeEffect);
            mixerVolume.BindValueChanged(change =>
            {
                volumeEffect.Gain = (float)change.NewValue;
                mixer.UpdateEffect(volumeEffect);
            });
            Input = new AudioPacketReader { OutStream = this };
        }

        public void SetSpeakers(IReadOnlyList<TeamSpeakSpeaker> members)
        {
            lock (sync)
            {
                var retained = members.ToHashSet();

                foreach (var entry in speakers.Where(entry => !retained.Contains(entry.Value.Member)).ToArray())
                {
                    entry.Value.Dispose();
                    speakers.Remove(entry.Key);
                }

                foreach (var member in members)
                {
                    if (speakers.ContainsKey(member.Id))
                        continue;

                    var channel = new BassStreamAudioChannel(member.Name, sample_rate, 2, StreamAudioSampleFormat.Float32);
                    audio.AddItem(channel);
                    mixer.Add(channel);
                    channel.Play();
                    speakers.Add(member.Id, new Speaker(member, channel));
                }
            }
        }

        public void Write(Span<byte> data, Meta? meta)
        {
            if (meta == null || meta.In.Whisper || meta.Codec is not (Codec.OpusVoice or Codec.OpusMusic))
                return;

            lock (sync)
            {
                if (!speakers.TryGetValue(meta.In.Sender, out var speaker))
                    return;

                try
                {
                    speaker.Decoder ??= OpusDecoder.Create(sample_rate, 2);
                    var decoded = speaker.Decoder.Decode(data, pcm);

                    if (decoded.IsEmpty)
                        return;

                    var samples = MemoryMarshal.Cast<byte, short>(decoded);
                    float scale = speaker.Member.Gain / 32768f;
                    for (int i = 0; i < samples.Length; i++)
                        output[i] = samples[i] * scale;

                    speaker.Channel.Submit(output.AsSpan(0, samples.Length));
                    speaker.Member.MarkSpeaking();
                }
                catch (Exception exception)
                {
                    if (decodeErrorLogged)
                        return;

                    decodeErrorLogged = true;
                    Logger.Error(exception, "Failed to decode incoming TeamSpeak audio.");
                }
            }
        }

        public void Dispose()
        {
            mixerVolume.UnbindAll();
            mixer.RemoveEffect(volumeEffect);
            SetSpeakers(Array.Empty<TeamSpeakSpeaker>());
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class VolumeEffectParameters : IEffectParameter
        {
            // BASS_BFX_VOLUME uses channel 0 to apply the gain to the entire mix.
            public int Channel = 0;
            public float Gain;

            public EffectType FXType => EffectType.Volume;
        }

        private sealed class Speaker : IDisposable
        {
            public readonly TeamSpeakSpeaker Member;
            public readonly BassStreamAudioChannel Channel;
            public OpusDecoder? Decoder;

            public Speaker(TeamSpeakSpeaker member, BassStreamAudioChannel channel)
            {
                Member = member;
                Channel = channel;
            }

            public void Dispose()
            {
                Decoder?.Dispose();
                Channel.Dispose();
            }
        }
    }
}
