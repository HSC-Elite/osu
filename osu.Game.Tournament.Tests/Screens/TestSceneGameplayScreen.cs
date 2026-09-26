// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Gameplay.Components;
using osu.Game.Tournament.Screens.Gameplay.Components.MatchHeader;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneGameplayScreen : TournamentScreenTestScene
    {
        [Cached]
        private TournamentMatchChatDisplay chat = new TournamentMatchChatDisplay { Width = 0.5f };

        [Resolved]
        private TournamentMatchScoreProcessor scoreProcessor { get; set; } = null!;

        [Test]
        public void TestWarmup()
        {
            createScreen();

            checkScoreVisibility(false);

            toggleWarmup();
            checkScoreVisibility(true);

            toggleWarmup();
            checkScoreVisibility(false);
        }

        [Test]
        public void TestStartupState([Values] TourneyState state)
        {
            AddStep("set state", () => IPCInfo.State.Value = state);
            createScreen();
        }

        [Test]
        public void TestStartupStateNoCurrentMatch([Values] TourneyState state)
        {
            AddStep("set null current", () => Ladder.CurrentMatch.Value = null);
            AddStep("set state", () => IPCInfo.State.Value = state);
            createScreen();
        }

        [Test]
        public void TestLiveScoreWarningVisibility()
        {
            AddStep("reset gameplay and API state", () =>
            {
                IPCInfo.State.Value = TourneyState.Idle;
                scoreProcessor.CurrentlyListening.Value = false;
                scoreProcessor.WaitingForAuthoritativeResult.Value = false;
            });
            createScreen();

            AddStep("set playing state", () => IPCInfo.State.Value = TourneyState.Playing);
            checkLiveScoreWarningVisibility(false);

            toggleWarmup();
            checkLiveScoreWarningVisibility(false);

            AddStep("start API listener", () => scoreProcessor.CurrentlyListening.Value = true);
            checkLiveScoreWarningVisibility(true);

            AddStep("stop API listener", () => scoreProcessor.CurrentlyListening.Value = false);
            checkLiveScoreWarningVisibility(false);

            AddStep("set ranking state", () => IPCInfo.State.Value = TourneyState.Ranking);
            checkLiveScoreWarningVisibility(false);
        }

        [Test]
        public void TestSongBarShowsWhileWaitingForAuthoritativeResult()
        {
            AddStep("reset API listener and result wait", () =>
            {
                IPCInfo.State.Value = TourneyState.Idle;
                scoreProcessor.CurrentlyListening.Value = false;
                scoreProcessor.WaitingForAuthoritativeResult.Value = false;
            });
            createScreen();

            AddUntilStep("song bar initially idle", () => !gameplaySongBar.IsLoading.Value);
            AddStep("wait for authoritative result", () => scoreProcessor.WaitingForAuthoritativeResult.Value = true);
            AddUntilStep("no loading when API listener is off", () => !gameplaySongBar.IsLoading.Value);
            AddStep("enable API listener", () => scoreProcessor.CurrentlyListening.Value = true);
            AddUntilStep("song bar shows loading", () => gameplaySongBar.IsLoading.Value);
            AddStep("authoritative result received", () => scoreProcessor.WaitingForAuthoritativeResult.Value = false);
            AddUntilStep("song bar hides loading", () => !gameplaySongBar.IsLoading.Value);
            AddStep("stop API listener", () => scoreProcessor.CurrentlyListening.Value = false);
        }

        private void createScreen()
        {
            AddStep("setup screen", () =>
            {
                Remove(chat, false);

                Children = new Drawable[]
                {
                    new GameplayScreen(),
                    chat,
                };
            });
        }

        private void checkScoreVisibility(bool visible)
            => AddUntilStep($"scores {(visible ? "shown" : "hidden")}",
                () =>
                {
                    var scores = this.ChildrenOfType<TeamScore>().ToArray();
                    return scores.Length > 0 && scores.All(score => score.ShowScore == visible);
                });

        private void checkLiveScoreWarningVisibility(bool visible)
            => AddUntilStep($"live score warning {(visible ? "shown" : "hidden")}",
                () =>
                {
                    Container? warning = this.ChildrenOfType<Container>().SingleOrDefault(container => container.Name == "Live score warning");

                    return warning != null && (visible ? warning.Alpha > 0.5f : warning.Alpha < 0.01f);
                });

        private GameplaySongBar gameplaySongBar => this.ChildrenOfType<GameplaySongBar>().Single();

        private void toggleWarmup()
            => AddStep("toggle warmup", () => this.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Toggle warmup").TriggerClick());
    }
}
