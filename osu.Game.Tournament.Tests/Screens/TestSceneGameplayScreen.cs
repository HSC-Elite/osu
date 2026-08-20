// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Tournament.IPC.MemoryIPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Online.Requests;
using osu.Game.Tournament.Online.Requests.Responses;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Gameplay.Components.MatchHeader;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneGameplayScreen : TournamentScreenTestScene
    {
        private MemoryBasedIPCWithMatchListener ipc => (MemoryBasedIPCWithMatchListener)IPCInfo;

        private const int test_beatmap_id = 789;

        private BeatmapChoice activeChoice = null!;

        [Cached]
        private TournamentMatchChatDisplay chat = new TournamentMatchChatDisplay { Width = 0.5f };

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
        public void TestManualResultIsOnlyAvailableDuringSettlement()
        {
            AddStep("load IPC", loadIpc);
            AddStep("setup API", setupMatchApi);

            AddStep("start listening", () => ipc.StartListening(123));
            AddUntilStep("match starts", () => ipc.CurrentlyPlaying.Value);
            AddStep("bind current choice", bindCurrentChoice);
            AddStep("reject manual result before settlement", () =>
                Assert.That(ipc.SubmitManualResult(123456, 654321), Is.False));

            AddStep("enter gameplay", () => ipc.State.Value = TourneyState.Playing);
            AddStep("enter settlement", () => ipc.State.Value = TourneyState.Ranking);
            AddUntilStep("enable manual result", () => ipc.CanSubmitManualResult.Value);
            AddStep("accept manual result during settlement", () =>
                Assert.That(ipc.SubmitManualResult(123456, 654321), Is.True));
            AddUntilStep("apply manual scores", () => ipc.Score1.Value == 123456 && ipc.Score2.Value == 654321
                                                       && activeChoice.Scores[TeamColour.Red] == 123456 && activeChoice.Scores[TeamColour.Blue] == 654321
                                                       && !ipc.CanSubmitManualResult.Value);
        }

        [Test]
        public void TestManualResultCanReplaceTimedOutApiResult()
        {
            AddStep("load IPC", loadIpc);
            AddStep("setup API", setupMatchApi);
            AddStep("shorten API timeout", () => ipc.ResultFetchTimeout = 1);

            AddStep("start listening", () => ipc.StartListening(123));
            AddUntilStep("match starts", () => ipc.CurrentlyPlaying.Value);
            AddStep("bind current choice", bindCurrentChoice);
            AddStep("enter gameplay", () => ipc.State.Value = TourneyState.Playing);
            AddStep("enter settlement", () => ipc.State.Value = TourneyState.Ranking);
            AddUntilStep("wait for API timeout", () => !ipc.CurrentlyPlaying.Value && ipc.CanSubmitManualResult.Value);
            AddStep("accept manual result after timeout", () =>
                Assert.That(ipc.SubmitManualResult(123456, 654321), Is.True));
            AddUntilStep("apply manual scores", () => ipc.Score1.Value == 123456 && ipc.Score2.Value == 654321
                                                       && activeChoice.Scores[TeamColour.Red] == 123456 && activeChoice.Scores[TeamColour.Blue] == 654321);
        }

        [Test]
        public void TestAbortDoesNotOpenManualSettlement()
        {
            AddStep("load IPC", loadIpc);
            AddStep("setup API", setupMatchApi);
            AddStep("start listening", () => ipc.StartListening(123));
            AddUntilStep("match starts", () => ipc.CurrentlyPlaying.Value);
            AddStep("bind current choice", bindCurrentChoice);
            AddStep("enter gameplay", () => ipc.State.Value = TourneyState.Playing);
            AddStep("return to idle", () => ipc.State.Value = TourneyState.Idle);
            AddStep("abort current round", ipc.CurrentRoundAborted);
            AddStep("reject manual result after abort", () =>
            {
                Assert.That(ipc.Aborted, Is.True);
                Assert.That(ipc.CanSubmitManualResult.Value, Is.False);
                Assert.That(ipc.SubmitManualResult(123456, 654321), Is.False);
            });
        }

        [Test]
        public void TestLatestApiResultReplacesManualResult()
        {
            AddStep("load IPC", loadIpc);
            AddStep("setup API", setupMatchApi);

            AddStep("start listening", () => ipc.StartListening(123));
            AddUntilStep("match starts", () => ipc.CurrentlyPlaying.Value);
            AddStep("bind current choice", bindCurrentChoice);
            AddStep("enter gameplay", () => ipc.State.Value = TourneyState.Playing);
            AddStep("enter settlement", () => ipc.State.Value = TourneyState.Ranking);
            AddStep("submit manual result", () => Assert.That(ipc.SubmitManualResult(123456, 654321), Is.True));
            AddStep("make API return a late result", () =>
            {
                ((DummyAPIAccess)API).HandleRequest = request =>
                {
                    if (request is not GetAPIMatchInfo matchRequest)
                        return false;

                    matchRequest.TriggerSuccess(new APIMatchInfo
                    {
                        APIMatch = new APIMatch { ID = 123 },
                        Events =
                        [
                            new APIMatchEvent
                            {
                                Id = 999,
                                Game = new APIMatchGame
                                {
                                    Id = 456,
                                    BeatmapId = test_beatmap_id,
                                    Scores =
                                    [
                                        new MatchScore { UserID = 1, TotalScore = 1 },
                                        new MatchScore { UserID = 2, TotalScore = 2 },
                                    ]
                                },
                                Detail = new MatchEventDetail { Type = MatchEventType.Other },
                            }
                        ],
                        Users = [],
                        CurrentGameID = null,
                    });

                    return true;
                };
                ipc.FetchMatch();
            });
            AddUntilStep("apply latest API scores", () => ipc.Score1.Value == 1 && ipc.Score2.Value == 2
                                                          && activeChoice.Scores[TeamColour.Red] == 1 && activeChoice.Scores[TeamColour.Blue] == 2);
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

        private void toggleWarmup()
            => AddStep("toggle warmup", () => this.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Toggle warmup").TriggerClick());

        private void setupMatchApi()
        {
            Ladder.CurrentMatch.Value!.Team1.Value!.Players[0].OnlineID = 1;
            Ladder.CurrentMatch.Value.Team2.Value!.Players[0].OnlineID = 2;
            activeChoice = new BeatmapChoice { BeatmapID = test_beatmap_id, Type = ChoiceType.Pick };
            Ladder.CurrentMatch.Value.PicksBans.Add(activeChoice);

            ((DummyAPIAccess)API).HandleRequest = request =>
            {
                if (request is not GetAPIMatchInfo matchRequest)
                    return false;

                matchRequest.TriggerSuccess(new APIMatchInfo
                {
                    APIMatch = new APIMatch { ID = 123 },
                    Events = [],
                    Users = [],
                    CurrentGameID = 456,
                });

                return true;
            };
        }

        private void loadIpc()
        {
            if (ipc.Parent == null)
                Add(ipc);
        }

        private void bindCurrentChoice() => ipc.BindChoiceToNextOrCurrentMatch(activeChoice);
    }
}
