// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class MemoryBasedIPCChatSyncTest
    {
        [Test]
        public void TestRebuiltChatDoesNotDuplicatePastMaxHistory()
        {
            var updater = new SimulatedChatUpdater();
            var reader = new FakeTourneyChatReader();

            for (int i = 0; i < 350; i++)
            {
                reader.AppendMessage(i);
                updater.Update(reader);
            }

            Assert.That(updater.Channel.Messages.Count, Is.EqualTo(Channel.MAX_HISTORY));

            var contents = updater.Channel.Messages.Select(m => m.Content).ToArray();

            Assert.That(contents.Distinct().Count(), Is.EqualTo(Channel.MAX_HISTORY));
            Assert.That(contents.First(), Is.EqualTo("message-50"));
            Assert.That(contents.Last(), Is.EqualTo("message-349"));

            updater.Update(reader);
            updater.Update(reader);

            var contentsAfterNoUpdate = updater.Channel.Messages.Select(m => m.Content).ToArray();

            Assert.That(updater.Channel.Messages.Count, Is.EqualTo(Channel.MAX_HISTORY));
            Assert.That(contentsAfterNoUpdate, Is.EqualTo(contents));
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
                List<Message>? updatedMessages = reader.GetTourneyChat(out int memoryMessageCount, currentMemoryMessageCount);

                applyUpdatedTourneyChat(updatedMessages);
                currentMemoryMessageCount = memoryMessageCount;
            }

            private void applyUpdatedTourneyChat(List<Message>? updatedMessages)
            {
                if (updatedMessages == null)
                    return;

                var takenChat = updatedMessages.TakeLast(Channel.MAX_HISTORY).ToArray();
                var toAdd = takenChat.Except(Channel.Messages).ToArray();

                Channel.AddNewMessages(toAdd);
            }
        }

        private class FakeTourneyChatReader
        {
            private readonly List<FakeTourneyMessage> messages = new List<FakeTourneyMessage>();
            private readonly APIUser sender = new APIUser
            {
                Username = "ref",
                Colour = string.Empty,
            };

            public void AppendMessage(int index)
            {
                messages.Add(new FakeTourneyMessage
                {
                    Timestamp = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero).AddSeconds(index),
                    Sender = sender,
                    Content = $"message-{index}",
                });
            }

            public List<Message>? GetTourneyChat(out int memoryMessageSize, int currentMessageCount)
            {
                memoryMessageSize = messages.Count;

                if (currentMessageCount == memoryMessageSize)
                    return null;

                return messages.Select(m => m.Clone()).Cast<Message>().ToList();
            }
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

            public FakeTourneyMessage Clone()
                => new FakeTourneyMessage
                {
                    Timestamp = Timestamp,
                    Sender = new APIUser
                    {
                        Username = Sender.Username,
                        Colour = Sender.Colour,
                        IsBot = Sender.IsBot,
                        Id = Sender.Id,
                    },
                    Content = Content,
                };
        }
    }
}
