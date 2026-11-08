// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Game.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Framework.Testing.Input;
using osuTK.Graphics;
using osuTK.Input;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Ladder;
using osu.Game.Tournament.Screens.MapPool;
using osu.Game.Tournament.Screens.RoomSelect;

namespace osu.Game.Tournament.Tests
{
    public partial class TestSceneTournamentSceneManager : TournamentTestScene
    {
        private TestSceneManager manager = null!;
        private readonly ManualClock promptClock = new ManualClock();
        private int invoked;
        private AutoAdvancePrompt prompt = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(manager = new TestSceneManager());
        }

        [Test]
        public void TestConsoleNavigationPreservesStage()
        {
            AddStep("show gameplay", () => manager.SetScreen(typeof(GameplayScreen)));
            AddUntilStep("gameplay fully visible", () => manager.ActiveStage?.Alpha == 1);
            AddStep("open room selection", () => manager.SetScreen(typeof(RoomSelectScreen)));
            AddAssert("room controls selected", () => manager.ActiveScreen is RoomSelectScreen);
            AddAssert("gameplay stays active", () => manager.ActiveStage is GameplayScreen { IsStageActive: true, Alpha: 1 });
            AddStep("open bracket", () => manager.SetScreen(typeof(LadderScreen)));
            AddAssert("bracket is console", () => manager.ActiveScreen is LadderScreen && manager.ActiveStage is GameplayScreen);
            AddStep("return to gameplay", () => manager.SetScreen(typeof(GameplayScreen)));
            AddAssert("no stage entrance replayed", () => !manager.ActiveStage!.Transforms.Any());
            AddAssert("panel lives outside stage", () => !manager.ActiveStage!.ChildrenOfType<ControlPanel>().Any());
            AddAssert("panel is loaded", () => manager.ActiveStage!.ControlPanel.IsLoaded);
            AddStep("select map pool", () => manager.SetScreen(typeof(MapPoolScreen)));
            AddAssert("both selections changed", () => manager.ActiveScreen == manager.ActiveStage && manager.ActiveStage is MapPoolScreen);
            AddAssert("previous stage inactive", () => manager.PreviousStage is GameplayScreen { IsStageActive: false });
        }

        [Test]
        public void TestAutoAdvanceSurvivesConsoleAndReturn()
        {
            requestPrompt();
            AddStep("open console", () => manager.SetScreen(typeof(RoomSelectScreen)));
            AddStep("advance half way", () => promptClock.CurrentTime += 500);
            AddWaitStep("update countdown", 2);
            AddAssert("prompt still running", () => invoked == 0 && prompt.IsAlive);
            AddStep("return to same stage", () => manager.SetScreen(typeof(MapPoolScreen)));
            AddAssert("prompt was not cancelled", () => prompt.IsAlive);
            AddStep("open bracket", () => manager.SetScreen(typeof(LadderScreen)));
            AddStep("reach deadline", () => promptClock.CurrentTime += 600);
            AddUntilStep("delegate ran while console selected", () => invoked == 1);
            AddAssert("console unchanged by delegate", () => manager.ActiveScreen is LadderScreen);
            AddWaitStep("more frames", 2);
            AddAssert("delegate ran once", () => invoked == 1);
        }

        [Test]
        public void TestStageChangeCancelsAutoAdvance()
        {
            requestPrompt();
            AddStep("open console", () => manager.SetScreen(typeof(RoomSelectScreen)));
            AddStep("select different stage", () => manager.SetScreen(typeof(GameplayScreen)));
            AddStep("pass deadline", () => promptClock.CurrentTime += 2000);
            AddWaitStep("update countdown", 2);
            AddAssert("delegate cancelled", () => invoked == 0);
        }

        [Test]
        public void TestReplacingPromptAndCancellingOldInstance()
        {
            requestPrompt();
            AddStep("replace prompt", () => manager.ShowAutoAdvance(new AutoAdvancePrompt(() => invoked += 10, 1000)
            {
                Clock = new FramedClock(promptClock),
            }));
            AddWaitStep("load replacement", 2);
            AddStep("cancel old instance again", () => prompt.Cancel());
            AddStep("pass deadline", () => promptClock.CurrentTime += 2000);
            AddUntilStep("only replacement invoked", () => invoked == 10);
        }

        [Test]
        public void TestAutoAdvanceCanSwitchStage()
        {
            requestPrompt();
            AddStep("request stage transition", () => manager.ShowAutoAdvance(new AutoAdvancePrompt(
                () =>
                {
                    invoked++;
                    manager.SetScreen(typeof(GameplayScreen));
                }, 1000)
            {
                Clock = new FramedClock(promptClock),
            }));
            AddWaitStep("load transition prompt", 2);
            AddStep("pass deadline", () => promptClock.CurrentTime += 2000);
            AddUntilStep("stage switched", () => manager.ActiveStage is GameplayScreen);
            AddAssert("controls switched too", () => manager.ActiveScreen == manager.ActiveStage);
            AddWaitStep("more frames", 2);
            AddAssert("transition invoked once", () => invoked == 1);
        }

