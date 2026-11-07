// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.OnlinePlay
{
    // NOTE: This test scene never calls ProcessFrame on clocks.
    // The current tests are fine without this as they are testing very static scenarios, but it's worth knowing
    // if adding further tests to this class.
    [HeadlessTest]
    public partial class TestSceneCatchUpSyncManager : OsuTestScene
    {
        private GameplayClockContainer master = null!;
        private SpectatorSyncManager syncManager = null!;

        private Dictionary<SpectatorPlayerClock, int> clocksById = null!;
        private SpectatorPlayerClock player1 = null!;
        private SpectatorPlayerClock player2 = null!;
        private double available1;
        private double available2;

        [SetUp]
        public void Setup()
        {
            syncManager = new SpectatorSyncManager(master = new GameplayClockContainer(new TestManualClock(), false, false));
            syncManager.ReadyToStart = master.Start;
            available1 = available2 = double.PositiveInfinity;
            player1 = syncManager.CreateManagedClock(() => available1);
            player2 = syncManager.CreateManagedClock(() => available2);

            clocksById = new Dictionary<SpectatorPlayerClock, int>
            {
                { player1, 1 },
                { player2, 2 }
            };

            Schedule(() =>
            {
                Children = new Drawable[]
                {
                    syncManager,
                    master
                };
            });
        }

        [Test]
        public void TestPlayerClocksStartWhenAllHaveFrames()
        {
            setWaiting(() => player1, false);
            assertPlayerClockState(() => player1, false);
            assertPlayerClockState(() => player2, false);

            setWaiting(() => player2, false);
            assertPlayerClockState(() => player1, true);
            assertPlayerClockState(() => player2, true);
        }

        [Test]
        public void TestReadyPlayersStartWhenReadyForMaximumDelayTime()
        {
            setWaiting(() => player1, false);
            AddWaitStep($"wait {SpectatorSyncManager.MAXIMUM_START_DELAY} milliseconds", (int)Math.Ceiling(SpectatorSyncManager.MAXIMUM_START_DELAY / TimePerAction));
            assertPlayerClockState(() => player1, true);
            assertPlayerClockState(() => player2, false);
        }

        [Test]
        public void TestPlayerClockDoesNotCatchUpWhenSlightlyOutOfSync()
        {
            setAllWaiting(false);

            setMasterTime(SpectatorSyncManager.SYNC_TARGET + 1);
            assertCatchingUp(() => player1, false);
        }

        [Test]
        public void TestPlayerClockStartsCatchingUpWhenTooFarBehind()
        {
            setAllWaiting(false);

            setMasterTime(SpectatorSyncManager.MAX_SYNC_OFFSET + 1);
            assertCatchingUp(() => player1, true);
            assertCatchingUp(() => player2, true);
        }

        [Test]
        public void TestPlayerClockKeepsCatchingUpWhenSlightlyOutOfSync()
        {
            setAllWaiting(false);

            setMasterTime(SpectatorSyncManager.MAX_SYNC_OFFSET + 1);
            setPlayerClockTime(() => player1, SpectatorSyncManager.SYNC_TARGET + 1);
            assertCatchingUp(() => player1, true);
        }

        [Test]
        public void TestPlayerClockStopsCatchingUpWhenInSync()
        {
            setAllWaiting(false);

            setMasterTime(SpectatorSyncManager.MAX_SYNC_OFFSET + 2);
            setPlayerClockTime(() => player1, SpectatorSyncManager.SYNC_TARGET);
            assertCatchingUp(() => player1, false);
            assertCatchingUp(() => player2, true);
        }

        [Test]
        public void TestPlayerClockDoesNotStopWhenSlightlyAhead()
        {
            setAllWaiting(false);

            setPlayerClockTime(() => player1, -SpectatorSyncManager.SYNC_TARGET);
            assertCatchingUp(() => player1, false);
            assertPlayerClockState(() => player1, true);
        }

        [Test]
        public void TestPlayerClockStopsWhenTooFarAheadAndStartsWhenBackInSync()
        {
            setAllWaiting(false);

            setPlayerClockTime(() => player1, -SpectatorSyncManager.SYNC_TARGET - 1);

            // This is a silent catchup, where IsCatchingUp = false but IsRunning = false also.
            assertCatchingUp(() => player1, false);
            assertPlayerClockState(() => player1, false);

            setMasterTime(1);
            assertCatchingUp(() => player1, false);
            assertPlayerClockState(() => player1, true);
        }

        [Test]
        public void TestInSyncPlayerClockDoesNotStartIfWaitingOnFrames()
        {
            setAllWaiting(false);

            assertPlayerClockState(() => player1, true);
            setWaiting(() => player1, true);
            assertPlayerClockState(() => player1, false);
        }

        [Test]
        public void TestSlowestPlayerPausesMasterUntilBufferReplenished()
        {
            setAllWaiting(false);
            AddStep("enable slowest sync with one low buffer", () =>
            {
                available1 = 500;
                syncManager.FollowSlowest = true;
            });
            AddAssert("master paused for slowest", () => !master.IsRunning);
            AddStep("replenish above low water only", () => available1 = 2000);
            AddAssert("master still buffering", () => !master.IsRunning);
            AddStep("replenish to resume water", () => available1 = 4000);
            AddAssert("master resumes", () => master.IsRunning);
        }

        [Test]
        public void TestSlowestSyncWaitsForLaggingPlayerWithAvailableFrames()
        {
            setAllWaiting(false);
            AddStep("enable slowest sync", () => syncManager.FollowSlowest = true);
            setMasterTime(100);
            setPlayerClockTime(() => player1, 0);
            AddAssert("master waits for other player", () => !master.IsRunning);
            setPlayerClockTime(() => player2, 0);
            AddAssert("master resumes after catchup", () => master.IsRunning);
        }

        [Test]
        public void TestSlowestSyncTimeoutAndRecovery()
        {
            setAllWaiting(false);
            AddStep("enable slowest sync", () =>
            {
                available1 = 0;
                syncManager.FollowSlowest = true;
                syncManager.BufferTimeout = 1000;
            });
            AddAssert("master pauses", () => !master.IsRunning);
            AddUntilStep("timeout lets others continue", () => master.IsRunning);
            AddStep("partial recovery", () => available1 = 2000);
            AddAssert("excluded player does not block", () => master.IsRunning);
            AddStep("full recovery", () => available1 = 4000);
            AddWaitStep("allow rejoin", 2);
            AddStep("buffer runs low again", () => available1 = 0);
            AddAssert("rejoined player blocks again", () => !master.IsRunning);
        }

        [Test]
        public void TestSlowestSyncRemovalAndCompletedReplay()
        {
            setAllWaiting(false);
            AddStep("enable slowest sync", () =>
            {
                available1 = available2 = 0;
                syncManager.FollowSlowest = true;
            });
            AddAssert("master pauses", () => !master.IsRunning);
            AddStep("remove first player", () => syncManager.RemoveManagedClock(player1));
            AddAssert("remaining player still blocks", () => !master.IsRunning);
            AddStep("remaining replay complete", () => available2 = double.PositiveInfinity);
            AddAssert("completed replay needs no buffer", () => master.IsRunning);
        }

        private void setWaiting(Func<SpectatorPlayerClock> playerClock, bool waiting)
            => AddStep($"set player clock {clocksById[playerClock()]} waiting = {waiting}", () => playerClock().WaitingOnFrames = waiting);

        private void setAllWaiting(bool waiting) => AddStep($"set all player clocks waiting = {waiting}", () =>
        {
            player1.WaitingOnFrames = waiting;
            player2.WaitingOnFrames = waiting;
        });

        private void setMasterTime(double time)
            => AddStep($"set master = {time}", () => master.Seek(time));

        /// <summary>
        /// clock.Time = master.Time - offsetFromMaster
        /// </summary>
        private void setPlayerClockTime(Func<SpectatorPlayerClock> playerClock, double offsetFromMaster)
            => AddStep($"set player clock {clocksById[playerClock()]} = master - {offsetFromMaster}", () => playerClock().Seek(master.CurrentTime - offsetFromMaster));

        private void assertCatchingUp(Func<SpectatorPlayerClock> playerClock, bool catchingUp) =>
            AddAssert($"player clock {clocksById[playerClock()]} {(catchingUp ? "is" : "is not")} catching up", () => playerClock().IsCatchingUp == catchingUp);

        private void assertPlayerClockState(Func<SpectatorPlayerClock> playerClock, bool running)
            => AddAssert($"player clock {clocksById[playerClock()]} {(running ? "is" : "is not")} running", () => playerClock().IsRunning == running);

        private class TestManualClock : ManualClock, IAdjustableClock
        {
            public TestManualClock()
            {
                IsRunning = true;
            }

            public void Start() => IsRunning = true;

            public void Stop() => IsRunning = false;

            public bool Seek(double position)
            {
                CurrentTime = position;
                return true;
            }

            public void Reset()
            {
                IsRunning = false;
                CurrentTime = 0;
            }

            public void ResetSpeedAdjustments()
            {
            }
        }
    }
}
