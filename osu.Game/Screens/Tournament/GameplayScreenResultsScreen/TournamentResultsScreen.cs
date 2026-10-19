// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Tournament.GameplayScreenResultsScreen
{
    [Cached(typeof(TournamentResultsScreen))]
    public partial class TournamentResultsScreen : ResultsScreen
    {
        /// <summary>
        /// Whether the score card should place its secondary information on the opposite side.
        /// Text remains upright; this only mirrors the layout used by the two tournament windows.
        /// </summary>
        public bool Mirrored { get; init; }

        /// <summary>
        /// Whether to hide the bottom panel containing the replay button.
        /// </summary>
        public bool HideBottomPanel
        {
            get => hideBottomPanel;
            set
            {
                hideBottomPanel = value;

                if (bottomPanel != null)
                {
                    bottomPanel.Alpha = value ? 0 : 1;
                    bottomPanel.Height = value ? 0 : TwoLayerButton.SIZE_EXTENDED.Y;
                }
            }
        }

        private bool hideBottomPanel;
        private Container? bottomPanel;

        protected override bool UseDefaultResultsLayout => false;

        public TournamentResultsScreen(ScoreInfo? score, bool mirrored = false)
            : base(score)
        {
            Mirrored = mirrored;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var buttons = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(5),
                Direction = FillDirection.Horizontal,
            };

            if (AllowWatchingReplay)
            {
                buttons.Add(new ReplayDownloadButton(SelectedScore.Value)
                {
                    Score = { BindTarget = SelectedScore },
                    Width = 300,
                });
            }

            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.AutoSize) },
                Content = new[]
                {
                    new Drawable[] { new TournamentResultsContent(Score, Mirrored) },
                    new Drawable[]
                    {
                        bottomPanel = new Container
                        {
                            Name = "Bottom panel",
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = HideBottomPanel ? 0 : TwoLayerButton.SIZE_EXTENDED.Y,
                            Alpha = HideBottomPanel ? 0 : 1,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = Color4Extensions.FromHex("#333"),
                                },
                                buttons,
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Returns the placement to display for a player in the shared leaderboard.
        /// </summary>
        protected virtual int? GetPlacement(ScoreInfo score, IReadOnlyList<GameplayLeaderboardScore> scores)
            => scores.FirstOrDefault(s => s.User.OnlineID == score.UserID)?.Position.Value;

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250);
        }

        private partial class TournamentResultsContent : CompositeDrawable
        {
            public TournamentResultsContent(ScoreInfo? score, bool mirrored)
            {
                RelativeSizeAxes = Axes.Both;

                InternalChild = new DrawSizePreservingFillContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    TargetDrawSize = new Vector2(400, 300),
                    Child = new ResultCard(score, mirrored)
                };
            }
        }

        private partial class ResultCard : CompositeDrawable
        {
            private const float circle_size = 176;
            private const float mods_width = 40;

            private readonly ScoreInfo? score;
            private readonly bool mirrored;

            [Resolved]
            private IGameplayLeaderboardProvider? leaderboard { get; set; }

            private readonly IBindableList<GameplayLeaderboardScore> leaderboardScores = new BindableList<GameplayLeaderboardScore>();
            private readonly Bindable<int?> placement = new Bindable<int?>();
            private readonly List<IBindable<int?>> trackedPositions = new List<IBindable<int?>>();

            [Resolved]
            private TournamentResultsScreen resultsScreen { get; set; } = null!;

            private Container placementBadge = null!;
            private OsuSpriteText placementText = null!;
            private OsuSpriteText performanceText = null!;
            private AccuracyCircle accuracyCircle = null!;
            private Box scoreStripBackground = null!;
            private GridContainer scoreStripText = null!;
            private ScoreRank? headerRank;
            private CancellationTokenSource? performanceCancellation;

            public ResultCard(ScoreInfo? score, bool mirrored)
            {
                this.score = score;
                this.mirrored = mirrored;

                Anchor = Anchor.Centre;
                Origin = Anchor.Centre;
                Size = new Vector2(400, 300);
            }

            [BackgroundDependencyLoader]
            private void load(BeatmapDifficultyCache difficultyCache, CancellationToken? cancellationToken)
            {
                if (score == null)
                {
                    InternalChild = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "No score",
                        Font = OsuFont.Torus.With(size: 20),
                    };
                    return;
                }

                InternalChild = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = 9, Bottom = 13, Horizontal = 24 },
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, 55),
                        new Dimension(),
                        new Dimension(GridSizeMode.AutoSize),
                    },
                    Content = new[]
                    {
                        new[] { createHeader() },
                        new[] { createBody() },
                        new[] { createBeatmapInformation() },
                    },
                };

                if (!score.PP.HasValue && score.BeatmapInfo != null)
                {
                    performanceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
                    var token = performanceCancellation.Token;

                    Task.Run(async () =>
                    {
                        var attributes = await difficultyCache.GetDifficultyAsync(score.BeatmapInfo, score.Ruleset, score.Mods, token).ConfigureAwait(false);
                        var performanceCalculator = score.Ruleset.CreateInstance().CreatePerformanceCalculator();

                        // Calculation requires the beatmap and ruleset to be locally available.
                        if (attributes?.DifficultyAttributes == null || performanceCalculator == null)
                            return;

                        var result = await performanceCalculator.CalculateAsync(score, attributes.Value.DifficultyAttributes, token).ConfigureAwait(false);

                        token.ThrowIfCancellationRequested();
                        Schedule(() => performanceText.Text = result.Total.ToString("0"));
                    }, token);
                }
            }

            private Drawable createBody()
            {
                var circle = new Container
                {
                    Name = "Rank circle",
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        createModsBackground(),
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = mirrored ? new MarginPadding { Right = mods_width } : new MarginPadding { Left = mods_width },
                            Children = new Drawable[]
                            {
                                createRankCircleBackground(),
                                accuracyCircle = new AccuracyCircle(score!, showRankBadges: false, rankTextScale: 0.72f)
                                {
                                    Name = "Accuracy circle",
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(circle_size),
                                },
                                createMods(),
                            },
                        },
                    },
                };
                var statistics = new FillFlowContainer
                {
                    Name = "Statistics column",
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new[]
                    {
                        createStatisticsPanel(createStatisticsBackground(), createHitStatistics(), 83),
                        createStatisticsPanel(createBottomStatisticsBackground(), createBottomStatistics(), 40),
                    },
                };

                // Share the grid across layers: backplates, circle, then unobscured badges.
                return new Container
                {
                    Name = "Result body",
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        createBodyGrid(new[]
                        {
                            orderForSide(new Drawable[] { null!, statistics }),
                            new Drawable[] { null!, null! },
                        }),
                        createBodyGrid(new[]
                        {
                            orderForSide(new Drawable[] { circle, null! }),
                            new Drawable[] { null!, null! },
                        }),
                        createBodyGrid(new[]
                        {
                            new Drawable[] { null!, null! },
                            orderForSide(new[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Children = new Drawable[]
                                    {
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            // Centre placement beneath the mod-side half of the circle column.
                                            Padding = mirrored
                                                ? new MarginPadding { Left = circle_size / 2 }
                                                : new MarginPadding { Right = circle_size / 2 },
                                            Child = createPlacementBadge(),
                                        },
                                        createPerfectBadge(),
                                    },
                                },
                                null!,
                            }),
                        }),
                    },
                };
            }

            private GridContainer createBodyGrid(Drawable[][] content) => new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = 23, Bottom = 8 },
                RowDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 10) },
                ColumnDimensions = mirrored
                    ? new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, circle_size + mods_width) }
                    : new[] { new Dimension(GridSizeMode.Absolute, circle_size + mods_width), new Dimension() },
                Content = content,
            };

            private static Drawable createStatisticsPanel(Drawable background, Drawable content, float height) => new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = height,
                Children = new[] { background, content },
            };

            protected override void LoadComplete()
            {
                base.LoadComplete();

                if (score == null)
                    return;

                placement.BindValueChanged(_ => updatePlacement(), true);

                if (leaderboard != null)
                {
                    leaderboardScores.BindTo(leaderboard.Scores);
                    leaderboardScores.BindCollectionChanged((_, _) => bindPlacement(), true);
                }
            }

            protected override void UpdateAfterChildren()
            {
                base.UpdateAfterChildren();

                if (score == null || headerRank == accuracyCircle.CurrentRank)
                    return;

                // Read after the gauge's transforms have updated so both visuals change in the same frame.
                headerRank = accuracyCircle.CurrentRank;
                scoreStripBackground.FadeColour(scoreHeaderColour(headerRank.Value), 100);
                scoreStripText.FadeColour(scoreHeaderTextColour(headerRank.Value), 100);
            }

            private void bindPlacement()
            {
                unbindPositions();

                foreach (var playerScore in leaderboardScores)
                {
                    var position = playerScore.Position.GetBoundCopy();
                    trackedPositions.Add(position);
                    position.BindValueChanged(_ => refreshPlacement());
                }

                refreshPlacement();
            }

            private void refreshPlacement() => placement.Value = resultsScreen.GetPlacement(score!, leaderboardScores.ToArray());

            private void unbindPositions()
            {
                foreach (var position in trackedPositions)
                    position.UnbindAll();

                trackedPositions.Clear();
            }

            private void updatePlacement()
            {
                int position = placement.Value.GetValueOrDefault();
                string suffix = position % 100 is 11 or 12 or 13
                    ? "th"
                    : (position % 10) switch
                    {
                        1 => "st",
                        2 => "nd",
                        3 => "rd",
                        _ => "th",
                    };

                placementBadge.Alpha = position > 0 ? 1 : 0;
                placementText.Text = position > 0 ? $"{position}{suffix}" : string.Empty;
                placementText.Colour = position == 1 ? Color4Extensions.FromHex("#FFC300") : Color4.White;
            }

            protected override void Dispose(bool isDisposing)
            {
                performanceCancellation?.Cancel();
                performanceCancellation?.Dispose();
                unbindPositions();
                leaderboardScores.UnbindAll();
                placement.UnbindAll();
                base.Dispose(isDisposing);
            }

            private Drawable createHeader()
            {
                const float avatar_size = 55;

                return new Container
                {
                    Name = "Score header",
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Size = new Vector2(255, avatar_size),
                    Children = new Drawable[]
                    {
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ColumnDimensions = mirrored
                                ? new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, avatar_size) }
                                : new[] { new Dimension(GridSizeMode.Absolute, avatar_size), new Dimension() },
                            Content = new[]
                            {
                                orderForSide(new Drawable[]
                                {
                                    new Container(),
                                    new GridContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        RowDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 32) },
                                        Content = new[]
                                        {
                                            new Drawable[]
                                            {
                                                new Container
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Padding = new MarginPadding { Horizontal = 5f },
                                                    Children = new Drawable[]
                                                    {
                                                        new TruncatingSpriteText
                                                        {
                                                            Anchor = mirrored ? Anchor.TopRight : Anchor.TopLeft,
                                                            Origin = mirrored ? Anchor.TopRight : Anchor.TopLeft,
                                                            MaxWidth = 255 - avatar_size - 10,
                                                            Text = $"{score?.User.Username}'s Ranking",
                                                            Font = OsuFont.Torus.With(size: 16, weight: FontWeight.Bold),
                                                        },
                                                        new TruncatingSpriteText
                                                        {
                                                            Anchor = mirrored ? Anchor.BottomRight : Anchor.BottomLeft,
                                                            Origin = mirrored ? Anchor.BottomRight : Anchor.BottomLeft,
                                                            MaxWidth = 255 - avatar_size - 10,
                                                            Text = $"Played on {score?.Date}",
                                                            Font = OsuFont.Torus.With(size: 8, weight: FontWeight.Medium),
                                                        },
                                                    }
                                                },
                                            },
                                            new[] { createScoreStrip() },
                                        },
                                    },
                                }),
                            },
                        },
                        new UpdateableAvatar(score!.User, isInteractive: false)
                        {
                            Anchor = mirrored ? Anchor.TopRight : Anchor.TopLeft,
                            Origin = mirrored ? Anchor.TopRight : Anchor.TopLeft,
                            Size = new Vector2(avatar_size),
                            Masking = true,
                            CornerRadius = 6,
                        },
                    },
                };
            }

            private Drawable createScoreStrip() => new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Container
                    {
                        Name = "Slanted score background",
                        RelativeSizeAxes = Axes.Both,
                        // Extend only the avatar-facing edge; the avatar is drawn above this background.
                        Anchor = mirrored ? Anchor.TopLeft : Anchor.TopRight,
                        Origin = mirrored ? Anchor.TopLeft : Anchor.TopRight,
                        Width = 1.06f,
                        Shear = new Vector2(mirrored ? 0.14f : -0.14f, 0),
                        Masking = true,
                        CornerRadius = 3,
                        Child = scoreStripBackground = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = scoreHeaderColour(ScoreRank.D),
                        },
                    },
                    scoreStripText = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 8 },
                        ColumnDimensions = mirrored
                            ? new[] { new Dimension(GridSizeMode.Absolute, 48), new Dimension() }
                            : new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 48) },
                        Content = new[]
                        {
                            orderForSide(new Drawable[]
                            {
                                new ScoreCounter(score!.TotalScore)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Direction = FillDirection.Vertical,
                                    AutoSizeAxes = Axes.Both,
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Text = "Accuracy",
                                            Font = OsuFont.Torus.With(size: 12, weight: FontWeight.Bold),
                                        },
                                        new OsuSpriteText
                                        {
                                            Text = score.Accuracy.ToString("P2"),
                                            Font = OsuFont.Torus.With(size: 13, weight: FontWeight.Bold),
                                        },
                                    },
                                },
                            }),
                        },
                    },
                },
            };

            private Drawable[] orderForSide(Drawable[] children) => mirrored ? children.Reverse().ToArray() : children;

            private Drawable createModsBackground() => new Container
            {
                Name = "Mods background",
                Anchor = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                Origin = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                Size = new Vector2(184, 132),
                CornerRadius = 45,
                Masking = true,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4Extensions.FromHex("#383838"),
                },
            };

            private Drawable createStatisticsBackground() => new Container
            {
                Name = "Statistics background",
                Anchor = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                Origin = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1.5f, 1),
                Masking = true,
                CornerRadius = 12,
                Shear = new Vector2(mirrored ? 0.12f : -0.12f, 0),
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4Extensions.FromHex("#383838"),
                }
            };

            private Drawable createHitStatistics()
            {
                var shear = new Vector2(mirrored ? 0.12f : -0.12f, 0);

                return new GridContainer
                {
                    Name = "Hit statistics",
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Height = 76,
                    Padding = new MarginPadding { Horizontal = 10f, Top = 3f },
                    // Slant the placement, then cancel the shear on each item to keep its text upright.
                    Shear = shear,
                    ColumnDimensions = new[] { new Dimension(), new Dimension() },
                    RowDimensions = new[] { new Dimension(), new Dimension() },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new HitStatistic("GREAT", getStatistic(HitResult.Great), Color4Extensions.FromHex("#02DBCD")) { Shear = -shear },
                            new HitStatistic("OK", getStatistic(HitResult.Ok), Color4Extensions.FromHex("#9EED00")) { Shear = -shear },
                        },
                        new Drawable[]
                        {
                            new HitStatistic("MEH", getStatistic(HitResult.Meh), Color4Extensions.FromHex("#F27255")) { Shear = -shear },
                            new HitStatistic("MISS", getStatistic(HitResult.Miss), Color4Extensions.FromHex("#FA3E3E")) { Shear = -shear },
                        }
                    }
                };
            }

            private Drawable createMods()
            {
                // Keep each icon upright along a concentric arc just outside the circle.
                // Packing is bounded vertically so larger mod sets remain on the backplate.
                var mods = score!.Mods.AsOrdered().ToArray();
                var container = new Container
                {
                    Name = "Mods",
                    RelativeSizeAxes = Axes.Both,
                    Anchor = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                    Origin = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                };

                const float radius = circle_size / 2 + 16;
                float spacing = mods.Length > 1 ? Math.Min(36, 80f / (mods.Length - 1)) : 0;

                for (int i = 0; i < mods.Length; i++)
                {
                    float y = (i - (mods.Length - 1) / 2f) * spacing;
                    float x = MathF.Sqrt(radius * radius - y * y);

                    container.Add(new ModDisplay(showExtendedInformation: false)
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(mirrored ? x : -x, y),
                        ExpansionMode = ExpansionMode.AlwaysExpanded,
                        Scale = new Vector2(0.46f),
                        Current = { Value = new[] { mods[i] } },
                    });
                }

                return container;
            }

            private Drawable createRankCircleBackground() => new CircularContainer
            {
                Name = "Rank circle background",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(136),
                Masking = true,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4Extensions.FromHex("#212121"),
                }
            };

            private Drawable createBottomStatisticsBackground()
            {
                return new Container
                {
                    Name = "Bottom statistics background",
                    Anchor = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                    Origin = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1.5f, 1),
                    Masking = true,
                    CornerRadius = 9,
                    Shear = new Vector2(mirrored ? -0.29f : 0.29f, 0),
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4Extensions.FromHex("#383838"),
                    }
                };
            }

            private Drawable createBottomStatistics()
            {
                int maximumCombo = score!.GetMaximumAchievableCombo();
                bool perfect = maximumCombo > 0 && score!.MaxCombo == maximumCombo;

                return new GridContainer
                {
                    Name = "Bottom statistics",
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Vertical = 7 },
                    ColumnDimensions = mirrored
                        ? new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 0.5f), new Dimension(GridSizeMode.Relative, 0.3f) }
                        : new[] { new Dimension(GridSizeMode.Relative, 0.3f), new Dimension(GridSizeMode.Absolute, 0.5f), new Dimension() },
                    Content = new[]
                    {
                        orderForSide(new Drawable[]
                        {
                            createPerformanceStatistic(),
                            new Box
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(0.5f, 14),
                                Colour = Color4.White.Opacity(0.6f),
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Text = "Max Combo",
                                        Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                                    },
                                    new FillFlowContainer
                                    {
                                        Anchor = Anchor.BottomCentre,
                                        Origin = Anchor.BottomCentre,
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Horizontal,
                                        Spacing = new Vector2(3, 0),
                                        Children = orderForSide(new Drawable[]
                                        {
                                            new OsuSpriteText
                                            {
                                                Anchor = Anchor.BottomLeft,
                                                Origin = Anchor.BottomLeft,
                                                Text = maximumCombo > 0 ? $"{maximumCombo}x" : string.Empty,
                                                Font = OsuFont.Torus.With(size: 8),
                                                Colour = Color4.White.Opacity(0.65f),
                                            },
                                            new OsuSpriteText
                                            {
                                                Anchor = Anchor.BottomLeft,
                                                Origin = Anchor.BottomLeft,
                                                Text = maximumCombo > 0 ? "/" : string.Empty,
                                                Font = OsuFont.Torus.With(size: 8),
                                                Colour = Color4.White.Opacity(0.65f),
                                            },
                                            new OsuSpriteText
                                            {
                                                Anchor = Anchor.BottomLeft,
                                                Origin = Anchor.BottomLeft,
                                                Text = $"{score!.MaxCombo}x",
                                                Font = OsuFont.Torus.With(size: 14, weight: FontWeight.Bold),
                                                Colour = perfect ? Color4Extensions.FromHex("#FFC300") : Color4.White,
                                            },
                                        }),
                                    },
                                },
                            },
                        }),
                    },
                };
            }

            private Drawable createPlacementBadge()
            {
                return placementBadge = new Container
                {
                    Name = "Placement badge",
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Size = new Vector2(25, 10),
                    Alpha = 0,
                    CornerRadius = 5,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("#383838") },
                        placementText = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = OsuFont.Torus.With(size: 8, weight: FontWeight.Bold),
                        },
                    },
                };
            }

            private Drawable createPerfectBadge()
            {
                int maximumCombo = score!.GetMaximumAchievableCombo();
                bool perfect = maximumCombo > 0 && score!.MaxCombo == maximumCombo;

                return new Container
                {
                    Name = "Perfect badge",
                    Anchor = mirrored ? Anchor.TopLeft : Anchor.TopRight,
                    Origin = mirrored ? Anchor.TopLeft : Anchor.TopRight,
                    // Clear the curved edge while keeping the badge beside the circle.
                    X = mirrored ? -12 : 12,
                    Size = new Vector2(48, 9),
                    CornerRadius = 5,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = perfect ? Color4Extensions.FromHex("#FFC300") : Color4Extensions.FromHex("#383838"),
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = "PERFECT",
                            Font = OsuFont.Torus.With(size: 8, weight: FontWeight.Bold),
                            Colour = perfect ? Color4Extensions.FromHex("#383838") : Color4.White.Opacity(0.3f),
                        },
                    },
                };
            }

            private Drawable createBeatmapInformation()
            {
                string title = score!.BeatmapInfo?.BeatmapSet?.Metadata.Title
                               ?? score.BeatmapInfo?.Metadata.Title
                               ?? "Tournament result";
                string difficulty = score.BeatmapInfo?.DifficultyName ?? string.Empty;
                string artist = score.BeatmapInfo?.Metadata.Artist ?? string.Empty;

                return new FillFlowContainer
                {
                    Name = "Beatmap information",
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new TruncatingSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = title,
                            Font = OsuFont.Torus.With(size: 12, weight: FontWeight.Bold),
                            MaxWidth = 270,
                        },
                        new TruncatingSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = artist,
                            Font = OsuFont.Torus.With(size: 9),
                            MaxWidth = 270,
                        },
                        new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Margin = new MarginPadding { Top = 3 },
                            AutoSizeAxes = Axes.X,
                            Height = 11,
                            Masking = true,
                            CornerRadius = 5.5f,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("#383838") },
                                new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Y,
                                    AutoSizeAxes = Axes.X,
                                    ColumnDimensions = new[] { new Dimension(GridSizeMode.AutoSize), new Dimension(GridSizeMode.AutoSize) },
                                    Content = new[]
                                    {
                                        new Drawable[]
                                        {
                                            new StarRatingDisplay(new StarDifficulty(score.BeatmapInfo?.StarRating ?? 0, 0), StarRatingDisplaySize.Small)
                                            {
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                Scale = new Vector2(0.7f),
                                            },
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.Y,
                                                AutoSizeAxes = Axes.X,
                                                Padding = new MarginPadding { Left = 4, Right = 6 },
                                                Children = new Drawable[]
                                                {
                                                    // Keep the original badge width as the minimum while allowing longer difficulty names.
                                                    new Container { Size = new Vector2(50, 11) },
                                                    new OsuSpriteText
                                                    {
                                                        Anchor = Anchor.Centre,
                                                        Origin = Anchor.Centre,
                                                        Text = difficulty,
                                                        Font = OsuFont.Torus.With(size: 8, weight: FontWeight.SemiBold),
                                                    },
                                                }
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    }
                };
            }

            private Drawable createPerformanceStatistic() => new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = "PP",
                        Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                    },
                    performanceText = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Text = score!.PP?.ToString("0") ?? "—",
                        Font = OsuFont.Torus.With(size: 14, weight: FontWeight.Bold),
                    }
                }
            };

            private int getStatistic(HitResult result) => score!.Statistics.TryGetValue(result, out int value) ? value : 0;

            private static Color4 scoreHeaderColour(ScoreRank rank) => Color4Extensions.FromHex(rank switch
            {
                ScoreRank.SH or ScoreRank.XH => "#F2F2F2",
                ScoreRank.X => "#FAC001",
                ScoreRank.S => "#E17B16",
                ScoreRank.A => "#38AD68",
                ScoreRank.B => "#236DC2",
                ScoreRank.C => "#942BA5",
                _ => "#BF0A0B",
            });

            private static Color4 scoreHeaderTextColour(ScoreRank rank)
                => rank is ScoreRank.X or ScoreRank.SH or ScoreRank.XH ? Color4Extensions.FromHex("#2E2E2E") : Color4.White;
        }

        private partial class ScoreCounter : RollingCounter<long>
        {
            private readonly long totalScore;

            protected override double RollingDuration => AccuracyCircle.ACCURACY_TRANSFORM_DURATION;

            protected override Easing RollingEasing => AccuracyCircle.ACCURACY_TRANSFORM_EASING;

            public ScoreCounter(long totalScore)
            {
                this.totalScore = totalScore;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                this.Delay(AccuracyCircle.ACCURACY_TRANSFORM_DELAY).Schedule(() => Current.Value = totalScore);
            }

            protected override LocalisableString FormatCount(long count) => count.ToString("N0");

            protected override OsuSpriteText CreateSpriteText() => new OsuSpriteText
            {
                Font = OsuFont.Torus.With(size: 36, weight: FontWeight.Bold),
            };
        }

        private partial class HitStatistic : CompositeDrawable
        {
            private readonly string label;
            private readonly int value;
            private readonly Color4 colour;

            public HitStatistic(string label, int value, Color4 colour)
            {
                this.label = label;
                this.value = value;
                this.colour = colour;

                RelativeSizeAxes = Axes.Both;
                Width = 1;
                Height = 1;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, -3),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = label,
                            Font = OsuFont.Torus.With(size: 16, weight: FontWeight.Bold),
                            Colour = colour,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = value.ToString(),
                            Font = OsuFont.Torus.With(size: 22, weight: FontWeight.Bold),
                            Colour = colour.Lighten(0.5f),
                        },
                    }
                };
            }
        }
    }
}
