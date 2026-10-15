// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Sprites;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osu.Game.Tests.Resources;
using osu.Game.Tests.Visual;
using osu.Game.Tests.Visual.Ranking;
using osu.Game.Tournament.Screens.Gameplay.GameplayScreenResultsScreen;
using osuTK;
using Realms;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneTournamentResultsScreen : OsuTestScene
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; }

        private TestResultsContainer resultsContainer;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            realm.Run(r =>
            {
                var beatmapInfo = r.All<BeatmapInfo>()
                                   .Filter($"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.OnlineID)} = $0 AND {nameof(BeatmapInfo.StatusInt)} = 1", 0)
                                   .FirstOrDefault();

                if (beatmapInfo != null)
                    Beatmap.Value = beatmaps.GetWorkingBeatmap(beatmapInfo);
            });

            // scheduling is needed as scaling the content immediately causes the entire scene to shake badly, for some odd reason.
            AddSliderStep("scale", 0.5f, 1.6f, 1f, v => Schedule(() =>
            {
                Content.Scale = new Vector2(v);
                Content.Size = new Vector2(1f / v);
            }));
        }

        private int onlineScoreID = 1;

        [TestCase(1, ScoreRank.X, 0)]
        [TestCase(0.9999, ScoreRank.S, 0)]
        [TestCase(0.975, ScoreRank.S, 0)]
        [TestCase(0.975, ScoreRank.A, 1)]
        [TestCase(0.925, ScoreRank.A, 5)]
        [TestCase(0.85, ScoreRank.B, 9)]
        [TestCase(0.75, ScoreRank.C, 11)]
        [TestCase(0.5, ScoreRank.D, 21)]
        [TestCase(0.2, ScoreRank.D, 51)]
        public void TestResultsWithPlayer(double accuracy, ScoreRank rank, int missCount)
        {
            TestResultsScreen? screen = null;

            loadResultsScreen(() =>
            {
                var score = TestResources.CreateTestScoreInfo();

                score.OnlineID = onlineScoreID++;
                score.HitEvents = TestSceneStatisticsPanel.CreatePositionDistributedHitEvents();
                score.Accuracy = accuracy;
                score.Rank = rank;
                score.Statistics[HitResult.Miss] = missCount;

                return screen = createResultsScreen(score);
            });
            AddUntilStep("wait for loaded", () => screen?.IsLoaded == true);
            AddUntilStep("result circle loaded", () => screen?.ChildrenOfType<AccuracyCircle>().Any() == true);
            AddAssert("no rank badges displayed", () => this.ChildrenOfType<RankBadge>().All(b => !b.IsPresent));
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        public void TestPerfectCombo(bool mirrored, bool fullCombo)
        {
            TestResultsScreen? screen = null;
            var score = TestResources.CreateTestScoreInfo();
            score.Statistics[HitResult.Miss] = 0;
            score.MaxCombo = score.GetMaximumAchievableCombo() - (fullCombo ? 0 : 1);

            Container badge() => screen!.ChildrenOfType<Container>().Single(c => c.Name == "Perfect badge");

            loadResultsScreen(() => screen = createResultsScreen(score, mirrored));
            AddUntilStep("perfect badge loaded", () => screen?.ChildrenOfType<Container>().Any(c => c.Name == "Perfect badge") == true);
            AddAssert("maximum achievable combo is positive", () => score.GetMaximumAchievableCombo() > 0);
            AddAssert("perfect highlight matches full combo", () =>
                badge().ChildrenOfType<Box>().Single().Colour.AverageColour == Color4Extensions.FromHex(fullCombo ? "#FFC300" : "#383838"));
            AddAssert("perfect label displayed", () => badge().ChildrenOfType<OsuSpriteText>().Single().Text.ToString() == "PERFECT");
            AddUntilStep("perfect badge clears circle", () =>
            {
                var circle = screen!.ChildrenOfType<AccuracyCircle>().Single().ScreenSpaceDrawQuad.AABBFloat;
                var bounds = badge().ScreenSpaceDrawQuad.AABBFloat;
                float centreX = circle.Left + circle.Width / 2;
                float centreY = circle.Top + circle.Height / 2;
                float dx = Math.Clamp(centreX, bounds.Left, bounds.Right) - centreX;
                float dy = Math.Clamp(centreY, bounds.Top, bounds.Bottom) - centreY;
                return dx * dx + dy * dy >= circle.Width * circle.Width / 4;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestMirroredLayout(bool mirrored)
        {
            TestResultsScreen? screen = null;

            loadResultsScreen(() => screen = createResultsScreen(TestResources.CreateTestScoreInfo(), mirrored));
            AddUntilStep("result circle loaded", () => screen?.ChildrenOfType<AccuracyCircle>().Any() == true);
            AddAssert("requested layout selected", () => screen?.Mirrored == mirrored);
            AddAssert("hit statistics on the outside", () =>
            {
                var circle = screen!.ChildrenOfType<AccuracyCircle>().Single().ScreenSpaceDrawQuad.Centre;
                var statistics = screen.ChildrenOfType<GridContainer>().Single(c => c.Name == "Hit statistics").ScreenSpaceDrawQuad.Centre;
                return mirrored ? statistics.X < circle.X : statistics.X > circle.X;
            });
            AddAssert("mods opposite statistics", () =>
            {
                var circle = screen!.ChildrenOfType<AccuracyCircle>().Single().ScreenSpaceDrawQuad.Centre;
                var mods = screen.ChildrenOfType<Container>().Single(m => m.Name == "Mods");
                return mods.Children.All(mod => mirrored ? mod.ScreenSpaceDrawQuad.Centre.X > circle.X : mod.ScreenSpaceDrawQuad.Centre.X < circle.X);
            });
            AddAssert("circle does not cover statistics", () =>
            {
                var circle = screen!.ChildrenOfType<AccuracyCircle>().Single().ScreenSpaceDrawQuad.AABBFloat;
                var statistics = screen.ChildrenOfType<GridContainer>().Single(c => c.Name == "Hit statistics");
                return statistics.ChildrenOfType<OsuSpriteText>().All(text =>
                {
                    var bounds = text.ScreenSpaceDrawQuad.AABBFloat;
                    return mirrored ? bounds.Right <= circle.Left : circle.Right <= bounds.Left;
                });
            });
            AddAssert("no rank badges displayed", () => this.ChildrenOfType<RankBadge>().All(b => !b.IsPresent));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestDynamicPlacement(bool mirrored)
        {
            TestResultsScreen? screen = null;
            var provider = new TestLeaderboardProvider();
            var score = TestResources.CreateTestScoreInfo();
            var entry = new GameplayLeaderboardScore(score, false, GameplayLeaderboardScore.ComboDisplayMode.Highest);
            var replacement = new GameplayLeaderboardScore(score, false, GameplayLeaderboardScore.ComboDisplayMode.Highest);

            Container badge() => screen!.ChildrenOfType<Container>().Single(c => c.Name == "Placement badge");
            OsuSpriteText text() => badge().ChildrenOfType<OsuSpriteText>().Single();

            loadResultsScreen(() => screen = createResultsScreen(score, mirrored), provider);
            AddUntilStep("placement badge loaded", () => screen?.ChildrenOfType<Container>().Any(c => c.Name == "Placement badge") == true);
            AddAssert("unknown placement hidden", () => badge().Alpha == 0);
            AddStep("another player arrives first", () =>
            {
                var otherScore = TestResources.CreateTestScoreInfo();
                otherScore.User = new APIUser { Id = score.UserID + 1, Username = "Other player" };
                var otherEntry = new GameplayLeaderboardScore(otherScore, false, GameplayLeaderboardScore.ComboDisplayMode.Highest);
                otherEntry.Position.Value = 1;
                provider.Entries.Add(otherEntry);
            });
            AddAssert("another player's placement ignored", () => badge().Alpha == 0);
            AddAssert("placement follows mirrored layout", () =>
            {
                var perfect = screen!.ChildrenOfType<Container>().Single(c => c.Name == "Perfect badge");
                return mirrored
                    ? badge().ScreenSpaceDrawQuad.Centre.X > perfect.ScreenSpaceDrawQuad.Centre.X
                    : badge().ScreenSpaceDrawQuad.Centre.X < perfect.ScreenSpaceDrawQuad.Centre.X;
            });
            AddStep("player arrives after results loaded", () => provider.Entries.Add(entry));
            AddAssert("unset position remains hidden", () => badge().Alpha == 0);

            foreach (var (position, expected) in new[] { (1, "1st"), (2, "2nd"), (3, "3rd"), (4, "4th"), (11, "11th"), (12, "12th"), (13, "13th"), (21, "21st") })
            {
                AddStep($"move player to {position}", () => entry.Position.Value = position);
                AddUntilStep($"shows {expected}", () => badge().Alpha == 1 && text().Text.ToString() == expected);
                AddAssert("only first place is highlighted", () => position == 1 ? text().Colour.AverageColour != Colour4.White : text().Colour.AverageColour == Colour4.White);
            }

            AddStep("remove player", () => provider.Entries.Remove(entry));
            AddUntilStep("removed player hidden", () => badge().Alpha == 0);
            AddStep("replace leaderboard entry", () =>
            {
                replacement.Position.Value = 3;
                provider.Entries.Add(replacement);
            });
            AddUntilStep("replacement position displayed", () => badge().Alpha == 1 && text().Text.ToString() == "3rd");
            AddStep("old entry changes", () => entry.Position.Value = 1);
            AddAssert("old entry no longer affects badge", () => text().Text.ToString() == "3rd");
            AddStep("invalid placement", () => replacement.Position.Value = 0);
            AddUntilStep("invalid placement hidden", () => badge().Alpha == 0);
            AddStep("placement becomes unknown", () => replacement.Position.Value = null);
            AddAssert("unknown placement remains hidden", () => badge().Alpha == 0);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestExistingPlacement(bool mirrored)
        {
            TestResultsScreen? screen = null;
            var score = TestResources.CreateTestScoreInfo();
            var provider = new TestLeaderboardProvider();
            var entry = new GameplayLeaderboardScore(score, false, GameplayLeaderboardScore.ComboDisplayMode.Highest);
            entry.Position.Value = 2;
            provider.Entries.Add(entry);

            loadResultsScreen(() => screen = createResultsScreen(score, mirrored), provider);
            AddUntilStep("existing placement shown", () => screen?.ChildrenOfType<Container>()
                .Any(c => c.Name == "Placement badge" && c.Alpha == 1 && c.ChildrenOfType<OsuSpriteText>().Single().Text.ToString() == "2nd") == true);
        }

        [Test]
        public void TestWithoutLeaderboardScores()
        {
            TestResultsScreen? screen = null;
            loadResultsScreen(() => screen = createResultsScreen());
            AddUntilStep("result loaded", () => screen?.ChildrenOfType<AccuracyCircle>().Any() == true);
            AddAssert("no fabricated placement", () => screen!.ChildrenOfType<Container>().Single(c => c.Name == "Placement badge").Alpha == 0);
        }

        private void loadResultsScreen(Func<ResultsScreen> createResults, TestLeaderboardProvider? leaderboard = null)
        {
            ResultsScreen? results = null;

            AddStep("load results", () => Child = resultsContainer = new TestResultsContainer(results = createResults(), leaderboard));
        }

        private TestResultsScreen createResultsScreen(ScoreInfo? score = null, bool mirrored = false)
            => new TestResultsScreen(score ?? TestResources.CreateTestScoreInfo(), mirrored);

        private partial class TestResultsContainer : Container
        {
            [Cached(typeof(Player))]
            public readonly Player Player = new TestPlayer();

            [Cached(typeof(UserStatisticsWatcher))]
            public readonly UserStatisticsWatcher UserStatisticsWatcher;

            [Cached(typeof(IGameplayLeaderboardProvider))]
            private readonly IGameplayLeaderboardProvider leaderboard;

            public TestResultsContainer(IScreen screen, TestLeaderboardProvider? leaderboard)
            {
                this.leaderboard = leaderboard ?? new TestLeaderboardProvider();
                RelativeSizeAxes = Axes.Both;
                OsuScreenStack stack;

                InternalChildren = new Drawable[]
                {
                    UserStatisticsWatcher = new UserStatisticsWatcher(new LocalUserStatisticsProvider()),
                    stack = new OsuScreenStack
                    {
                        RelativeSizeAxes = Axes.Both,
                    }
                };

                stack.Push(screen);
            }
        }

        private partial class TestResultsScreen : TournamentResultsScreen
        {
            public TestResultsScreen(ScoreInfo score, bool mirrored = false)
                : base(score, mirrored)
            {
                AllowRetry = false;
                IsLocalPlay = true;
            }

        }

        private class TestLeaderboardProvider : IGameplayLeaderboardProvider
        {
            public BindableList<GameplayLeaderboardScore> Entries { get; } = new BindableList<GameplayLeaderboardScore>();
            public IBindableList<GameplayLeaderboardScore> Scores => Entries;
        }
    }
}
