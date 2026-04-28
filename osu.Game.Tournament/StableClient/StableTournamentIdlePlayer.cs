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
using osuTK;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableTournamentIdlePlayer : CompositeDrawable
    {
        private readonly int slotIndex;
        private readonly TeamColour colour;
        private readonly TournamentSpriteText userText;
        private readonly OsuLogo logo;

        [Resolved]
        private StableMatchIPCInfo stableIpc { get; set; } = null!;

        public StableTournamentIdlePlayer(int slotIndex, TeamColour colour)
        {
            this.slotIndex = slotIndex;
            this.colour = colour;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                logo = new OsuLogo
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
                    Colour = colour == TeamColour.Red ? Color4Extensions.FromHex("#FB8B96") : Color4Extensions.FromHex("#AFF0F7")
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            stableIpc.CurrentMatch.BindValueChanged(_ => updateUsername(), true);
        }

        private void updateUsername()
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

            // 在 Stable 协议中，目前我们只能通过 SpectatorHandler 获取 Username
            // 这里我们通过遍历已激活的处理器来寻找匹配的用户
            var handler = stableIpc.GetActiveSpectatorHandlers().FirstOrDefault(h => h.UserId == userId);
            userText.Text = handler?.Username ?? userId.ToString();
        }
    }
}
