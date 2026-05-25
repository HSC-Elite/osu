// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.Tests.NonVisual.Stable
{
    [TestFixture]
    public class StableProtocolTest
    {
        [TestCase("")]
        [TestCase("hello")]
        [TestCase("\u65e5\u672c\u8a9e")]
        public void TestReadBString(string value)
        {
            using var reader = createReader(writeBString(value));

            Assert.That(reader.ReadBString(), Is.EqualTo(value));
            Assert.That(reader.BaseStream.Position, Is.EqualTo(reader.BaseStream.Length));
        }

        [Test]
        public void TestReadLongBString()
        {
            string value = new string('a', 130);

            using var reader = createReader(writeBString(value));

            Assert.That(reader.ReadBString(), Is.EqualTo(value));
            Assert.That(reader.BaseStream.Position, Is.EqualTo(reader.BaseStream.Length));
        }

        [TestCase("#spectator", false)]
        [TestCase("peppy", true)]
        [TestCase("", true)]
        public void TestMessageParsing(string target, bool expectedPrivate)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(writeBString("BanchoBot"));
                writer.Write(writeBString("ready"));
                writer.Write(writeBString(target));
                writer.Write(3);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            var message = new bMessage(reader);

            Assert.That(message.SendingClient, Is.EqualTo("BanchoBot"));
            Assert.That(message.Message, Is.EqualTo("ready"));
            Assert.That(message.Target, Is.EqualTo(target));
            Assert.That(message.SenderId, Is.EqualTo(3));
            Assert.That(message.IsPrivate, Is.EqualTo(expectedPrivate));
        }

        [Test]
        public void TestMultiplayerMatchParsing()
        {
            byte[] slotStatuses = new byte[16];
            byte[] slotTeams = new byte[16];
            int[] slotMods = new int[16];

            slotStatuses[0] = 4;
            slotStatuses[2] = 124;
            slotStatuses[10] = 16;

            slotTeams[0] = 1;
            slotTeams[2] = 2;
            slotTeams[10] = 1;

            slotMods[0] = 8;
            slotMods[2] = 64;
            slotMods[10] = 576;

            using var reader = createReader(writeMatch(
                id: 42,
                inProgress: true,
                matchType: 0,
                mods: 72,
                name: "fixture room",
                password: "secret",
                beatmapName: "artist - title",
                beatmapId: 1234,
                beatmapChecksum: "0123456789abcdef0123456789abcdef",
                slotStatuses: slotStatuses,
                slotTeams: slotTeams,
                slotUserIdsBySlot: new[] { (0, 1001), (2, 1002), (10, 1010) },
                hostId: 1001,
                playMode: 3,
                scoringType: 3,
                teamType: 2,
                freeMods: true,
                slotMods: slotMods,
                seed: 987654321));

            var match = new MultiplayerMatch(reader);

            Assert.That(match.Id, Is.EqualTo(42));
            Assert.That(match.InProgress, Is.True);
            Assert.That(match.MatchType, Is.Zero);
            Assert.That(match.Mods, Is.EqualTo(72));
            Assert.That(match.Name, Is.EqualTo("fixture room"));
            Assert.That(match.Password, Is.EqualTo("secret"));
            Assert.That(match.BeatmapName, Is.EqualTo("artist - title"));
            Assert.That(match.BeatmapId, Is.EqualTo(1234));
            Assert.That(match.BeatmapChecksum, Is.EqualTo("0123456789abcdef0123456789abcdef"));

            Assert.That(match.SlotStatuses, Is.EqualTo(slotStatuses));
            Assert.That(match.SlotTeams, Is.EqualTo(slotTeams));
            Assert.That(match.SlotUserIds[0], Is.EqualTo(1001));
            Assert.That(match.SlotUserIds[1], Is.Zero);
            Assert.That(match.SlotUserIds[2], Is.EqualTo(1002));
            Assert.That(match.SlotUserIds[10], Is.EqualTo(1010));

            Assert.That(match.HostId, Is.EqualTo(1001));
            Assert.That(match.PlayMode, Is.EqualTo(3));
            Assert.That(match.ScoringType, Is.EqualTo(3));
            Assert.That(match.TeamType, Is.EqualTo(2));
            Assert.That(match.FreeMods, Is.True);
            Assert.That(match.SlotMods, Is.EqualTo(slotMods));
            Assert.That(match.Seed, Is.EqualTo(987654321));
            Assert.That(reader.BaseStream.Position, Is.EqualTo(reader.BaseStream.Length));
        }

        [Test]
        public void TestReplayFrameBundleParsing()
        {
            using var reader = createReader(writeReplayFrameBundle(
                extra: 777,
                frames: new[]
                {
                    new ReplayFrameSpec(1, 0, 12.5f, 24.5f, 1000),
                    new ReplayFrameSpec(5, 0, 14.5f, 27.5f, 1016),
                },
                action: ReplayAction.Skip,
                scoreFrame: new ScoreFrameSpec(
                    Time: 1016,
                    Id: 2,
                    Count300: 10,
                    Count100: 3,
                    Count50: 1,
                    CountGeki: 4,
                    CountKatu: 2,
                    CountMiss: 1,
                    TotalScore: 123456,
                    MaxCombo: 32,
                    CurrentCombo: 16,
                    Perfect: false,
                    Hp: 200,
                    Tag: 7,
                    ScoreV2: true,
                    ComboPortion: 0.75,
                    BonusPortion: 0.25),
                sequence: 55));

            var bundle = new bReplayFrameBundle(reader);

            Assert.That(bundle.Extra, Is.EqualTo(777));
            Assert.That(bundle.Action, Is.EqualTo(ReplayAction.Skip));
            Assert.That(bundle.Sequence, Is.EqualTo(55));
            Assert.That(bundle.Frames, Has.Count.EqualTo(2));
            Assert.That(bundle.Frames[0].ButtonState, Is.EqualTo(1));
            Assert.That(bundle.Frames[0].Constant, Is.Zero);
            Assert.That(bundle.Frames[0].MouseX, Is.EqualTo(12.5f));
            Assert.That(bundle.Frames[0].MouseY, Is.EqualTo(24.5f));
            Assert.That(bundle.Frames[0].Time, Is.EqualTo(1000));
            Assert.That(bundle.Frames[1].ButtonState, Is.EqualTo(5));
            Assert.That(bundle.Frames[1].Time, Is.EqualTo(1016));

            Assert.That(bundle.ScoreFrame.Time, Is.EqualTo(1016));
            Assert.That(bundle.ScoreFrame.Id, Is.EqualTo(2));
            Assert.That(bundle.ScoreFrame.Count300, Is.EqualTo(10));
            Assert.That(bundle.ScoreFrame.Count100, Is.EqualTo(3));
            Assert.That(bundle.ScoreFrame.Count50, Is.EqualTo(1));
            Assert.That(bundle.ScoreFrame.CountGeki, Is.EqualTo(4));
            Assert.That(bundle.ScoreFrame.CountKatu, Is.EqualTo(2));
            Assert.That(bundle.ScoreFrame.CountMiss, Is.EqualTo(1));
            Assert.That(bundle.ScoreFrame.TotalScore, Is.EqualTo(123456));
            Assert.That(bundle.ScoreFrame.MaxCombo, Is.EqualTo(32));
            Assert.That(bundle.ScoreFrame.CurrentCombo, Is.EqualTo(16));
            Assert.That(bundle.ScoreFrame.Perfect, Is.False);
            Assert.That(bundle.ScoreFrame.Hp, Is.EqualTo(200));
            Assert.That(bundle.ScoreFrame.Pass, Is.True);
            Assert.That(bundle.ScoreFrame.Tag, Is.EqualTo(7));
            Assert.That(bundle.ScoreFrame.ScoreV2, Is.True);
            Assert.That(bundle.ScoreFrame.ComboPortion, Is.EqualTo(0.75));
            Assert.That(bundle.ScoreFrame.BonusPortion, Is.EqualTo(0.25));
            Assert.That(reader.BaseStream.Position, Is.EqualTo(reader.BaseStream.Length));
        }

        [Test]
        public void TestScoreFrameHp254MeansFailed()
        {
            using var reader = createReader(writeReplayFrameBundle(
                extra: 0,
                frames: Enumerable.Empty<ReplayFrameSpec>(),
                action: ReplayAction.Fail,
                scoreFrame: new ScoreFrameSpec(
                    Time: 5000,
                    Id: 0,
                    Count300: 20,
                    Count100: 0,
                    Count50: 0,
                    CountGeki: 0,
                    CountKatu: 0,
                    CountMiss: 1,
                    TotalScore: 654321,
                    MaxCombo: 50,
                    CurrentCombo: 0,
                    Perfect: false,
                    Hp: 254,
                    Tag: 0,
                    ScoreV2: false),
                sequence: 10));

            var bundle = new bReplayFrameBundle(reader);

            Assert.That(bundle.Frames, Is.Empty);
            Assert.That(bundle.Action, Is.EqualTo(ReplayAction.Fail));
            Assert.That(bundle.ScoreFrame.Hp, Is.Zero);
            Assert.That(bundle.ScoreFrame.Pass, Is.False);
            Assert.That(bundle.ScoreFrame.ScoreV2, Is.False);
            Assert.That(bundle.ScoreFrame.ComboPortion, Is.Zero);
            Assert.That(bundle.ScoreFrame.BonusPortion, Is.Zero);
        }

        [Test]
        public void TestReplayActionValuesMatchStableOrder()
        {
            Assert.That((byte)ReplayAction.Standard, Is.EqualTo(0));
            Assert.That((byte)ReplayAction.NewSong, Is.EqualTo(1));
            Assert.That((byte)ReplayAction.Skip, Is.EqualTo(2));
            Assert.That((byte)ReplayAction.Completion, Is.EqualTo(3));
            Assert.That((byte)ReplayAction.Fail, Is.EqualTo(4));
            Assert.That((byte)ReplayAction.Pause, Is.EqualTo(5));
            Assert.That((byte)ReplayAction.Unpause, Is.EqualTo(6));
            Assert.That((byte)ReplayAction.SongSelect, Is.EqualTo(7));
            Assert.That((byte)ReplayAction.WatchingOther, Is.EqualTo(8));
        }

        private static BinaryReader createReader(byte[] data) => new BinaryReader(new MemoryStream(data), Encoding.UTF8);

        private static byte[] writeBString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new byte[] { 0 };

            byte[] encoded = Encoding.UTF8.GetBytes(value);

            using var stream = new MemoryStream();
            stream.WriteByte(0x0b);
            writeUleb128(stream, encoded.Length);
            stream.Write(encoded, 0, encoded.Length);

            return stream.ToArray();
        }

        private static void writeUleb128(Stream stream, int value)
        {
            do
            {
                byte b = (byte)(value & 0x7f);
                value >>= 7;

                if (value != 0)
                    b |= 0x80;

                stream.WriteByte(b);
            } while (value != 0);
        }

        private static byte[] writeMatch(
            short id,
            bool inProgress,
            byte matchType,
            int mods,
            string name,
            string password,
            string beatmapName,
            int beatmapId,
            string beatmapChecksum,
            byte[] slotStatuses,
            byte[] slotTeams,
            (int Slot, int UserId)[] slotUserIdsBySlot,
            int hostId,
            byte playMode,
            byte scoringType,
            byte teamType,
            bool freeMods,
            int[] slotMods,
            int seed)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(id);
                writer.Write((byte)(inProgress ? 1 : 0));
                writer.Write(matchType);
                writer.Write(mods);
                writer.Write(writeBString(name));
                writer.Write(writeBString(password));
                writer.Write(writeBString(beatmapName));
                writer.Write(beatmapId);
                writer.Write(writeBString(beatmapChecksum));

                writer.Write(slotStatuses);
                writer.Write(slotTeams);

                for (int i = 0; i < 16; i++)
                {
                    if ((slotStatuses[i] & 124) == 0)
                        continue;

                    writer.Write(slotUserIdsBySlot.Single(s => s.Slot == i).UserId);
                }

                writer.Write(hostId);
                writer.Write(playMode);
                writer.Write(scoringType);
                writer.Write(teamType);
                writer.Write((byte)(freeMods ? 1 : 0));

                if (freeMods)
                {
                    for (int i = 0; i < 16; i++)
                        writer.Write(slotMods[i]);
                }

                writer.Write(seed);
            }

            return stream.ToArray();
        }

        private static byte[] writeReplayFrameBundle(int extra, IEnumerable<ReplayFrameSpec> frames, ReplayAction action, ScoreFrameSpec scoreFrame, ushort sequence)
        {
            ReplayFrameSpec[] frameArray = frames.ToArray();

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(extra);
                writer.Write((ushort)frameArray.Length);

                foreach (ReplayFrameSpec frame in frameArray)
                    writeReplayFrame(writer, frame);

                writer.Write((byte)action);
                writeScoreFrame(writer, scoreFrame);
                writer.Write(sequence);
            }

            return stream.ToArray();
        }

        private static void writeReplayFrame(BinaryWriter writer, ReplayFrameSpec frame)
        {
            writer.Write(frame.ButtonState);
            writer.Write(frame.Constant);
            writer.Write(frame.MouseX);
            writer.Write(frame.MouseY);
            writer.Write(frame.Time);
        }

        private static void writeScoreFrame(BinaryWriter writer, ScoreFrameSpec frame)
        {
            writer.Write(frame.Time);
            writer.Write(frame.Id);
            writer.Write(frame.Count300);
            writer.Write(frame.Count100);
            writer.Write(frame.Count50);
            writer.Write(frame.CountGeki);
            writer.Write(frame.CountKatu);
            writer.Write(frame.CountMiss);
            writer.Write(frame.TotalScore);
            writer.Write(frame.MaxCombo);
            writer.Write(frame.CurrentCombo);
            writer.Write(frame.Perfect);
            writer.Write(frame.Hp);
            writer.Write(frame.Tag);
            writer.Write(frame.ScoreV2);

            if (frame.ScoreV2)
            {
                writer.Write(frame.ComboPortion);
                writer.Write(frame.BonusPortion);
            }
        }

        private readonly record struct ReplayFrameSpec(byte ButtonState, byte Constant, float MouseX, float MouseY, int Time);

        private readonly record struct ScoreFrameSpec(
            int Time,
            byte Id,
            ushort Count300,
            ushort Count100,
            ushort Count50,
            ushort CountGeki,
            ushort CountKatu,
            ushort CountMiss,
            int TotalScore,
            ushort MaxCombo,
            ushort CurrentCombo,
            bool Perfect,
            byte Hp,
            byte Tag,
            bool ScoreV2,
            double ComboPortion = 0,
            double BonusPortion = 0);
    }
}
