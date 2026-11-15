// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Gameplay.Components.RoundInformation
{
    /// <summary>An annotated stage preview for the flow editor. The broadcast keeps its own presentation.</summary>
    public partial class BanPickStageDisplay : FillFlowContainer
    {
        public BanPickStageDisplay(string name, IEnumerable<BanPickFlowSlot> slots)
        {
            AutoSizeAxes = Axes.Both;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 4);
            Add(new TournamentSpriteText
            {
                Text = name,
                Font = OsuFont.Default.With(size: 14),
                Height = 18
            });
            var maps = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0)
            };
            Add(maps);

            foreach (var slot in slots)
            {
                Drawable map = slot.Choice == null
                    ? new PendingFlowMap(slot)
                    : slot.Choice.Type switch
                    {
                        ChoiceType.Protected => new ProtectMapBox(slot.Choice),
                        ChoiceType.Ban => new BanMapBox(slot.Choice),
                        _ => new PickMapBox(slot.Choice)
                    };
                maps.Add(map);
            }
        }

        private partial class PendingFlowMap : CompositeDrawable
        {
            public PendingFlowMap(BanPickFlowSlot slot)
            {
                Size = new Vector2(42, 110);
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = OsuColour.Gray(0.18f),
                        Alpha = 0.6f
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 4,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = MapBox.GetColorFromTeamColor(slot.Team)
                    },
                    new TournamentSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 15,
                        Font = OsuFont.Default.With(size: 13),
                        Text = BanPickFlow.ActionName(slot.Step.CurrentAction.Value)
                    },
                    new TournamentSpriteText
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Y = -15,
                        Font = OsuFont.Default.With(size: 13),
                        Text = slot.Team == TeamColour.Red ? "红队" : "蓝队",
                        Colour = MapBox.GetColorFromTeamColor(slot.Team)
                    }
                };
            }
        }
    }
}
