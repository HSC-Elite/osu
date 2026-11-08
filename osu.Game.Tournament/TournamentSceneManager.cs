// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.Screens;
using osu.Game.Tournament.Screens.Drawings;
using osu.Game.Tournament.Screens.Editors;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Ladder;
using osu.Game.Tournament.Screens.MapPool;
using osu.Game.Tournament.Screens.RoomSelect;
using osu.Game.Tournament.Screens.Schedule;
using osu.Game.Tournament.Screens.Setup;
using osu.Game.Tournament.Screens.Showcase;
using osu.Game.Tournament.Screens.TeamIntro;
using osu.Game.Tournament.Screens.TeamWin;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Tournament
{
    [Cached]
    public partial class TournamentSceneManager : CompositeDrawable
    {
        public const int CONTROL_AREA_WIDTH = 200;
        public const int STREAM_AREA_WIDTH = 1366;
        public const int STREAM_AREA_HEIGHT = (int)(STREAM_AREA_WIDTH / ASPECT_RATIO);
        public const float ASPECT_RATIO = 16 / 9f;
        public const int REQUIRED_WIDTH = CONTROL_AREA_WIDTH * 2 + STREAM_AREA_WIDTH;

        [Cached]
        private readonly TournamentMatchChatDisplay chat = new TournamentMatchChatDisplay();

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        private readonly Bindable<int> stageHeight = new Bindable<int>();
        private readonly Bindable<Type> currentScreenBindable = new Bindable<Type>();
        private readonly Dictionary<Type, TournamentScreen> screens = new Dictionary<Type, TournamentScreen>();
        private readonly Dictionary<TournamentStageScreen, Container> controlPanels = new Dictionary<TournamentStageScreen, Container>();

        private Container consoleRoot = null!;
        private Container stageRoot = null!;
        private Container stageSizeControlContainer = null!;
        private Container chatContainer = null!;
        private Container autoAdvanceContainer = null!;
        private FillFlowContainer buttons = null!;
        private TourneyVideo video = null!;
        private Drawable? consoleContent;
        private Drawable? temporaryScreen;
        private ScheduledDelegate? scheduledHide;
        private AutoAdvancePrompt? autoAdvancePrompt;
        private float depth;

        public IBindable<Type> CurrentScreen => currentScreenBindable;
        public Drawable? ActiveScreen { get; private set; }
        public TournamentStageScreen? ActiveStage { get; private set; }
        public TournamentStageScreen? PreviousStage { get; private set; }

        public TournamentSceneManager()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(TournamentConfigManager config)
        {
            config.BindWith(StorageConfig.StageHeight, stageHeight);

            foreach (var screen in new TournamentScreen[]
                     {
                         new SetupScreen(), new RoomSelectScreen(), new LadderScreen(), new LadderEditorScreen(),
                         new TeamEditorScreen(), new ModColorEditorScreen(), new ModMultiplierSettingsEditorScreen(),
                         new RoundEditorScreen(), new OsuGameScreen(),
                         new ScheduleScreen(), new TeamIntroScreen(), new SeedingScreen(), new MapPoolScreen(),
                         new GameplayScreen(), new TeamWinScreen(), new DrawingsScreen(), new ShowcaseScreen(),
                     })
                screens.Add(screen.GetType(), screen);

            InternalChild = new RefCountedBackbufferProvider
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = CONTROL_AREA_WIDTH },
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.08f) },
                            consoleRoot = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                ChildrenEnumerable = screens.Values.Where(s => s is not TournamentStageScreen),
                            },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = CONTROL_AREA_WIDTH,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black },
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                RowDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.AutoSize) },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new OsuScrollContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            ScrollbarVisible = false,
                                            Child = buttons = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(5),
                                                Padding = new MarginPadding(5),
                                                Children = new Drawable[]
                                                {
                                                    heading("Console"),
                                                    button<SetupScreen>("Setup"),
                                                    button<RoomSelectScreen>("Select Room"),
                                                    button<LadderScreen>("Bracket", Key.B),
                                                    button<TeamEditorScreen>("Team Editor"),
                                                    button<ModColorEditorScreen>("Mod Color Editor"),
                                                    button<ModMultiplierSettingsEditorScreen>("Mod Multiplier Editor"),
                                                    button<RoundEditorScreen>("Rounds Editor"),
                                                    button<LadderEditorScreen>("Bracket Editor"),
                                                    button<OsuGameScreen>("Osu"),
                                                    heading("Stage"),
                                                    button<ScheduleScreen>("Schedule", Key.S),
                                                    button<TeamIntroScreen>("Team Intro", Key.I),
                                                    button<SeedingScreen>("Seeding", Key.D),
                                                    button<MapPoolScreen>("Map Pool", Key.M),
                                                    button<GameplayScreen>("Gameplay", Key.G),
                                                    button<TeamWinScreen>("Win", Key.W),
                                                    button<DrawingsScreen>("Drawings"),
                                                    button<ShowcaseScreen>("Showcase"),
                                                },
                                            },
                                        },
                                    },
                                    new Drawable[]
                                    {
                                        autoAdvanceContainer = new Container
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Padding = new MarginPadding { Horizontal = 5 },
                                        },
                                    },
                                },
                            },
                        },
                    },
                    CreateStageWindow(stageSizeControlContainer = new Container
                    {
                        Child = new DrawSizePreservingFillContainer
                        {
                            Children = new Drawable[]
                            {
                                video = new TourneyVideo("main", true) { RelativeSizeAxes = Axes.Both, Loop = true },
                                stageRoot = new ShortcutContainer(handleShortcut)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    ChildrenEnumerable = screens.Values.OfType<TournamentStageScreen>(),
                                },
                                chatContainer = new Container
                                {
                                    Alpha = 0,
                                    Position = new Vector2(332, -142),
                                    Width = 700,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.TopLeft,
                                    Child = chat,
                                },
                            },
                        },
                    }),
                },
            };

            foreach (var screen in screens.Values)
                screen.Hide();

            stageHeight.BindValueChanged(h => stageSizeControlContainer.Size = new Vector2((int)(h.NewValue * ASPECT_RATIO), h.NewValue), true);
        }

        // Tests can host the same two trees without creating a native window.
        protected virtual Drawable CreateStageWindow(Container content) => new ExternalWindowContainer
        {
            AutoSizeAxes = Axes.Both,
            Depth = float.MaxValue,
            WindowTitle = "Tournament Stage Window",
            Child = content,
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Screen dependency loaders have now populated their detached control panels.
            foreach (var screen in screens.Values.OfType<TournamentStageScreen>())
            {
                var panelContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    Child = screen.ControlPanel,
                };
                controlPanels.Add(screen, panelContainer);
                consoleRoot.Add(panelContainer);
            }

            SetScreen(typeof(SetupScreen));
        }

        private static Drawable heading(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.Default.With(size: 20, weight: FontWeight.Bold),
            Margin = new MarginPadding { Top = 15, Bottom = 5 },
        };

        private ScreenButton button<T>(string text, Key? shortcut = null) where T : TournamentScreen
            => new ScreenButton(typeof(T), shortcut) { Text = text, RequestSelection = SetScreen };

        public void SetScreen(Drawable screen)
        {
            hideConsoleContent();
            temporaryScreen?.Expire();
            consoleRoot.Add(temporaryScreen = screen);
            consoleContent = screen;
            ActiveScreen = screen;
            currentScreenBindable.Value = screen.GetType();
            screen.Show();
            updateButtons();
        }

        public void SetScreen(Type screenType)
        {
            if (!screens.TryGetValue(screenType, out var target) || ActiveScreen == target)
                return;

            hideConsoleContent();
            temporaryScreen?.Expire();
            temporaryScreen = null;

            if (target is TournamentStageScreen stage)
            {
                switchStage(stage);
                consoleContent = controlPanels[stage];
            }
            else
                consoleContent = target;

            ActiveScreen = target;
            currentScreenBindable.Value = screenType;
            consoleRoot.ChangeChildDepth(consoleContent, depth--);
            consoleContent.Show();
            updateButtons();
        }

        private void hideConsoleContent()
        {
            if (consoleContent is TournamentScreen screen)
                screen.Hide();
            else
                consoleContent?.FadeOut(TournamentScreen.FADE_DELAY);
        }

        private void switchStage(TournamentStageScreen target)
        {
            if (ActiveStage == target)
                return;

            autoAdvancePrompt?.Cancel();

            // Finish any previous delayed exit before starting a new transition.
            if (scheduledHide?.Completed == false)
            {
                scheduledHide.RunTask();
                scheduledHide.Cancel();
            }

            scheduledHide = null;

            var previous = ActiveStage;
            PreviousStage = previous;
            previous?.IsStageActive = false;
            ActiveStage = target;
            target.IsStageActive = true;

            if (target.ChildrenOfType<TourneyVideo>().Any(v => v.VideoAvailable))
            {
                video.FadeOut(TournamentScreen.FADE_DELAY);
                scheduledHide = Scheduler.AddDelayed(() => previous?.Hide(), TournamentScreen.FADE_DELAY);
            }
            else
            {
                previous?.Hide();
                video.Show();
            }

            stageRoot.ChangeChildDepth(target, depth--);
            target.Show();

            if (target is MapPoolScreen or GameplayScreen)
            {
                chatContainer.FadeIn(TournamentScreen.FADE_DELAY);
                chatContainer.ResizeHeightTo(82, TournamentScreen.FADE_DELAY, Easing.In);
            }
            else
                chatContainer.FadeOut(TournamentScreen.FADE_DELAY);
        }

        /// <summary>
        /// Displays a caller-owned prompt independently of the selected console page.
        /// A change of stage cancels the current prompt.
        /// </summary>
        public void ShowAutoAdvance(AutoAdvancePrompt prompt)
        {
            autoAdvancePrompt?.Cancel();
            autoAdvanceContainer.Clear();
            autoAdvanceContainer.Add(autoAdvancePrompt = prompt);
        }

        private void updateButtons()
        {
            foreach (var button in buttons.OfType<ScreenButton>())
                button.IsSelected = button.Type == ActiveScreen?.GetType() || button.Type == ActiveStage?.GetType();
        }

        protected override bool OnKeyDown(KeyDownEvent e) => handleShortcut(e) || base.OnKeyDown(e);

        private bool handleShortcut(KeyDownEvent e)
        {
            var button = buttons.OfType<ScreenButton>().FirstOrDefault(b => b.ShortcutKey == e.Key);
            if (button == null)
                return false;

            SetScreen(button.Type);
            return true;
        }

        // The external window has its own input manager, so its unhandled shortcuts
        // must be routed from inside that tree as well as from the console tree.
        private partial class ShortcutContainer : Container
        {
            private readonly Func<KeyDownEvent, bool> handleShortcut;

            public ShortcutContainer(Func<KeyDownEvent, bool> handleShortcut)
            {
                this.handleShortcut = handleShortcut;
            }

            protected override bool OnKeyDown(KeyDownEvent e) => handleShortcut(e) || base.OnKeyDown(e);
        }

        private partial class ScreenButton : TourneyButton
        {
            public readonly Type Type;

            public Key? ShortcutKey { get; }

            public ScreenButton(Type type, Key? shortcutKey = null)
            {
                ShortcutKey = shortcutKey;

                Type = type;

                BackgroundColour = OsuColour.Gray(0.2f);
                Action = () => RequestSelection?.Invoke(type);

                RelativeSizeAxes = Axes.X;

                if (shortcutKey != null)
                {
                    Add(new CircularContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(24),
                        Margin = new MarginPadding(5),
                        Masking = true,
                        Alpha = 0.5f,
                        Blending = BlendingParameters.Additive,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                Colour = OsuColour.Gray(0.1f),
                                RelativeSizeAxes = Axes.Both,
                            },
                            new OsuSpriteText
                            {
                                Font = OsuFont.Default.With(size: 24),
                                Y = -2,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = shortcutKey.Value.ToString(),
                            }
                        }
                    });
                }
            }

            private bool isSelected;

            public Action<Type>? RequestSelection;

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value)
                        return;

                    isSelected = value;
                    BackgroundColour = value ? Color4.Gray : OsuColour.Gray(0.2f);
                }
            }
        }
    }
}
