// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Screens.Menu;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.StableClient.IPC;
using osu.Game.Tournament.StableClient.Protocol;
using osuTK;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableTournamentIdlePlayer : CompositeDrawable
    {
        private readonly int slotIndex;
        private readonly int playersPerTeam;
        private readonly TournamentSpriteText userText;

        [Resolved]
        private StableMatchIPCInfo stableIpc { get; set; } = null!;

        public StableTournamentIdlePlayer(int slotIndex, int playersPerTeam)
        {
            this.slotIndex = slotIndex;
            this.playersPerTeam = playersPerTeam;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new OsuLogo
                {
                    Scale = new Vector2(0.5f),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                },
                new MenuSideFlashes(),
                new KiaiMenuFountains(),
                userText = new TournamentSpriteText
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Font = OsuFont.Default.With(size: 60),
                    Colour = slotIndex < playersPerTeam
                        ? Color4Extensions.FromHex("#AFF0F7")
                        : Color4Extensions.FromHex("#FB8B96")
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            stableIpc.CurrentMatch.BindValueChanged(_ => updateDisplay(), true);
        }

        private void updateDisplay()
        {
            var match = stableIpc.CurrentMatch.Value;

            if (match == null)
            {
                userText.Text = string.Empty;
                return;
            }

            int userId = match.SlotUserIds[slotIndex];
            if (userId <= 0)
            {
                userText.Text = string.Empty;
                return;
            }

            var handler = stableIpc.GetActiveSpectatorHandlers().FirstOrDefault(h => h.UserId == userId);
            userText.Text = handler?.Username ?? userId.ToString();

            updateColour(match);
        }

        private void updateColour(MultiplayerMatch match)
        {
            byte team = match.SlotTeams[slotIndex];

            if (team == 1)
                userText.Colour = Color4Extensions.FromHex("#AFF0F7");
            else if (team == 2)
                userText.Colour = Color4Extensions.FromHex("#FB8B96");
        }
    }
}
