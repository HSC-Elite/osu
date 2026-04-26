// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;

namespace osu.Game.Tournament.StableClient.Protocol
{
    public class bReplayFrameBundle
    {
        public int Extra;
        public List<bReplayFrame> Frames = new List<bReplayFrame>();
        public byte Action;
        public bScoreFrame ScoreFrame;
        public ushort Sequence;

        public bReplayFrameBundle(BinaryReader sr)
        {
            Extra = sr.ReadInt32();
            ushort frameCount = sr.ReadUInt16();

            for (int i = 0; i < frameCount; i++)
            {
                Frames.Add(new bReplayFrame(sr));
            }

            Action = sr.ReadByte();
            ScoreFrame = new bScoreFrame(sr);
            Sequence = sr.ReadUInt16();
        }
    }

    public class bReplayFrame
    {
        public byte ButtonState;
        public byte Constant;
        public float MouseX;
        public float MouseY;
        public int Time;

        public bReplayFrame(BinaryReader sr)
        {
            ButtonState = sr.ReadByte();
            Constant = sr.ReadByte();
            MouseX = sr.ReadSingle();
            MouseY = sr.ReadSingle();
            Time = sr.ReadInt32();
        }
    }

    public class bScoreFrame
    {
        public int Time;
        public byte Id;
        public ushort Count300;
        public ushort Count100;
        public ushort Count50;
        public ushort CountGeki;
        public ushort CountKatu;
        public ushort CountMiss;
        public int TotalScore;
        public ushort MaxCombo;
        public ushort CurrentCombo;
        public bool Perfect;
        public byte Hp;
        public byte Tag;
        public bool ScoreV2;

        public bScoreFrame(BinaryReader sr)
        {
            Time = sr.ReadInt32();
            Id = sr.ReadByte();
            Count300 = sr.ReadUInt16();
            Count100 = sr.ReadUInt16();
            Count50 = sr.ReadUInt16();
            CountGeki = sr.ReadUInt16();
            CountKatu = sr.ReadUInt16();
            CountMiss = sr.ReadUInt16();
            TotalScore = sr.ReadInt32();
            MaxCombo = sr.ReadUInt16();
            CurrentCombo = sr.ReadUInt16();
            Perfect = sr.ReadBoolean();
            Hp = sr.ReadByte();
            Tag = sr.ReadByte();
            ScoreV2 = sr.ReadBoolean();

            if (ScoreV2)
            {
                sr.ReadDouble(); // Combo portion
                sr.ReadDouble(); // Bonus portion
            }
        }
    }
}
