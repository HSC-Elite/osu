// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Audio;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentPlayerGameplayScreen : TournamentPlayerPresentation
    {
        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly TournamentLiveLeaderboardProvider leaderboardProvider;

        public PlayerArea PlayerArea { get; }
        private readonly Score score;

        public override bool PlayerLoaded => PlayerArea.PlayerLoaded;

        public override IBindable<int>? Combo => leaderboardProvider.GetPlayerCombo(PlayerArea.UserId);

        public override bool IsAudioSourceCandidate
            => PlayerArea.SpectatorPlayerClock.IsRunning && !PlayerArea.SpectatorPlayerClock.IsCatchingUp && !PlayerArea.SpectatorPlayerClock.WaitingOnFrames;

        public override double CurrentTime => PlayerArea.SpectatorPlayerClock.CurrentTime;

        public override Score? ReplayScore => PlayerArea.Score;

        public override IAggregateAudioAdjustment ClockAdjustmentsFromMods => PlayerArea.ClockAdjustmentsFromMods;

        protected override BackgroundScreen CreateBackground() => new BackgroundScreenDefault();

        private Player? player;

        internal TournamentPlayerGameplayScreen(PlayerArea playerArea, Score score, TournamentLiveLeaderboardProvider leaderboardProvider)
        {
            PlayerArea = playerArea;
            this.score = score;
            this.leaderboardProvider = leaderboardProvider;
            InternalChild = playerArea.With(p => p.RelativeSizeAxes = Axes.Both);
            PlayerArea.OnGameplayStarted += onGameplayStarted;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (PlayerArea.Score == null)
                PlayerArea.LoadScore(score);
        }

        public override void MarkFailedOrQuit() => PlayerArea.FadeColour(Colour4.Gray, 400, Easing.OutQuint);

        public override void ForceToResult()
        {
            if (PlayerArea.Player is MultiSpectatorPlayer spectatorPlayer)
                spectatorPlayer.ForceToResult();
        }

        public override void SetMuted(bool muted) => PlayerArea.Mute = muted;

        private void onGameplayStarted()
        {
            player = PlayerArea.Player;

            if (player != null)
                player.OnShowingResults += onShowingResults;
        }

        private void onShowingResults()
        {
            if (player != null)
                player.OnShowingResults -= onShowingResults;

            NotifyGameplayEnded();
        }

        protected override void Dispose(bool isDisposing)
        {
            PlayerArea.OnGameplayStarted -= onGameplayStarted;

            if (player != null)
                player.OnShowingResults -= onShowingResults;

            base.Dispose(isDisposing);
        }
    }
}
