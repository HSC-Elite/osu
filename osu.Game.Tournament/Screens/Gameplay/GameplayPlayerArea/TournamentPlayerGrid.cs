// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    public partial class TournamentPlayerGrid : CompositeDrawable
    {
        private readonly int playerPerTeam;
        private readonly Container redTeamContainer;
        private readonly Container blueTeamContainer;

        public int PlayerPerTeam => playerPerTeam;

        public TournamentPlayerGrid(int playerPerTeam)
        {
            if (playerPerTeam > 4)
                throw new ArgumentException("Not Support this player count");

            this.playerPerTeam = playerPerTeam;

            InternalChildren = new Drawable[]
            {
                redTeamContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                },
                blueTeamContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                }
            };

            setLayout(redTeamContainer);
            setLayout(blueTeamContainer);
        }

        public Container GetSlot(TeamColour colour, int index)
        {
            var teamContainer = colour == TeamColour.Red ? redTeamContainer : blueTeamContainer;

            if (index < 0 || index >= teamContainer.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (Container)teamContainer[index];
        }

        public void SetSlot(TeamColour colour, int index, Drawable drawable)
        {
            GetSlot(colour, index).Child = drawable.With(d => d.RelativeSizeAxes = Axes.Both);
        }

        public void ClearSlot(TeamColour colour, int index) => GetSlot(colour, index).Clear();

        private void setLayout(Container container)
        {
            switch (playerPerTeam)
            {
                case 1:
                    container.Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                        }
                    };
                    break;

                case 2:
                    container.Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Height = 0.5f,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Height = 0.5f,
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre
                        }
                    };
                    break;

                case 3:
                    container.Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight
                        },
                    };
                    break;

                case 4:
                    container.Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Height = 0.5f,
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight
                        },
                    };
                    break;

                default:
                    throw new ArgumentException("Not Support this player count");
            }
        }

    }
}
