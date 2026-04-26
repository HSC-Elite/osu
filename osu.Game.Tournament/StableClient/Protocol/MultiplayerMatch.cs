// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace osu.Game.Tournament.StableClient.Protocol
{
    public static class SerializationHelper
    {
        public static string ReadBString(this BinaryReader reader)
        {
            byte exists = reader.ReadByte();
            if (exists != 0x0b) return string.Empty;

            int length = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }
    }

    public class MultiplayerMatch
    {
        public short Id;
        public bool InProgress;
        public byte MatchType;
        public int Mods;
        public string Name;
        public string Password;
        public string BeatmapName;
        public int BeatmapId;
        public string BeatmapChecksum;
        public byte[] SlotStatuses = new byte[16];
        public byte[] SlotTeams = new byte[16];
        public int[] SlotUserIds = new int[16];
        public int HostId;
        public byte PlayMode;
        public byte ScoringType;
        public byte TeamType;
        public bool FreeMods;
        public int[] SlotMods = new int[16];
        public int Seed;

        public MultiplayerMatch(BinaryReader reader)
        {
            Id = reader.ReadInt16();
            InProgress = reader.ReadByte() == 1;
            MatchType = reader.ReadByte();
            Mods = reader.ReadInt32();
            Name = reader.ReadBString();
            Password = reader.ReadBString();
            BeatmapName = reader.ReadBString();
            BeatmapId = reader.ReadInt32();
            BeatmapChecksum = reader.ReadBString();

            for (int i = 0; i < 16; i++) SlotStatuses[i] = reader.ReadByte();
            for (int i = 0; i < 16; i++) SlotTeams[i] = reader.ReadByte();

            for (int i = 0; i < 16; i++)
            {
                // 如果 SlotStatus 包含 HasPlayer (124 掩码用于排除 Open/Locked 等状态)
                if ((SlotStatuses[i] & 124) != 0)
                    SlotUserIds[i] = reader.ReadInt32();
            }

            HostId = reader.ReadInt32();
            PlayMode = reader.ReadByte();
            ScoringType = reader.ReadByte();
            TeamType = reader.ReadByte();
            FreeMods = reader.ReadByte() == 1;

            if (FreeMods)
            {
                for (int i = 0; i < 16; i++) SlotMods[i] = reader.ReadInt32();
            }

            Seed = reader.ReadInt32();
        }
    }
}