        [Test]
        public void TestShortcutsWithStageButtonsOutsideScrollView()
        {
            AddStep("shorten navigation viewport", () => manager.Height = 0.4f);
            AddStep("open room selection", () => manager.SetScreen(typeof(RoomSelectScreen)));
            AddWaitStep("allow room input to initialise", 3);
            AddStep("open bracket", () => manager.SetScreen(typeof(LadderScreen)));
            AddStep("press map pool shortcut", () => pressShortcut(InputManager, Key.M));
            AddUntilStep("map pool selected", () => manager.ActiveScreen is MapPoolScreen && manager.ActiveStage is MapPoolScreen);
            AddStep("press gameplay shortcut", () => pressShortcut(InputManager, Key.G));
            AddUntilStep("gameplay selected", () => manager.ActiveScreen is GameplayScreen && manager.ActiveStage is GameplayScreen);
            AddStep("press bracket shortcut", () => pressShortcut(InputManager, Key.B));
            AddUntilStep("bracket selected", () => manager.ActiveScreen is LadderScreen);
            AddAssert("gameplay remains active", () => manager.ActiveStage is GameplayScreen);
            AddStep("restore viewport", () => manager.Height = 1);
        }

        [Test]
        public void TestActiveStageButtonRemainsHighlighted()
        {
            AddStep("show gameplay", () => manager.SetScreen(typeof(GameplayScreen)));
            AddStep("open bracket", () => manager.SetScreen(typeof(LadderScreen)));
            AddAssert("stage highlighted", () => navigationButton("Gameplay").BackgroundColour == Color4.Gray);
            AddAssert("console highlighted", () => navigationButton("Bracket").BackgroundColour == Color4.Gray);
            AddStep("hover stage button", () => InputManager.MoveMouseTo(navigationButton("Gameplay")));
            AddStep("move away", () => InputManager.MoveMouseTo(navigationButton("Bracket")));
            AddUntilStep("stage highlight persists", () => navigationButton("Gameplay").Background.Colour == Color4.Gray);
            AddStep("switch stage", () => manager.SetScreen(typeof(MapPoolScreen)));
            AddAssert("new stage highlighted", () => navigationButton("Map Pool").BackgroundColour == Color4.Gray);
            AddAssert("old stage unselected", () => navigationButton("Gameplay").BackgroundColour != Color4.Gray);
        }

        [Test]
        public void TestShortcutsFromStageWindowInput()
        {
            AddStep("show map pool", () => manager.SetScreen(typeof(MapPoolScreen)));
            AddStep("press gameplay in stage window", () => pressShortcut(manager.StageInput, Key.G));
            AddUntilStep("gameplay selected", () => manager.ActiveScreen is GameplayScreen && manager.ActiveStage is GameplayScreen);
            AddStep("press bracket in stage window", () => pressShortcut(manager.StageInput, Key.B));
            AddUntilStep("bracket selected", () => manager.ActiveScreen is LadderScreen);
            AddAssert("stage preserved", () => manager.ActiveStage is GameplayScreen);
        }

        [Test]
        public void TestHiddenRoomSearchDoesNotCaptureShortcuts()
        {
            RoomSelectScreen roomScreen = null!;
            BasicSearchTextBox search = null!;
            int hiddenUpdates = 0;

            AddStep("open room selection", () =>
            {
                manager.SetScreen(typeof(RoomSelectScreen));
                roomScreen = (RoomSelectScreen)manager.ActiveScreen!;
            });
            AddUntilStep("search loaded", () =>
            {
                search = roomScreen.ChildrenOfType<BasicSearchTextBox>().SingleOrDefault()!;
                return search?.IsLoaded == true;
            });
            AddStep("focus room search", () => ((IFocusManager)InputManager).ChangeFocus(search));
            AddAssert("search has focus", () => search.HasFocus);
            AddStep("hide room selection", () => manager.SetScreen(typeof(MapPoolScreen)));
            AddWaitStep("allow focus contention", 3);
            AddAssert("search released focus", () => !search.HasFocus);
            AddAssert("hidden room ignores input", () => !roomScreen.PropagateNonPositionalInputSubTree && !roomScreen.PropagatePositionalInputSubTree);
            AddStep("observe background updates", () => roomScreen.OnUpdate += _ => hiddenUpdates++);
            AddWaitStep("update hidden room", 3);
            AddAssert("background processing continues", () => hiddenUpdates > 0);
            AddStep("press gameplay shortcut in console", () => pressShortcut(InputManager, Key.G));
            AddUntilStep("gameplay selected", () => manager.ActiveScreen is GameplayScreen);
            AddStep("return to room selection", () => manager.SetScreen(typeof(RoomSelectScreen)));
            AddUntilStep("room input restored", () => roomScreen.PropagateNonPositionalInputSubTree && roomScreen.PropagatePositionalInputSubTree);
            AddStep("focus search again", () => ((IFocusManager)InputManager).ChangeFocus(search));
            AddAssert("search usable again", () => search.HasFocus);
        }

        private static void pressShortcut(ManualInputManager input, Key key)
        {
            input.PressKey(key);
            input.ReleaseKey(key);
        }

        private TourneyButton navigationButton(string text)
            => manager.ChildrenOfType<TourneyButton>().Single(b => b.Text == text);

        private void requestPrompt()
        {
            AddStep("show map pool", () => manager.SetScreen(typeof(MapPoolScreen)));
            AddStep("request auto advance", () =>
            {
                invoked = 0;
                promptClock.CurrentTime = 0;
                manager.ShowAutoAdvance(prompt = new AutoAdvancePrompt(() => invoked++, 1000)
                {
                    Clock = new FramedClock(promptClock),
                });
            });
            AddUntilStep("prompt loaded", () => prompt.IsLoaded);
        }

        private partial class TestSceneManager : TournamentSceneManager
        {
            public ManualInputManager StageInput { get; private set; } = null!;

            protected override Drawable CreateStageWindow(Container content) => StageInput = new ManualInputManager
            {
                RelativeSizeAxes = Axes.Both,
                UseParentInput = false,
                ShowVisualCursorGuide = false,
                Child = content,
            };
        }
    }
}
