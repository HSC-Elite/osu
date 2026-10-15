// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
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

namespace osu.Game.Tournament.Screens.Gameplay.GameplayScreenResultsScreen
{
    public partial class TournamentResultsScreen : ResultsScreen
    {
        /// <summary>
        /// Whether the score card should place its secondary information on the opposite side.
        /// Text remains upright; this only mirrors the layout used by the two tournament windows.
        /// </summary>
        public bool Mirrored { get; init; }

        protected override bool UseDefaultResultsLayout => false;

        public TournamentResultsScreen(ScoreInfo? score, bool mirrored = false)
            : base(score)
        {
            Mirrored = mirrored;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new TournamentResultsContent(Score, Mirrored);
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
            private readonly ScoreInfo? score;
            private readonly bool mirrored;

            [Resolved]
            private IGameplayLeaderboardProvider? leaderboard { get; set; }

            private readonly IBindableList<GameplayLeaderboardScore> leaderboardScores = new BindableList<GameplayLeaderboardScore>();
            private readonly Bindable<int?> placement = new Bindable<int?>();
            private GameplayLeaderboardScore? trackedScore;
            private Container placementBadge = null!;
            private OsuSpriteText placementText = null!;

            public ResultCard(ScoreInfo? score, bool mirrored)
            {
                this.score = score;
                this.mirrored = mirrored;

                Anchor = Anchor.Centre;
                Origin = Anchor.Centre;
                Size = new Vector2(400, 300);
            }

            [BackgroundDependencyLoader]
            private void load()
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

                InternalChildren = new Drawable[]
                {
                    createHeader(),
                    createModsBackground(),
                    createStatisticsBackground(),
                    createHitStatistics(),
                    createMods(),
                    createBottomStatisticsBackground(),
                    createBottomStatistics(),
                    createRankCircleBackground(),
                    new AccuracyCircle(score, showRankBadges: false, rankTextScale: 0.72f)
                    {
                        Name = "Accuracy circle",
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        X = mirrored ? 42 : -42,
                        Y = 9,
                        Size = new Vector2(176),
                    },
                    createPerfectBadge(),
                    createPlacementBadge(),
                    createBeatmapInformation(),
                };
            }

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

            private void bindPlacement()
            {
                var playerScore = leaderboardScores.FirstOrDefault(s => s.User.OnlineID == score!.UserID);
                if (ReferenceEquals(playerScore, trackedScore))
                    return;

                if (trackedScore != null)
                    placement.UnbindFrom(trackedScore.Position);

                trackedScore = playerScore;
                placement.Value = null;

                if (trackedScore != null)
                    placement.BindTo(trackedScore.Position);
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
                leaderboardScores.UnbindAll();
                placement.UnbindAll();
                base.Dispose(isDisposing);
            }

