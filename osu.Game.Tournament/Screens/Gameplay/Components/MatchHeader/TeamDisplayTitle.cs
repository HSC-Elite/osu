// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay.Components.MatchHeader
{
    public partial class TeamDisplayTitle : CompositeDrawable
    {
        private readonly TournamentTeam? team;
        private readonly TeamColour teamColour;
        private readonly BindableList<BeatmapChoice> picksBans = new BindableList<BeatmapChoice>();

        private TournamentSpriteText teamText = null!;
        private Container stateIconContainer = null!;
        private TournamentSpriteText teamIdText = null!;
        private Box teamIdBackground = null!;
        private Sprite pigIcon = null!;
        private Triangle arrowIcon = null!;

        [Resolved]
        private TextureStore store { get; set; } = null!;

        public TeamDisplayTitle(TournamentTeam? team, TeamColour teamColour)
        {
            this.team = team;
            this.teamColour = teamColour;
        }

        private readonly Bindable<double?> currentTeamCoin = new Bindable<double?>();
        private readonly Bindable<double?> opponentTeamCoin = new Bindable<double?>();
        private TeamDisplayNote teamNote = null!;
        private Container teamTextContainer = null!;

        [BackgroundDependencyLoader]
        private void load(LadderInfo ladder, MatchHeader header)
        {
            var anchor = teamColour == TeamColour.Blue ? Anchor.CentreLeft : Anchor.CentreRight;

            AutoSizeAxes = Axes.X;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White
                },
                new Box
                {
                    Anchor = teamColour == TeamColour.Blue ? Anchor.CentreRight : Anchor.CentreLeft,
                    Origin = teamColour == TeamColour.Blue ? Anchor.CentreRight : Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Y,
                    Width = 10,
                    Colour = TournamentGame.GetTeamColour(teamColour)
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.X,
                    RelativeSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            Anchor = anchor,
                            Origin = anchor,
                            LayoutDuration = 100,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Margin = teamColour == TeamColour.Blue
                                ? new MarginPadding { Left = 10, Right = 10 + 15 }
                                : new MarginPadding { Left = 10 + 15, Right = 10 },
                            Spacing = new Vector2(2, 0),
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    Anchor = anchor,
                                    Origin = anchor,
                                    RelativeSizeAxes = Axes.Y,
                                    AutoSizeAxes = Axes.X,
                                    Direction = FillDirection.Vertical,
                                    Margin = new MarginPadding { Bottom = 2f },
                                    Spacing = new Vector2(0, 2),
                                    Children = new Drawable[]
                                    {
                                        new Container
                                        {
                                            Anchor = Anchor.BottomCentre,
                                            Origin = Anchor.BottomCentre,
                                            Size = new Vector2(16, 11),
                                            Children = new Drawable[]
                                            {
                                                teamIdBackground = new Box
                                                {
                                                    Colour = TournamentGame.ELEMENT_BACKGROUND_COLOUR,
                                                    RelativeSizeAxes = Axes.Both,
                                                },
                                                teamIdText = new TournamentSpriteText
                                                {
                                                    Anchor = Anchor.Centre,
                                                    Origin = Anchor.Centre,
                                                    Colour = TournamentGame.ELEMENT_FOREGROUND_COLOUR,
                                                    Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                                                }
                                            }
                                        },
                                        stateIconContainer = new Container
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Anchor = Anchor.BottomCentre,
                                            Origin = Anchor.BottomCentre,
                                        },
                                    }
                                },
                                teamTextContainer = new Container
                                {
                                    Anchor = anchor,
                                    Origin = anchor,
                                    AutoSizeAxes = Axes.Both,
                                    Child = teamText = new TournamentSpriteText
                                    {
                                        Anchor = anchor,
                                        Origin = anchor,
                                        Font = OsuFont.GetFont(size: 20, weight: FontWeight.Bold),
                                        Colour = Color4.Black,
                                    },
                                },
                                pigIcon = new Sprite
                                {
                                    Anchor = anchor,
                                    Origin = anchor,
                                    Texture = store.Get("pig"),
                                    Size = new Vector2(13),
                                    Alpha = 0,
                                },
                                new Container
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Anchor = anchor,
                                    Origin = anchor,
                                    Child = arrowIcon = new Triangle
                                    {
                                        Size = new Vector2(20, 15),
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Colour = Color4.Black,
                                        Rotation = teamColour == TeamColour.Blue ? -90 : 90,
                                        Alpha = 0,
                                        AlwaysPresent = true,
                                    },
                                },
                            }
                        },
                    }
                },
                teamNote = new TeamDisplayNote(teamColour)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.BottomCentre,
                }
            };

            if (team != null)
            {
                team.FullName.BindValueChanged(name =>
                {
                    teamText.Text = name.NewValue;

                    Scheduler.AddOnce(() => header.TextWidthEachTeam[teamColour] = teamText.Width);
                }, true);
                team.Seed.BindValueChanged(seed => teamIdText.Text = seed.NewValue, true);
                team.Color.BindValueChanged(color => teamIdBackground.Colour = color.NewValue, true);
                team.IdTextColor.BindValueChanged(color => teamIdText.Colour = color.NewValue, true);
                team.Note.BindValueChanged(note =>
                {
                    teamNote.Text = note.NewValue;
                }, true);

                header.TextWidthEachTeam.BindCollectionChanged((_, _) =>
                    teamTextContainer.Padding = new MarginPadding
                    {
                        Horizontal = (header.TextWidthEachTeam.Max(w => w.Value) - teamText.Width) / 2
                    });

                picksBans.BindCollectionChanged((_, _) => updatePick());

                if (ladder.CurrentMatch.Value != null)
                    picksBans.BindTo(ladder.CurrentMatch.Value.PicksBans);
            }

            var currentMatch = ladder.CurrentMatch.Value;

            if (currentMatch == null)
                return;

            currentTeamCoin.BindTo(teamColour == TeamColour.Red ? currentMatch.Team1Coin : currentMatch.Team2Coin);
            opponentTeamCoin.BindTo(teamColour == TeamColour.Blue ? currentMatch.Team1Coin : currentMatch.Team2Coin);

            currentTeamCoin.BindValueChanged(_ => updateDisplay(), true);
            opponentTeamCoin.BindValueChanged(_ => updateDisplay(), true);
        }

        private const double pig_warning_coin = -35;
        private const double disconnect_warning_coin = -60;

        private Drawable getIconByDiff(double diff)
        {
            return diff < disconnect_warning_coin ? getIcon("WEB") : Empty();
        }

        private void updateDisplay() => Scheduler.AddOnce(() =>
        {
            double diff = (currentTeamCoin.Value ?? 0) - (opponentTeamCoin.Value ?? 0);
            stateIconContainer.Child = getIconByDiff(diff);
            stateIconContainer.FadeIn(500).Then().FadeOut(500).Loop();

            if (diff < pig_warning_coin)
            {
                pigIcon.FadeIn();
            }
            else
            {
                pigIcon.FadeOut();
            }
        });

        private Drawable getIcon(string icon) => new Sprite
        {
            Size = new Vector2(10, 8),
            Texture = store.Get(icon)
        };

        private void updatePick()
        {
            var lastChoice = picksBans.LastOrDefault();

            if (lastChoice?.Team == teamColour && lastChoice?.Type == ChoiceType.Pick)
            {
                arrowIcon.FadeIn(100);
            }
            else
            {
                arrowIcon.FadeOut(100);
            }
        }
    }
}
