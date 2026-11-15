// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components.RoundInformation;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    public partial class RoundInformationPreview : CompositeDrawable
    {
        [Resolved]
        protected LadderInfo LadderInfo { get; private set; } = null!;

        protected readonly FillFlowContainer MapContentContainer;
        protected readonly TournamentSpriteText MapCountText;
        private static readonly Color4 boarder_color = Color4Extensions.FromHex("#808080");

        private BanPickFlowSubscription? flowSubscription;

        private const float cover_width = 50f;

        protected readonly Bindable<TournamentMatch?> CurrentMatch = new Bindable<TournamentMatch?>();

        public RoundInformationPreview()
        {
            AutoSizeAxes = Axes.X;
            Height = 155;
            Masking = true;
            CornerRadius = 10;

            InternalChildren = new Drawable[]
            {
                new BackdropBlurContainer
                {
                    BorderColour = boarder_color,
                    BorderThickness = 2f,
                    RelativeSizeAxes = Axes.Both,
                    BlurSigma = new Vector2(10f),
                    CornerRadius = 10,
                    Masking = true,
                    Child = new Box
                    {
                        Name = "backgroud",
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.25f,
                    },
                },
                new Container
                {
                    Name = "右侧主要内容",
                    Width = 1000f,
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        new BufferedContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                MapContentContainer = new FillFlowContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(8f, 0)
                                },
                                new Container
                                {
                                    Name = "cover",
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    RelativeSizeAxes = Axes.Both,
                                    Blending = new BlendingParameters
                                    {
                                        // Don't change the destination colour.
                                        RGBEquation = BlendingEquation.Add,
                                        Source = BlendingType.Zero,
                                        Destination = BlendingType.One,
                                        // Subtract the cover's alpha from the destination (points with alpha 1 should make the destination completely transparent).
                                        AlphaEquation = BlendingEquation.Add,
                                        SourceAlpha = BlendingType.Zero,
                                        DestinationAlpha = BlendingType.OneMinusSrcAlpha
                                    },
                                    Children = new Drawable[]
                                    {
                                        new Box
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            RelativeSizeAxes = Axes.Y,
                                            Width = cover_width,
                                            Colour = ColourInfo.GradientHorizontal(
                                                Color4.White.Opacity(1f),
                                                Color4.White.Opacity(0f)
                                            )
                                        },
                                        new Box
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            RelativeSizeAxes = Axes.Y,
                                            Width = cover_width,
                                            Colour = ColourInfo.GradientHorizontal(
                                                Color4.White.Opacity(0f),
                                                Color4.White.Opacity(1f)
                                            )
                                        },
                                    }
                                }
                            }
                        },
                        MapCountText = new TournamentSpriteText
                        {
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre,
                            Font = OsuFont.Torus.With(size: 12),
                            Colour = Color4Extensions.FromHex("#E5E5E5"),
                            Margin = new MarginPadding(5)
                        }
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            CurrentMatch.BindTo(LadderInfo.CurrentMatch);
            CurrentMatch.BindValueChanged(matchChanged, true);
        }

        private void matchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            if (match.OldValue != null)
            {
                match.OldValue.PicksBans.CollectionChanged -= picksBansOnCollectionChanged;
                match.OldValue.Round.ValueChanged -= roundChanged;
                match.OldValue.FirstPickTeam.ValueChanged -= firstTeamChanged;
            }

            if (match.NewValue != null)
            {
                match.NewValue.PicksBans.CollectionChanged += picksBansOnCollectionChanged;
                match.NewValue.Round.ValueChanged += roundChanged;
                match.NewValue.FirstPickTeam.ValueChanged += firstTeamChanged;
            }

            bindRound();
        }

        private void roundChanged(ValueChangedEvent<TournamentRound?> _) => bindRound();
        private void firstTeamChanged(ValueChangedEvent<TeamColour> _) => Scheduler.AddOnce(updateState);

        private void bindRound()
        {
            flowSubscription?.Dispose();
            flowSubscription = CurrentMatch.Value?.Round.Value is TournamentRound round
                ? new BanPickFlowSubscription(round, () => Scheduler.AddOnce(updateState))
                : null;
            Scheduler.AddOnce(updateState);
        }

        private void picksBansOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Scheduler.AddOnce(updateState);

        private void updateState()
        {
            MapContentContainer.Clear();
            MapCountText.Text = string.Empty;

            if (CurrentMatch.Value?.Round.Value == null)
                return;

            UpdateContent();
            UpdateMapCount();
            SetAnimation();
        }

        protected virtual void UpdateContent()
        {
            if (CurrentMatch.Value?.Round.Value == null)
                return;

            var match = CurrentMatch.Value;
            var round = match.Round.Value;
            var slots = BanPickFlow.Resolve(round, match.PicksBans, match.FirstPickTeam.Value);

            foreach (var group in round.BanPickFlowGroups)
            {
                if (group.TotalStep == 0)
                    continue;
                if (MapContentContainer.Count > 0)
                    MapContentContainer.Add(createDivideLine());
                MapContentContainer.Add(new BanPickStageDisplay(group.Name.Value, slots.Where(s => s.Group == group)));
            }

            var assigned = slots.Where(s => s.Choice != null).Select(s => s.Choice).ToHashSet();
            var extra = match.PicksBans.Where(c => !assigned.Contains(c) && (c.Type != ChoiceType.TB || !round.SeparateTiebreaker.Value)).ToArray();

            if (extra.Length > 0)
            {
                var manualGroup = new BanPickFlowGroup { Name = { Value = "手动操作" } };
                MapContentContainer.Add(createDivideLine());
                MapContentContainer.Add(new BanPickStageDisplay("手动操作", extra.Select(c => new BanPickFlowSlot(-1, manualGroup,
                    new BanPickFlowStep { CurrentAction = { Value = c.Type } }) { Choice = c })));
            }

            var tbMap = round.Beatmaps.FirstOrDefault(map => map.Mods == "TB");

            if (round.SeparateTiebreaker.Value && !slots.Any(s => s.Step.CurrentAction.Value == ChoiceType.TB) && tbMap != null)
            {
                MapContentContainer.Add(createDivideLine());
                MapContentContainer.Add(new TbMapBox(tbMap) { Margin = new MarginPadding { Top = 22 } });
            }
        }

        protected virtual void UpdateMapCount()
        {
            var match = CurrentMatch.Value;
            var round = match?.Round.Value;

            if (match == null || round == null)
                return;

            int mapCount = round.Beatmaps.Count;
            int remainMapCount = mapCount - match.PicksBans.Count(p => p.IsConsumed());

            MapCountText.Text = $"图池内谱面数量：{mapCount}  |  图池内剩余谱面：{remainMapCount}";
        }

        protected virtual void SetAnimation()
        {
            Scheduler.Add(() =>
            {
                FinishTransforms();

                if (MapContentContainer.DrawWidth < 1000)
                {
                    MapContentContainer.Anchor = Anchor.TopCentre;
                    MapContentContainer.Origin = Anchor.TopCentre;
                    MapContentContainer.X = 0;
                    return;
                }

                MapContentContainer.Anchor = Anchor.TopLeft;
                MapContentContainer.Origin = Anchor.TopLeft;

                // 每秒走50px
                double timeToRepeat = (MapContentContainer.DrawWidth - 1000 + cover_width) / 30f * 1000;
                MapContentContainer.X = cover_width;
                MapContentContainer.MoveToX((1000 - cover_width - MapContentContainer.DrawWidth), timeToRepeat, Easing.OutSine).Then(5000).MoveToX(cover_width, timeToRepeat, Easing.OutSine).Then(5000)
                                   .Loop();
            });
        }

        private Box createDivideLine() => new Box
        {
            Colour = Color4Extensions.FromHex("#E5E5E5"),
            Height = 38.5f,
            Width = 2f,
            Margin = new MarginPadding
            {
                Top = 59f
            }
        };

        protected override void Dispose(bool isDisposing)
        {
            flowSubscription?.Dispose();

            if (CurrentMatch.Value != null)
            {
                CurrentMatch.Value.PicksBans.CollectionChanged -= picksBansOnCollectionChanged;
                CurrentMatch.Value.Round.ValueChanged -= roundChanged;
                CurrentMatch.Value.FirstPickTeam.ValueChanged -= firstTeamChanged;
            }

            base.Dispose(isDisposing);
        }
    }
}
