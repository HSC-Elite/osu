// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Textures;
using TSLib;

namespace osu.Game.Tournament.Audio
{
    internal sealed class TeamSpeakSpeaker
    {
        private long speakingUntil;
        private volatile float gain = 1;

        public ClientId Id { get; }
        public string Name { get; }
        public Uid? Uid { get; }
        public BindableDouble Volume { get; } = new BindableDouble(1) { MinValue = 0, MaxValue = 2, Precision = 0.01 };
        public float Gain => gain;
        public Texture? Avatar { get; set; }
        public bool IsSpeaking => Environment.TickCount64 < Interlocked.Read(ref speakingUntil);

        public TeamSpeakSpeaker(ClientId id, string name, Uid? uid)
        {
            Id = id;
            Name = name;
            Uid = uid;
            Volume.BindValueChanged(change => gain = (float)change.NewValue);
        }

        public void MarkSpeaking() => Interlocked.Exchange(ref speakingUntil, Environment.TickCount64 + 300);
    }
}
