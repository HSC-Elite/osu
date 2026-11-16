// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Play;
using osu.Game.Screens.Tournament;

namespace osu.Game.Skinning.Components
{
    [UsedImplicitly]
    public partial class PlayerName : FontAdjustableSkinComponent, ISpinnerAware
    {
        private readonly OsuSpriteText text;

        [SettingSource("Hide when spinning")]
        public Bindable<bool> HideWhenSpinning { get; } = new Bindable<bool>();

        [Resolved]
        private GameplayState? gameplayState { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private ITeamColorProvider? teamColorProvider { get; set; }

        private IBindable<APIUser>? apiUser;

        protected override bool OverrideUserTextColor => teamColorProvider != null;

        public PlayerName()
        {
            AutoSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                text = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (gameplayState != null)
            {
                text.Text = gameplayState.Score.ScoreInfo.User.Username;

                if (teamColorProvider != null)
                {
                    SetTextColour(teamColorProvider.GetTeamColor(gameplayState.Score.ScoreInfo.UserID));
                }
            }
            else
            {
                apiUser = api.LocalUser.GetBoundCopy();
                apiUser.BindValueChanged(u =>
                {
                    text.Text = u.NewValue.Username;

                    if (teamColorProvider != null)
                    {
                        SetTextColour(teamColorProvider.GetTeamColor(u.NewValue.Id));
                    }
                }, true);
            }
        }

        protected override void SetFont(FontUsage font) => text.Font = font.With(size: 40);

        protected override void SetTextColour(Colour4 textColour) => text.Colour = textColour;
    }
}
