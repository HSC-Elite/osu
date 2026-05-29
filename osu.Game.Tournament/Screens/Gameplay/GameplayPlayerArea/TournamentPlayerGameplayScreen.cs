// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Select.Leaderboards;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    public partial class TournamentPlayerGameplayScreen : OsuScreen
    {
        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly MultiSpectatorLeaderboardProvider leaderboardProvider;

        public PlayerArea PlayerArea { get; }
        private readonly Score score;

        protected override BackgroundScreen CreateBackground() => new BackgroundScreenDefault();

        public TournamentPlayerGameplayScreen(PlayerArea playerArea, Score score, MultiSpectatorLeaderboardProvider leaderboardProvider)
        {
            PlayerArea = playerArea;
            this.score = score;
            this.leaderboardProvider = leaderboardProvider;
            InternalChild = playerArea.With(p => p.RelativeSizeAxes = Axes.Both);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (PlayerArea.Score == null)
                PlayerArea.LoadScore(score);
        }
    }
}
