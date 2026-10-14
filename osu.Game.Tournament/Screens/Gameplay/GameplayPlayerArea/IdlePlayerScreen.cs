// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Graphics;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Menu;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    public partial class IdlePlayerScreen : OsuScreen
    {
        private readonly int index;
        private readonly TeamColour colour;
        private readonly TournamentSpriteText userText;

        protected override BackgroundScreen CreateBackground() => new BackgroundScreenDefault();

        private readonly IBindableList<MatchRoomPlayerInfo> teamUser = new BindableList<MatchRoomPlayerInfo>();

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        private readonly Bindable<float> usernameFontSize = new Bindable<float>();

        private readonly OsuLogo logo;

        public IdlePlayerScreen(int index, TeamColour colour)
        {
            this.index = index;
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
                new IdleSideFlash(),
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
        private void load(LadderInfo ladder)
        {
            usernameFontSize.BindValueChanged(u =>
            {
                userText.Font = OsuFont.Default.With(size: u.NewValue);
            });

            usernameFontSize.BindTo(ladder.IdleScreenUsernameFontSize);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            float targetLogoWidth = MathF.Sqrt(DrawWidth * DrawHeight) * 0.5f;
            float scale = Math.Min(0.5f, targetLogoWidth / logo.DrawWidth);
            logo.Scale = new Vector2(scale);

            teamUser.BindCollectionChanged((_, _) => updateUsername());

            switch (colour)
            {
                case TeamColour.Red:
                    teamUser.BindTo(ipc.RoomPlayers);
                    break;

                case TeamColour.Blue:
                    teamUser.BindTo(ipc.RoomPlayers);
                    break;
            }
        }

        private void updateUsername()
        {
            string username = string.Empty;

            var player = teamUser.FirstOrDefault(p => p.Team == colour && p.SlotIndex == index);

            if (player.SlotIndex != null)
                username = player.Username ?? string.Empty;

            userText.Text = username;
        }

        private partial class IdleSideFlash : MenuSideFlashes
        {
            protected override float Intensity => 2;

            protected override bool OnlyKiai => false;
        }
    }
}
