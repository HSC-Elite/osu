// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.OnlinePlay.Multiplayer;
using osu.Game.Screens.Play;

namespace osu.Game.Tests.Visual.Multiplayer
{
    public partial class TestSceneMultiplayerPlayer : MultiplayerTestScene
    {
        private MultiplayerPlayer player = null!;

        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("join room", () => JoinRoom(CreateDefaultRoom()));
            WaitForJoined();
        }

        [Test]
        public void TestGameplay()
        {
            setup();

            AddUntilStep("wait for gameplay start", () => player.LocalUserPlaying.Value);
        }

        [Test]
        public void TestReplayInputSurvivesMultiplayerStart()
        {
            DrawableOsuRuleset drawable() => ((ReplayInputMultiplayerPlayer)player).TestDrawableRuleset;

            setup(replayInput: true);
            AddUntilStep("wait for gameplay start", () => player.LocalUserPlaying.Value);
            AddAssert("physical input remains blocked", () => !drawable().KeyBindingInputManager.UseParentInput);
            AddAssert("no replay handler attached", () => drawable().KeyBindingInputManager.ReplayInputHandler == null);
            AddAssert("local clock has no replay handler", () => ((FrameStabilityContainer)drawable().FrameStableClock).ReplayInputHandler == null);
            AddUntilStep("recorded input produces score", () => player.ScoreProcessor.TotalScore.Value > 0);
            AddAssert("input recorded for spectators", () => player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.Any()));
        }

        [Test]
        public void TestFail()
        {
            setup(() => new[] { new OsuModAutopilot() });

            AddUntilStep("wait for gameplay start", () => player.LocalUserPlaying.Value);
            AddStep("set health zero", () => player.ChildrenOfType<HealthProcessor>().Single().Health.Value = 0);
            AddUntilStep("wait for fail", () => player.ChildrenOfType<HealthProcessor>().Single().HasFailed);
            AddAssert("fail animation not shown", () => !player.GameplayState.HasFailed);

            // ensure that even after reaching a failed state, score processor keeps accounting for new hit results.
            // the testing method used here (autopilot + hold key) is sort-of dodgy, but works enough.
            AddAssert("score is zero", () => player.GameplayState.ScoreProcessor.TotalScore.Value == 0);
            AddStep("hold key", () => player.ChildrenOfType<OsuInputManager.RulesetKeyBindingContainer>().First().TriggerPressed(OsuAction.LeftButton));
            AddUntilStep("score changed", () => player.GameplayState.ScoreProcessor.TotalScore.Value > 0);
        }

        private void setup(Func<IReadOnlyList<Mod>>? mods = null, bool replayInput = false)
        {
            AddStep("set beatmap", () =>
            {
                Beatmap.Value = CreateWorkingBeatmap(new OsuRuleset().RulesetInfo);
                SelectedMods.Value = mods?.Invoke() ?? Array.Empty<Mod>();
            });

            AddStep("Start track playing", () =>
            {
                Beatmap.Value.Track.Start();
            });

            AddStep("initialise gameplay", () =>
            {
                var item = new PlaylistItem(Beatmap.Value.BeatmapInfo)
                {
                    RulesetID = Beatmap.Value.BeatmapInfo.Ruleset.OnlineID,
                };
                var users = MultiplayerClient.ServerRoom!.Users.ToArray();
                Stack.Push(player = replayInput
                    ? new ReplayInputMultiplayerPlayer(MultiplayerClient.ServerAPIRoom!, item, users)
                    : new MultiplayerPlayer(MultiplayerClient.ServerAPIRoom!, item, users));
            });

            AddUntilStep("wait for player to be current", () => player.IsCurrentScreen() && player.IsLoaded);

            AddAssert("gameplay clock is paused", () => player.ChildrenOfType<GameplayClockContainer>().Single().IsPaused.Value);
            AddAssert("gameplay clock is not running", () => !player.ChildrenOfType<GameplayClockContainer>().Single().IsRunning);

            AddStep("start gameplay", () => ((IMultiplayerClient)MultiplayerClient).GameplayStarted());

            AddUntilStep("gameplay clock is not paused", () => !player.ChildrenOfType<GameplayClockContainer>().Single().IsPaused.Value);
            AddAssert("gameplay clock is running", () => player.ChildrenOfType<GameplayClockContainer>().Single().IsRunning);
        }

        private partial class ReplayInputMultiplayerPlayer : MultiplayerPlayer
        {
            public DrawableOsuRuleset TestDrawableRuleset => (DrawableOsuRuleset)DrawableRuleset;

            public ReplayInputMultiplayerPlayer(Room room, PlaylistItem item, MultiplayerRoomUser[] users)
                : base(room, item, users)
            {
            }

            protected override void PrepareReplay()
            {
                base.PrepareReplay();
                ((DrawableOsuRuleset)DrawableRuleset).SetReplayInput(new OsuAutoGenerator(GameplayState.Beatmap, GameplayState.Mods).Generate());
            }
        }
    }
}
