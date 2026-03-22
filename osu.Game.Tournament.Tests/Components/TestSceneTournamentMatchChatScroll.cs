// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
    public partial class TestSceneTournamentMatchChatScroll : TournamentTestScene
    {
        private readonly Channel channel = new Channel();
        private readonly APIUser sender = new APIUser
        {
            Id = 100,
            Username = "Referee",
            Colour = "#ffffff"
        };

        private TournamentMatchChatDisplay chatDisplay = null!;
        private int messageIndex;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("clear scene", () =>
            {
                channel.Messages.Clear();
                messageIndex = 0;
                Child = chatDisplay = new TournamentMatchChatDisplay
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new osuTK.Vector2(0.75f),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
                chatDisplay.Channel.Value = channel;
            });
        }

        [Test]
        public void TestScrollScenarios()
        {
            AddStep("seed 80 messages", () => seedMessages(80));
            AddStep("seed 320 messages", () => seedMessages(320));

            AddStep("append 1 normal message", () =>
                channel.AddNewMessages(nextMessage("append", DateTimeOffset.Now)));

            AddStep("rebuild same 300 messages", () =>
            {
                var snapshot = channel.Messages.OfType<MemoryChatMessage>().TakeLast(Channel.MAX_HISTORY).ToArray();
                channel.Messages.Clear();
                channel.AddNewMessages(snapshot.Select(cloneMessage).ToArray());
            });

            AddStep("rebuild shifted timestamps", () =>
            {
                var rebuilt = channel.Messages
                                     .OfType<MemoryChatMessage>()
                                     .TakeLast(Channel.MAX_HISTORY)
                                     .Select(m => cloneMessage(m, m.Timestamp.AddDays(1)))
                                     .ToArray();

                channel.Messages.Clear();
                channel.AddNewMessages(rebuilt);
            });

            AddStep("remove first 20 from backing store", () =>
            {
                foreach (var message in channel.Messages.Take(20).ToArray())
                    channel.Messages.Remove(message);
            });

            AddStep("remove first 20 then add 20", () =>
            {
                foreach (var message in channel.Messages.Take(20).ToArray())
                    channel.Messages.Remove(message);

                channel.AddNewMessages(Enumerable.Range(0, 20)
                                                 .Select(_ => nextMessage("direct-remove-add", DateTimeOffset.Now))
                                                 .ToArray());
            });

            AddStep("clear and add last 300 clone batch", () =>
            {
                var rebuilt = channel.Messages
                                     .OfType<MemoryChatMessage>()
                                     .TakeLast(Channel.MAX_HISTORY)
                                     .Select(cloneMessage)
                                     .ToArray();

                channel.Messages.Clear();
                channel.AddNewMessages(rebuilt);
            });
        }

        private void seedMessages(int count)
        {
            channel.Messages.Clear();

            var startTime = DateTimeOffset.Now.Date.AddHours(12);
            channel.AddNewMessages(Enumerable.Range(0, count)
                                             .Select(i => createMessage($"seed-{i}", startTime.AddSeconds(i)))
                                             .ToArray());

            messageIndex = count;
        }

        private MemoryChatMessage createMessage(string content, DateTimeOffset timestamp)
            => new MemoryChatMessage
            {
                Timestamp = timestamp,
                Sender = sender,
                Content = content
            };

        private MemoryChatMessage nextMessage(string prefix, DateTimeOffset startTime)
            => createMessage($"{prefix}-{messageIndex}", startTime.AddSeconds(messageIndex++));

        private static MemoryChatMessage cloneMessage(MemoryChatMessage original)
            => cloneMessage(original, original.Timestamp);

        private static MemoryChatMessage cloneMessage(MemoryChatMessage original, DateTimeOffset timestamp)
            => new MemoryChatMessage
            {
                Timestamp = timestamp,
                Sender = new APIUser
                {
                    Id = original.Sender.Id,
                    Username = original.Sender.Username,
                    Colour = original.Sender.Colour,
                    IsBot = original.Sender.IsBot,
                },
                Content = original.Content
            };

        private partial class MemoryChatMessage : Message
        {
            public override bool Equals(Message? other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Timestamp == other.Timestamp
                       && Sender?.Username == other.Sender?.Username
                       && Content == other.Content;
            }

            public override int GetHashCode() => HashCode.Combine(Timestamp, Sender?.Username, Content);
        }
    }
}
