// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Graphics;
using osu.Game.Online.Multiplayer;
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

        private readonly IBindableList<MultiplayerRoomUser> teamUser = new BindableList<MultiplayerRoomUser>();

        [Resolved]
        private LazerRoomMatchInfo lazerRoomMatchInfo { get; set; } = null!;

        private OsuLogo logo;

        private static readonly Vector2 small_logo_size = new Vector2(0.35f);
        private static readonly Vector2 medium_logo_size = new Vector2(0.5f);

        public bool SmallOsuLogo
        {
            get => logo.Scale == small_logo_size;
            set => Scheduler.Add(() =>
            {
                logo.Scale = value ? small_logo_size : medium_logo_size;
            });
        }

        public IdlePlayerScreen(int index, TeamColour colour)
        {
            this.index = index;
            this.colour = colour;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                logo = new OsuLogo
                {
                    Scale = medium_logo_size,
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

        protected override void LoadComplete()
        {
            base.LoadComplete();

            teamUser.BindCollectionChanged((_, _) => updateUsername());

            switch (colour)
            {
                case TeamColour.Red:
                    teamUser.BindTo(lazerRoomMatchInfo.RedTeamUser);
                    break;

                case TeamColour.Blue:
                    teamUser.BindTo(lazerRoomMatchInfo.BlueTeamUser);
                    break;
            }
        }

        private void updateUsername()
        {
            string username = string.Empty;

            if (teamUser.Count > index)
            {
                username = teamUser[index].User?.Username ?? string.Empty;
            }

            userText.Text = username;
        }
    }
}