            private Drawable createHeader()
            {
                const float header_width = 255;
                const float avatar_size = 55;
                const float details_x = avatar_size;

                Anchor side = mirrored ? Anchor.CentreRight : Anchor.CentreLeft;
                float direction = mirrored ? -1 : 1;

                return new Container
                {
                    Name = "Score header",
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    X = mirrored ? 4 : -4,
                    Y = 9,
                    Size = new Vector2(header_width, avatar_size),
                    Children = new Drawable[]
                    {
                        new UpdateableAvatar(score!.User, isInteractive: false)
                        {
                            Anchor = side,
                            Origin = side,
                            Size = new Vector2(avatar_size),
                        },
                        new Container
                        {
                            Anchor = side,
                            Origin = side,
                            X = direction * details_x,
                            Width = header_width - details_x,
                            Height = avatar_size,
                            Children = new Drawable[]
                            {
                                new TruncatingSpriteText
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Text = $"{score.User.Username}'s Ranking",
                                    Font = OsuFont.Torus.With(size: 16, weight: FontWeight.Bold),
                                    MaxWidth = header_width - details_x,
                                },
                                new Container
                                {
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    RelativeSizeAxes = Axes.X,
                                    Width = 1,
                                    Height = 32,
                                    Masking = true,
                                    Children = new Drawable[]
                                    {
                                        new Container
                                        {
                                            Name = "Slanted score background",
                                            RelativeSizeAxes = Axes.Both,
                                            // Extend beneath the avatar and clip that edge straight.
                                            X = mirrored ? 5 : -5,
                                            Width = 1,
                                            Shear = new Vector2(mirrored ? 0.14f : -0.14f, 0),
                                            Masking = true,
                                            CornerRadius = 3,
                                            Child = new Box
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Colour = scoreHeaderColour(score.Rank),
                                            },
                                        },
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            X = mirrored ? 30 : -30,
                                            Text = score.TotalScore.ToString("N0"),
                                            Font = OsuFont.Torus.With(size: 28, weight: FontWeight.Bold),
                                            Colour = scoreHeaderTextColour(score.Rank),
                                        },
                                        new FillFlowContainer
                                        {
                                            Anchor = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                                            Origin = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                                            Direction = FillDirection.Vertical,
                                            AutoSizeAxes = Axes.Both,
                                            X = mirrored ? 18 : -18,
                                            Children = new Drawable[]
                                            {
                                                new OsuSpriteText
                                                {
                                                    Text = "Accuracy",
                                                    Font = OsuFont.Torus.With(size: 10, weight: FontWeight.Bold),
                                                    Colour = scoreHeaderTextColour(score.Rank),
                                                },
                                                new OsuSpriteText
                                                {
                                                    Text = score.Accuracy.ToString("P2"),
                                                    Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                                                    Colour = scoreHeaderTextColour(score.Rank),
                                                },
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };
            }

            private Drawable createModsBackground() => new Container
            {
                Name = "Mods background",
                Anchor = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                Origin = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                X = mirrored ? -23 : 23,
                Y = 8,
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
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                X = mirrored ? -82 : 82,
                Y = -17,
                Size = new Vector2(180, 83),
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
                Anchor side = mirrored ? Anchor.CentreLeft : Anchor.CentreRight;
                float direction = mirrored ? 1 : -1;

                var shear = new Vector2(mirrored ? 0.12f : -0.12f, 0);

                return new GridContainer
                {
                    Name = "Hit statistics",
                    Anchor = side,
                    Origin = side,
                    X = direction * 30,
                    Y = -12,
                    Size = new Vector2(132, 76),
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
                // Keep each icon upright while placing its centre on the circle's outer arc.
                // Packing is bounded vertically so larger mod sets remain on the backplate.
                var mods = score!.Mods.AsOrdered().ToArray();
                var container = new Container
                {
                    Name = "Mods",
                    RelativeSizeAxes = Axes.Both,
                    Anchor = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                    Origin = mirrored ? Anchor.CentreRight : Anchor.CentreLeft,
                };

                const float radius = 111;
                float spacing = mods.Length > 1 ? Math.Min(32, 65f / (mods.Length - 1)) : 0;
                float centreX = mirrored ? 242 : 158;

                for (int i = 0; i < mods.Length; i++)
                {
                    float y = (i - (mods.Length - 1) / 2f) * spacing;
                    float x = MathF.Sqrt(radius * radius - y * y);

                    container.Add(new ModDisplay(showExtendedInformation: false)
                    {
                        Origin = Anchor.Centre,
                        Position = new Vector2(centreX + (mirrored ? x : -x), 158 + y),
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
                X = mirrored ? 42 : -42,
                Y = 9,
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
                Anchor side = mirrored ? Anchor.CentreLeft : Anchor.CentreRight;
                float direction = mirrored ? 1 : -1;

                return new Container
                {
                    Name = "Bottom statistics background",
                    Anchor = side,
                    Origin = side,
                    X = direction * 25,
                    Y = 54,
                    Size = new Vector2(187, 40),
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

                return new Container
                {
                    Name = "Bottom statistics",
                    Anchor = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                    Origin = mirrored ? Anchor.CentreLeft : Anchor.CentreRight,
                    X = mirrored ? 33 : -33,
                    Y = 54,
                    Size = new Vector2(128, 40),
                    Children = new Drawable[]
                    {
                        createBottomStatistic("PP", score!.PP?.ToString("0") ?? "—", 0.3f, mirrored ? 0.7f : 0),
                        new Box
                        {
                            RelativePositionAxes = Axes.X,
                            X = mirrored ? 0.7f : 0.3f,
                            Y = 13,
                            Size = new Vector2(0.5f, 14),
                            Colour = Color4.White.Opacity(0.6f),
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativePositionAxes = Axes.X,
                            X = mirrored ? 0 : 0.3f,
                            Width = 0.7f,
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Y = 7,
                                    Text = "Max Combo",
                                    Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.BottomCentre,
                                    Origin = Anchor.BottomCentre,
                                    Y = -7,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(3, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.BottomLeft,
                                            Origin = Anchor.BottomLeft,
                                            Text = maximumCombo > 0 ? $"{maximumCombo}x /" : string.Empty,
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
                                    },
                                },
                            },
                        },
                    },
                };
            }

            private Drawable createPlacementBadge()
            {
                return placementBadge = new Container
                {
                    Name = "Placement badge",
                    Position = new Vector2(mirrored ? 299 : 76, 228),
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
                    Position = new Vector2(mirrored ? 137 : 215, 228),
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
                var difficultyText = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = difficulty,
                    Font = OsuFont.Torus.With(size: 8, weight: FontWeight.SemiBold),
                };
                difficultyText.OnUpdate += text => text.Scale = new Vector2(Math.Min(1, text.Parent!.DrawWidth / Math.Max(1, text.Width)));

                return new FillFlowContainer
                {
                    Name = "Beatmap information",
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    X = mirrored ? 31 : -31,
                    Y = -13,
                    Width = 270,
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
                            Size = new Vector2(111, 11),
                            Masking = true,
                            CornerRadius = 5.5f,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("#383838") },
                                new Container
                                {
                                    Size = new Vector2(35, 11),
                                    Masking = true,
                                    CornerRadius = 5.5f,
                                    Children = new Drawable[]
                                    {
                                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("#7948EA") },
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Text = $"★ {score.BeatmapInfo?.StarRating ?? 0:0.00}",
                                            Font = OsuFont.Torus.With(size: 8, weight: FontWeight.Bold),
                                            Colour = Color4Extensions.FromHex("#FFC300"),
                                        },
                                    },
                                },
                                new Container
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    X = 39,
                                    Size = new Vector2(69, 11),
                                    Child = difficultyText,
                                },
                            },
                        },
                    }
                };
            }

            private static Drawable createBottomStatistic(string label, string value, float relativeWidth, float relativeX) => new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = relativeWidth,
                RelativePositionAxes = Axes.X,
                X = relativeX,
                Height = 1,
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 7,
                        Text = label,
                        Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Y = -7,
                        Text = value,
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
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
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
