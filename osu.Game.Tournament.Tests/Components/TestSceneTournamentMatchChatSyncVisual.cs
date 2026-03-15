// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.Components
{
    [TestFixture]
    public partial class TestSceneTournamentMatchChatSyncVisual : TournamentTestScene
    {
        private FakeTourneyChatReader reader = null!;
        private SimulatedChatUpdater updater = null!;
        private TournamentMatchChatDisplay display = null!;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("reset scene", () =>
            {
                reader = new FakeTourneyChatReader();
                updater = new SimulatedChatUpdater();

                Child = display = new TournamentMatchChatDisplay
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new osuTK.Vector2(0.75f),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };

                display.Channel.Value = updater.Channel;
            });
        }

        [Test]
        public void TestVisualSyncBehaviour()
        {
            AddStep("append and sync 50", () => appendAndSync(50));
            AddStep("append and sync to 299", () => appendAndSyncTo(299));
            AddStep("append and sync 300th", () => appendAndSync(1));
            AddStep("append and sync 301st", () => appendAndSync(1));
            AddStep("append and sync to 350", () => appendAndSyncTo(350));

            AddStep("poll no update once", () => updater.Update(reader));
            AddStep("poll no update x10", () =>
            {
                for (int i = 0; i < 10; i++)
                    updater.Update(reader);
            });

            AddStep("rebuild with same identities", () => reader.RebuildMessages(_ => _));
            AddStep("sync rebuilt same identities", () => updater.Update(reader));

            AddStep("rebuild with shifted timestamps", () => reader.RebuildMessages(m => m with
            {
                Timestamp = m.Timestamp.AddDays(1)
            }));
            AddStep("sync shifted timestamps", () => updater.Update(reader));
        }

        private void appendAndSync(int count)
        {
            for (int i = 0; i < count; i++)
                reader.AppendMessage();

            updater.Update(reader);
        }

        private void appendAndSyncTo(int count)
        {
            while (reader.Count < count)
                reader.AppendMessage();

            updater.Update(reader);
        }

        private class SimulatedChatUpdater
        {
            private int currentMemoryMessageCount;

            public Channel Channel { get; } = new Channel
            {
                Name = "mp",
                Type = ChannelType.Private,
            };

            public void Update(FakeTourneyChatReader reader)
            {
                List<Message>? updatedMessages = reader.GetTourneyChat(out int memoryMessageCount, Channel.Messages.Count);

                if (updatedMessages != null)
                {
                    var takenChat = updatedMessages.TakeLast(Channel.MAX_HISTORY).ToArray();

                    var toRemove = Channel.Messages.Except(takenChat).ToArray();
                    foreach (var item in toRemove)
                        Channel.Messages.Remove(item);

                    var toAdd = takenChat.Except(Channel.Messages).ToArray();

                    Channel.AddNewMessages(toAdd);
                }

                currentMemoryMessageCount = memoryMessageCount;
            }
        }

        private class FakeTourneyChatReader
        {
            private readonly APIUser sender = new APIUser
            {
                Id = 100,
                Username = "ref",
                Colour = string.Empty,
            };

            private readonly List<MessageData> messages = new List<MessageData>();

            public int Count => messages.Count;

            public void AppendMessage()
            {
                int index = messages.Count;

                messages.Add(new MessageData(
                    new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero).AddSeconds(index),
                    $"message-{index}"));
            }

            public void RebuildMessages(Func<MessageData, MessageData> transform)
            {
                for (int i = 0; i < messages.Count; i++)
                    messages[i] = transform(messages[i]);
            }

            public List<Message>? GetTourneyChat(out int memoryMessageSize, int currentMessageCount)
            {
                memoryMessageSize = messages.Count;

                if (currentMessageCount == memoryMessageSize)
                    return null;

                return messages.Select(m => new FakeTourneyMessage
                {
                    Timestamp = m.Timestamp,
                    Sender = new APIUser
                    {
                        Id = sender.Id,
                        Username = sender.Username,
                        Colour = sender.Colour,
                    },
                    Content = m.Content,
                }).Cast<Message>().ToList();
            }

            public record struct MessageData(DateTimeOffset Timestamp, string Content);
        }

        private class FakeTourneyMessage : Message
        {
            public override bool Equals(Message? other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Timestamp == other.Timestamp
                       && Sender.Username == other.Sender.Username
                       && Content == other.Content;
            }

            public override int GetHashCode() => HashCode.Combine(Timestamp, Sender.Username, Content);
        }
    }
}
