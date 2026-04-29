// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableTournamentGrid : CompositeDrawable
    {
        public readonly int PlayersPerTeam;

        public int SlotCount => PlayersPerTeam * 2;

        private readonly Container leftContainer;
        private readonly Container rightContainer;

        public StableTournamentGrid(int playersPerTeam)
        {
            if (playersPerTeam > 4)
                throw new ArgumentException("Not Support this player count");

            PlayersPerTeam = playersPerTeam;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                leftContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                },
                rightContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                }
            };

            setLayout(leftContainer);
            setLayout(rightContainer);
        }

        private void setLayout(Container container)
        {
            switch (PlayersPerTeam)
            {
                case 1:
                    container.Children = new Drawable[] { new Container { RelativeSizeAxes = Axes.Both, Masking = true } };
                    break;

                case 2:
                    container.Children = new Drawable[]
                    {
                        new Container { RelativeSizeAxes = Axes.Both, Height = 0.5f, Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Masking = true },
                        new Container { RelativeSizeAxes = Axes.Both, Height = 0.5f, Anchor = Anchor.BottomCentre, Origin = Anchor.BottomCentre, Masking = true }
                    };
                    break;

                case 3:
                    container.Children = new Drawable[]
                    {
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Masking = true },
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Masking = true },
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.BottomRight, Origin = Anchor.BottomRight, Masking = true }
                    };
                    break;

                case 4:
                    container.Children = new Drawable[]
                    {
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.TopLeft, Origin = Anchor.TopLeft, Masking = true },
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.TopRight, Origin = Anchor.TopRight, Masking = true },
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Masking = true },
                        new Container { RelativeSizeAxes = Axes.Both, Width = 0.5f, Height = 0.5f, Anchor = Anchor.BottomRight, Origin = Anchor.BottomRight, Masking = true }
                    };
                    break;
            }
        }

        public Container GetSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));

            if (slotIndex < PlayersPerTeam)
                return (Container)leftContainer[slotIndex];

            return (Container)rightContainer[slotIndex - PlayersPerTeam];
        }
    }
}
