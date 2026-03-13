// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.Multiplayer;
using osu.Game.Overlays.Settings;
using osu.Game.Screens;
using osu.Game.Screens.Play.PlayerSettings;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components;
using osu.Game.Tournament.Screens.Gameplay.Components.MatchHeader;
using osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea;
using osu.Game.Tournament.Screens.MapPool;
using osu.Game.Tournament.Screens.TeamWin;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay
{
    public partial class GameplayScreen : BeatmapInfoScreen
    {
        private readonly BindableBool warmup = new BindableBool();

        public readonly Bindable<TourneyState> State = new Bindable<TourneyState>();
        private OsuButton warmupButton = null!;
        private Sprite slotSprite = null!;

        private PlayerArea redArea = null!;
        private PlayerArea blueArea = null!;

        private MatchHeader header = null!;
        private RoundInformationPreview roundPreview = null!;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        [Resolved]
        private TextureStore textures { get; set; } = null!;

        [Resolved]
        private TournamentMatchChatDisplay chat { get; set; } = null!;

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        public bool Playing => chroma.CurrentScreen is TournamentMultiSpectatorScreen;

        private ScreenStack chroma = null!;

        protected override SongBar CreateSongBar() => new GameplaySongBar
        {
            Depth = float.MinValue,
        };

        private LabelledTextBox chatBox = null!;

        protected override bool FetchDataFromMemoryThisScreen => true;

        private GameplaySongBar gameplaySongBar => (GameplaySongBar)SongBar;

        protected override bool ShowLogo => true;

        private bool switchFromMappool;

        [BackgroundDependencyLoader]
        private void load(TextureStore store)
        {
            AddRangeInternal(new Drawable[]
            {
                new TourneyVideo("gameplay")
                {
                    Loop = true,
                    RelativeSizeAxes = Axes.Both,
                },
                new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Texture = store.Get("Videos/gameplay"),
                    FillMode = FillMode.Fit,
                },
                header = new MatchHeader(),
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Y = 110,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Children = new[]
                    {
                        chroma = new OsuScreenStack
                        {
                            RelativeSizeAxes = Axes.None,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Height = 512,
                            Width = 1366,
                        },
                    }
                },
                scoreDisplay = new TournamentMatchScoreDisplay
                {
                    Y = -147,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.TopCentre,
                },
                new Container
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Size = new Vector2(100, 50),
                    Margin = new MarginPadding { Left = 10f, Bottom = 7f },
                    Child = slotSprite = new Sprite
                    {
                        Alpha = 0,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        FillMode = FillMode.Fit,
                        RelativeSizeAxes = Axes.Both
                    }
                },
                roundPreview = new RoundInformationPreview
                {
                    Alpha = 0f,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Margin = new MarginPadding(13)
                }
            });

            ControlPanel.AddRange(new Drawable[]
            {
                warmupButton = new TourneyButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = "Toggle warmup",
                    Action = () => warmup.Toggle()
                },
                new TourneyButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = "Toggle chat",
                    Action = () => { State.Value = State.Value == TourneyState.Idle ? TourneyState.Playing : TourneyState.Idle; }
                },
                new SettingsSlider<int>
                {
                    LabelText = $"{(OperatingSystem.IsWindows() ? "Player Area" : "Chroma")} width",
                    Current = LadderInfo.ChromaKeyWidth,
                    KeyboardStep = 1,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Master Volume",
                    Current = audio.Volume
                },
                new SettingsSlider<double>
                {
                    LabelText = "Track Volume",
                    Current = audio.VolumeTrack
                },
                new SettingsSlider<double>
                {
                    LabelText = "Sample Volume",
                    Current = audio.VolumeSample
                },
                new SettingsSlider<int>
                {
                    LabelText = "Players per team",
                    Current = LadderInfo.PlayersPerTeam,
                    KeyboardStep = 1,
                },
                new ControlPanel.Spacer(),
                new SettingsSlider<double>
                {
                    LabelText = "Master Volume",
                    Current = audio.Volume
                },
                new SettingsSlider<double>
                {
                    LabelText = "Track Volume",
                    Current = audio.VolumeTrack
                },
                new SettingsSlider<double>
                {
                    LabelText = "Sample Volume",
                    Current = audio.VolumeSample
                },
                new ControlPanel.Spacer(),
                new MatchRoundNameTextBox
                {
                    RelativeSizeAxes = Axes.X,
                },
                new TourneyButton
                {
                    Text = "红飞",
                    Action = redArea.Launch
                },
                new TourneyButton
                {
                    Text = "蓝飞",
                    Action = blueArea.Launch
                },
                new TourneyButton
                {
                    Text = "飞重置",
                    Action = () =>
                    {
                        redArea.Reset();
                        blueArea.Reset();
                    }
                },
                new TourneyButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = "Toggle map detail",
                    Action = () =>
                    {
                        if (roundPreviewShow)
                        {
                            HideRoundPreview();
                        }
                        else
                        {
                            ShowRoundPreview();
                        }
                    }
                },
                chatBox = new LabelledTextBox
                {
                    Label = "enter to chat",
                },
                new TourneyButton
                {
                    Text = "强制刷新聊天区域",
                    Action = () => chat.UpdateChat()
                },
                new TourneyButton
                {
                    Text = "force spect",
                    Action = () =>
                    {
                        IPC.State.Value = TourneyState.WaitingForClients;
                        updateState();
                    }
                },
                new TourneyButton
                {
                    Text = "panic",
                    Action = () =>
                    {
                        if (chroma.CurrentScreen is IdleScreen)
                            return;

                        chroma.Exit();
                    }
                },
                new VisualSettings
                {
                    Scale = new Vector2(0.7f)
                }
            });

            LadderInfo.ChromaKeyWidth.BindValueChanged(width => chroma.Width = width.NewValue, true);

            warmup.BindValueChanged(w =>
            {
                warmupButton.Alpha = !w.NewValue ? 0.5f : 1;
                header.ShowScores = !w.NewValue;
            }, true);

            sceneManager?.CurrentScreen.BindValueChanged(s =>
            {
                if (s.OldValue == typeof(MapPoolScreen) && s.NewValue == typeof(GameplayScreen))
                    switchFromMappool = true;
            });

            chroma.Push(new IdleScreen());

            chatBox.OnCommit += (_, _) =>
            {
                chat.PostMessage(chatBox.Text);
                chatBox.Text = string.Empty;
            };
        }

        private bool roundPreviewShow;

        public bool ShowRoundPreview()
        {
            if (!IsLoaded)
                return false;

            scheduledShowRoundPreview?.Cancel();

            if (!LadderInfo.EnableRoundPreview.Value)
                return false;

            if (roundPreviewShow)
                return false;

            if (!IsPresent)
                return false;

            if (State.Value != TourneyState.Idle && State.Value != TourneyState.Ranking)
                return false;

            SongBar.FadeOut(100);
            chat.Contract();

            using (roundPreview.BeginDelayedSequence(200))
                roundPreview.FadeIn(200);

            roundPreviewShow = true;
            return true;
        }

        public void HideRoundPreview()
        {
            if (!roundPreviewShow)
                return;

            scheduledHideRoundPreview?.Cancel();

            roundPreview.FadeOut(100);

            using (SongBar.BeginDelayedSequence(200))
                SongBar.FadeIn(200);

            roundPreviewShow = false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            State.BindTo(IPC.State);
            State.BindValueChanged(_ => updateState(), true);
            LadderInfo.InvertScoreColour.BindValueChanged(v => scoreDisplay.InvertTextColor = v.NewValue, true);
        }

        protected override void SetModAcronym(string acronym)
        {
            var texture = textures.Get($"Slots/{acronym}");

            if (texture == null)
                slotSprite.FadeOut(500, Easing.Out);
            else
            {
                slotSprite.Texture = texture;
                slotSprite.FadeInFromZero(500, Easing.Out);
            }
        }

        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);

            if (match.NewValue == null)
                return;

            warmup.Value = match.NewValue.Team1Score.Value + match.NewValue.Team2Score.Value == 0;
            scheduledScreenChange?.Cancel();
        }

        private AutoAdvancePrompt? scheduledScreenChange;
        private ScheduledDelegate? scheduledContract;
        private ScheduledDelegate? scheduledShowRoundPreview;
        private ScheduledDelegate? scheduledHideRoundPreview;

        private TournamentMatchScoreDisplay scoreDisplay = null!;

        private TourneyState lastState;

        private void contract()
        {
            if (!IsLoaded)
                return;

            scheduledContract?.Cancel();

            gameplaySongBar.Expanded = false;
            scoreDisplay.FadeOut(100);
            using (chat.BeginDelayedSequence(500))
                chat.Expand();
        }

        private void expand()
        {
            if (!IsLoaded)
                return;

            scheduledContract?.Cancel();

            chat.Contract();
            gameplaySongBar.Expanded = true;

            using (BeginDelayedSequence(300))
            {
                scoreDisplay.FadeIn(100);
            }
        }

        private void updateState()
        {
            try
            {
                scheduledScreenChange?.Cancel();

                if (State.Value == TourneyState.Ranking)
                {
                    if (warmup.Value || CurrentMatch.Value == null) return;

                    var lastPick = CurrentMatch.Value.PicksBans.LastOrDefault(p => p.Type == ChoiceType.Pick && p.BeatmapID == IPC.Beatmap.Value?.OnlineID);

                    if (lastPick?.Winner.Value != null)
                        return;

                    if (IPC.Score1.Value > IPC.Score2.Value)
                    {
                        CurrentMatch.Value.Team1Score.Value++;
                        if (lastPick != null) lastPick.Winner.Value = TeamColour.Red;
                    }
                    else
                    {
                        CurrentMatch.Value.Team2Score.Value++;
                        if (lastPick != null) lastPick.Winner.Value = TeamColour.Blue;
                    }
                }

                switch (State.Value)
                {
                    case TourneyState.Idle:
                        if (Playing)
                        {
                            chroma.Exit();
                        }

                        contract();

                        if (LadderInfo.AutoProgressScreens.Value)
                        {
                            const float delay_before_progression = 4000;

                            // if we've returned to idle and the last screen was ranking
                            // we should automatically proceed after a short delay
                            if (lastState == TourneyState.Ranking && !warmup.Value)
                            {
                                if (CurrentMatch.Value?.Completed.Value == true)
                                    scheduledScreenChange = new AutoAdvancePrompt(() => { sceneManager?.SetScreen(typeof(TeamWinScreen)); }, delay_before_progression);
                                else if (CurrentMatch.Value?.Completed.Value == false)
                                    scheduledScreenChange = new AutoAdvancePrompt(() => { sceneManager?.SetScreen(typeof(MapPoolScreen)); }, delay_before_progression);

                                if (scheduledScreenChange != null)
                                    ControlPanel.Add(scheduledScreenChange);
                            }
                        }

                        break;

                    case TourneyState.Ranking:
                        scheduledContract = Scheduler.AddDelayed(contract, 10000);
                        break;

                    case TourneyState.WaitingForClients:
                        if (client.Room == null || chroma.CurrentScreen is TournamentMultiSpectatorScreen)
                            break;

                        int[] userIds = client.CurrentMatchPlayingUserIds.ToArray();
                        MultiplayerRoomUser[] users = userIds.Select(id => client.Room.Users.First(u => u.UserID == id)).ToArray();
                        Logger.Log($"start spec {users}");

                        if (userIds.Length == 0)
                            break;

                        chroma.Push(new TournamentMultiSpectatorScreen(users));
                        break;

                    default:
                        if (roundPreviewShow)
                        {
                            HideRoundPreview();
                        }

                        expand();
                        break;
                }
            }
            finally
            {
                lastState = State.Value;
            }
        }

        public override void Hide()
        {
            if (roundPreviewShow)
            {
                HideRoundPreview();
            }

            scheduledScreenChange?.Cancel();
            scheduledShowRoundPreview?.Cancel();
            base.Hide();
        }

        public override void Show()
        {
            updateState();

            if (switchFromMappool)
            {
                scheduledShowRoundPreview = Scheduler.AddDelayed(() =>
                {
                    if (ShowRoundPreview())
                        scheduledHideRoundPreview = Scheduler.AddDelayed(HideRoundPreview, 30000);
                }, 5000);
            }

            base.Show();
        }

        private partial class PlayerArea : CompositeDrawable
        {
            [Resolved]
            private LadderInfo ladder { get; set; } = null!;

            private readonly TeamColour teamColour;
            private Container? loseTextContainer;

            private ScheduledDelegate? textCrazyScheduled;

            public PlayerArea(TeamColour teamColour)
            {
                this.teamColour = teamColour;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                ladder.PlayersPerTeam.BindValueChanged(performLayout, true);
            }

            public void Launch()
            {
                textCrazyScheduled?.Cancel();
                double delayTime = 0;
                bool clockWise = true;

                foreach (var player in InternalChildren.OfType<PlayerWindow>())
                {
                    using (player.BeginDelayedSequence(delayTime))
                    {
                        player.FlyingLaunch(clockWise);
                    }

                    delayTime += 300;
                    clockWise = !clockWise;
                }

                loseTextContainer?.RotateTo(150).Delay(delayTime + 1200).FadeIn(1000).RotateTo(0, 1000, Easing.OutCubic).Then()
                                 .Schedule(() =>
                                 {
                                     textCrazyScheduled = Scheduler.AddDelayed(() =>
                                     {
                                         loseTextContainer.MoveTo(new Vector2(RNG.NextSingle(-20, 20), RNG.NextSingle(-20, 20)), 50);
                                     }, 50, true);
                                 });
            }

            public void Reset()
            {
                textCrazyScheduled?.Cancel();
                loseTextContainer?.MoveTo(new Vector2(0));
                double delayTime = 0;

                foreach (var player in InternalChildren.OfType<PlayerWindow>())
                {
                    using (player.BeginDelayedSequence(delayTime))
                    {
                        player.Reset();
                    }

                    delayTime += 300;
                }

                loseTextContainer?.Delay(delayTime + 1200).FadeOut(300);
            }

            private void performLayout(ValueChangedEvent<int> playerCount)
            {
                if (!OperatingSystem.IsWindows())
                {
                    switch (playerCount.NewValue)
                    {
                        case 3:
                            InternalChildren = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Colour = Color4.Green,
                                },
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Height = 0.5f,
                                    Colour = Color4.Green,
                                },
                            };
                            break;

                        default:
                            InternalChild = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.Green,
                            };
                            break;
                    }
                }
                else
                {
                    int clientIndex = teamColour == TeamColour.Red ? 0 : playerCount.NewValue;

                    switch (playerCount.NewValue)
                    {
                        case 1:
                            InternalChildren = new Drawable[]
                            {
                                new PlayerWindow(clientIndex)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    RelativeSizeAxes = Axes.Both,
                                }
                            };
                            break;

                        case 2:
                            InternalChildren = new Drawable[]
                            {
                                new PlayerWindow(clientIndex++)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.25f, 0.5f),
                                    Origin = Anchor.Centre,
                                },
                                new PlayerWindow(clientIndex)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.75f, 0.5f),
                                    Origin = Anchor.Centre,
                                }
                            };
                            break;

                        case 3:
                            InternalChildren = new Drawable[]
                            {
                                new PlayerWindow(clientIndex++)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.5f, 0.25f),
                                    Origin = Anchor.Centre,
                                },
                                new PlayerWindow(clientIndex++)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.25f, 0.75f),
                                    Origin = Anchor.Centre,
                                },
                                new PlayerWindow(clientIndex)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.75f, 0.75f),
                                    Origin = Anchor.Centre,
                                },
                            };
                            break;

                        case 4:
                            InternalChildren = new Drawable[]
                            {
                                new PlayerWindow(clientIndex++)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.25f, 0.25f),
                                    Origin = Anchor.Centre,
                                },
                                new PlayerWindow(clientIndex++)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.75f, 0.25f),
                                    Origin = Anchor.Centre,
                                },
                                new PlayerWindow(clientIndex++)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.25f, 0.75f),
                                    Origin = Anchor.Centre,
                                },
                                new PlayerWindow(clientIndex)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                    Height = 0.5f,
                                    RelativeAnchorPosition = new Vector2(0.75f, 0.75f),
                                    Origin = Anchor.Centre,
                                },
                            };
                            break;

                        default:
                            throw new ArgumentException("Not Support this player count");
                    }
                }

                AddInternal(loseTextSprite());
            }

            private Container loseTextSprite() => loseTextContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new TournamentSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.Torus.With(size: 187, weight: FontWeight.Bold),
                        Text = "输了...",
                        Colour = TournamentGame.GetTeamColour(teamColour)
                    },
                    new TournamentSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.Torus.With(size: 175, weight: FontWeight.Bold),
                        Text = "输了...",
                    },
                }
            };
        }
    }
}
