// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Storyboards;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.Osu.Tests
{
    public partial class TestSceneReplayRecording : PlayerTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        protected override TestPlayer CreatePlayer(Ruleset ruleset) => new TestPlayer(allowPause: true, showResults: false, pauseOnFocusLost: false);

        [Resolved]
        private AudioManager audioManager { get; set; } = null!;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => new Beatmap
        {
            HitObjects =
            {
                new HitCircle
                {
                    Position = OsuPlayfield.BASE_SIZE / 2,
                    StartTime = 0,
                },
                new HitCircle
                {
                    Position = OsuPlayfield.BASE_SIZE / 2,
                    StartTime = 5000,
                },
                new HitCircle
                {
                    Position = OsuPlayfield.BASE_SIZE / 2,
                    StartTime = 10000,
                },
                new HitCircle
                {
                    Position = OsuPlayfield.BASE_SIZE / 2,
                    StartTime = 15000,
                }
            }
        };

        protected override WorkingBeatmap CreateWorkingBeatmap(IBeatmap beatmap, Storyboard? storyboard = null) =>
            new ClockBackedTestWorkingBeatmap(beatmap, storyboard, new FramedClock(new ManualClock { Rate = 1 }), audioManager);

        [Test]
        public void TestRecording()
        {
            seekTo(0);
            AddStep("move cursor to circle", () => InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Single()));
            AddStep("press X", () => InputManager.PressKey(Key.X));
            seekTo(15);
            AddStep("release X", () => InputManager.ReleaseKey(Key.X));
            AddAssert("right button press recorded to replay", () => Player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.SequenceEqual([OsuAction.RightButton])));

            seekTo(5000);
            AddStep("move cursor to circle", () => InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Single()));
            AddStep("press Z", () => InputManager.PressKey(Key.Z));
            seekTo(5015);
            AddStep("release Z", () => InputManager.ReleaseKey(Key.Z));
            AddAssert("left button press recorded to replay", () => Player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.SequenceEqual([OsuAction.LeftButton])));

            seekTo(10000);
            AddStep("move cursor to circle", () => InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Single()));
            AddStep("press C", () => InputManager.PressKey(Key.C));
            seekTo(10015);
            AddStep("release C", () => InputManager.ReleaseKey(Key.C));
            AddAssert("smoke button press recorded to replay", () => Player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.SequenceEqual([OsuAction.Smoke])));
        }

        [Test]
        public void TestReplayInputIsRecordedAsLocalPlay()
        {
            AddStep("attach test player input", () => Player.DrawableRuleset.SetReplayInput(new Replay
            {
                Frames =
                {
                    new OsuReplayFrame(-2000, OsuPlayfield.BASE_SIZE / 2),
                    new OsuReplayFrame(0, OsuPlayfield.BASE_SIZE / 2, OsuAction.RightButton),
                    new OsuReplayFrame(15, OsuPlayfield.BASE_SIZE / 2),
                    new OsuReplayFrame(20000, OsuPlayfield.BASE_SIZE / 2),
                }
            }));
            AddStep("pause gameplay", () => Player.DrawableRuleset.IsPaused.Value = true);
            AddStep("resume gameplay", () => Player.DrawableRuleset.IsPaused.Value = false);
            AddAssert("physical input remains blocked", () => !((DrawableOsuRuleset)Player.DrawableRuleset).KeyBindingInputManager.UseParentInput);
            AddAssert("no replay handler attached", () => ((DrawableOsuRuleset)Player.DrawableRuleset).KeyBindingInputManager.ReplayInputHandler == null);
            seekTo(100);
            AddAssert("still local play", () => !Player.DrawableRuleset.HasReplayLoaded.Value && Player.DrawableRuleset.ReplayScore == null);
            AddAssert("replay input judged", () => Player.ScoreProcessor.Combo.Value == 1);
            AddAssert("input recorded", () => Player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.Contains(OsuAction.RightButton)));
        }

        [Test]
        public void TestReplayInputSurvivesPauseAndResume()
        {
            AddStep("attach test player input", () => Player.DrawableRuleset.SetReplayInput(new Replay
            {
                Frames =
                {
                    new OsuReplayFrame(-2000, OsuPlayfield.BASE_SIZE / 2),
                    new OsuReplayFrame(0, OsuPlayfield.BASE_SIZE / 2, OsuAction.RightButton),
                    new OsuReplayFrame(1000, OsuPlayfield.BASE_SIZE / 2),
                    new OsuReplayFrame(5000, OsuPlayfield.BASE_SIZE / 2, OsuAction.LeftButton),
                    new OsuReplayFrame(5015, OsuPlayfield.BASE_SIZE / 2),
                    new OsuReplayFrame(20000, OsuPlayfield.BASE_SIZE / 2),
                }
            }));
            seekTo(100);
            AddAssert("recorded button held", () => ((DrawableOsuRuleset)Player.DrawableRuleset).KeyBindingInputManager.PressedActions.Contains(OsuAction.RightButton));
            AddStep("pause player", () => Assert.That(Player.Pause(), Is.True));
            AddAssert("clock paused", () => Player.GameplayClockContainer.IsPaused.Value);
            AddAssert("buttons released while paused", () => !((DrawableOsuRuleset)Player.DrawableRuleset).KeyBindingInputManager.PressedActions.Any());
            AddStep("resume player", () => Player.Resume());
            AddUntilStep("clock resumed without cursor click", () => !Player.GameplayClockContainer.IsPaused.Value);
            AddAssert("recorded hold restored", () => ((DrawableOsuRuleset)Player.DrawableRuleset).KeyBindingInputManager.PressedActions.Contains(OsuAction.RightButton));
            AddStep("send physical input", () =>
            {
                InputManager.MoveMouseTo(new osuTK.Vector2(10));
                InputManager.PressKey(Key.C);
            });
            AddAssert("physical smoke blocked", () => !((DrawableOsuRuleset)Player.DrawableRuleset).KeyBindingInputManager.PressedActions.Contains(OsuAction.Smoke));
            seekTo(5100);
            AddAssert("second object judged after resume", () => Player.ScoreProcessor.Combo.Value == 2);
            AddAssert("second press recorded", () => Player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.Contains(OsuAction.LeftButton)));
            AddStep("release physical input", () => InputManager.ReleaseKey(Key.C));
        }

        [Test]
        public void TestPressAndReleaseOnSameFrame()
        {
            seekTo(0);
            AddStep("move cursor to circle", () => InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Single()));
            AddStep("press X", () => InputManager.PressKey(Key.X));
            AddStep("release X", () => InputManager.ReleaseKey(Key.X));
            AddAssert("right button press recorded to replay", () => Player.Score.Replay.Frames.OfType<OsuReplayFrame>().Any(f => f.Actions.SequenceEqual([OsuAction.RightButton])));
        }

        private void seekTo(double time)
        {
            AddStep($"seek to {time}ms", () => Player.GameplayClockContainer.Seek(time));
            AddUntilStep("wait for seek to finish", () => Player.DrawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(time).Within(500));
        }
    }
}
