// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Online.Spectator;
using osu.Game.Replays.Legacy;
using osu.Game.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tournament.StableClient;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.Tests.NonVisual.Stable
{
    [TestFixture]
    public class StableSpectatorDataSourceTest
    {
        private StableSpectatorDataSource source = null!;
        private int framesReceived;
        private int sessionsStarted;

        [SetUp]
        public void SetUp()
        {
            source = new StableSpectatorDataSource();
            framesReceived = 0;
            sessionsStarted = 0;
            source.OnNewFrames += (_, _) => framesReceived++;
            source.OnUserBeganPlaying += (_, _) => sessionsStarted++;
        }

        [Test]
        public void TestSharedWatchAndFinalUnsubscription()
        {
            source.WatchUser(42);
            source.WatchUser(42);
            receiveFrames();
            source.StopWatchingUser(42);
            receiveFrames();
            Assert.That(framesReceived, Is.EqualTo(2));
            Assert.That(sessionsStarted, Is.EqualTo(1));
            Assert.That(source.WatchedUserStates.ContainsKey(42), Is.True);
            source.StopWatchingUser(42);
            receiveFrames();
            Assert.That(framesReceived, Is.EqualTo(2));
            Assert.That(source.WatchedUserStates.ContainsKey(42), Is.False);
        }

        [Test]
        public void TestLateWatchReceivesCurrentState()
        {
            receiveFrames();
            Assert.That(framesReceived, Is.Zero);
            source.WatchUser(42);
            Assert.That(source.WatchedUserStates[42].BeatmapID, Is.EqualTo(100));
            receiveFrames();
            Assert.That(framesReceived, Is.EqualTo(1));
        }

        [Test]
        public void TestRepeatedBeatmapStartsNewSession()
        {
            source.WatchUser(42);
            receiveFrames();
            source.ReceiveAction(42, ReplayAction.Completion);
            Assert.That(source.WatchedUserStates[42].State, Is.EqualTo(SpectatedUserState.Passed));
            source.ReceiveAction(42, ReplayAction.NewSong);
            Assert.That(source.WatchedUserStates.ContainsKey(42), Is.False);
            receiveFrames();
            Assert.That(sessionsStarted, Is.EqualTo(2));
            Assert.That(source.WatchedUserStates[42].State, Is.EqualTo(SpectatedUserState.Playing));
        }

        [Test]
        public void TestFailDoesNotStopMultiplayerPlayback()
        {
            source.WatchUser(42);
            receiveFrames();
            source.ReceiveAction(42, ReplayAction.Fail);
            receiveFrames();
            Assert.That(source.WatchedUserStates[42].State, Is.EqualTo(SpectatedUserState.Playing));
            Assert.That(framesReceived, Is.EqualTo(2));
        }

        [Test]
        public void TestResetPreservesWatchRegistrations()
        {
            source.WatchUser(42);
            receiveFrames();
            source.Reset();
            Assert.That(source.WatchedUserStates.Count, Is.Zero);
            receiveFrames();
            Assert.That(sessionsStarted, Is.EqualTo(2));
            Assert.That(framesReceived, Is.EqualTo(2));
        }

        [Test]
        public void TestCompletedStateIsNotReopenedByTrailingFrames()
        {
            source.WatchUser(42);
            receiveFrames();
            source.ReceiveAction(42, ReplayAction.Completion);
            receiveFrames();
            Assert.That(source.WatchedUserStates[42].State, Is.EqualTo(SpectatedUserState.Passed));
            Assert.That(sessionsStarted, Is.EqualTo(1));
        }

        private void receiveFrames() => source.ReceiveFrames(42,
            new SpectatorState { BeatmapID = 100, RulesetID = 0, State = SpectatedUserState.Playing },
            new FrameDataBundle(new FrameHeader(new ScoreInfo(), new ScoreProcessorStatistics()) { ScoreSource = FrameScoreSource.StableRaw, TotalScore = 12345 }, Array.Empty<LegacyReplayFrame>()));
    }
}
