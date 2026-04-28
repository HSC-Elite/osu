// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Tournament.StableClient
{
    /// <summary>
    /// 统一的玩家布局网格，用于保证 Idle 和 Playing 状态下的画面位置完全对齐。
    /// 参照 shanden-lazer 的 TournamentPlayerGrid 实现。
    /// </summary>
    public partial class StableTournamentGrid : CompositeDrawable
    {
        private readonly int playersPerTeam;
        private readonly Container redTeamContainer;
        private readonly Container blueTeamContainer;

        public StableTournamentGrid(int playersPerTeam)
        {
            if (playersPerTeam > 4)
                throw new ArgumentException("Not Support this player count");

            this.playersPerTeam = playersPerTeam;
            RelativeSizeAxes = Axes.Both;

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

        private void setLayout(Container container)
        {
            switch (playersPerTeam)
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

        private int redIndex = 0;
        private int blueIndex = 0;

        public bool AddRedPlayer(Drawable player)
        {
            if (redIndex >= redTeamContainer.Count) return false;
            ((Container)redTeamContainer[redIndex++]).Child = player.With(p => p.RelativeSizeAxes = Axes.Both);
            return true;
        }

        public bool AddBluePlayer(Drawable player)
        {
            if (blueIndex >= blueTeamContainer.Count) return false;
            ((Container)blueTeamContainer[blueIndex++]).Child = player.With(p => p.RelativeSizeAxes = Axes.Both);
            return true;
        }
    }
}
