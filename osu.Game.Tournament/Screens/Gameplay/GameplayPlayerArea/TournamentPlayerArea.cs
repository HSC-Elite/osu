// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Scoring;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Tournament.GameplayScreenResultsScreen;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentPlayerArea : PlayerArea
    {
        private readonly IGameplayLeaderboardProvider leaderboard;
        private readonly bool mirrored;

        public TournamentPlayerArea(int userId, SpectatorPlayerClock clock, IGameplayLeaderboardProvider leaderboard, bool mirrored)
            : base(userId, clock)
        {
            this.leaderboard = leaderboard;
            this.mirrored = mirrored;
        }

        protected override MultiSpectatorPlayer CreatePlayer(Score score, SpectatorPlayerClock clock)
            => new TournamentSpectatorPlayer(score, clock, leaderboard, mirrored);

        private partial class TournamentSpectatorPlayer : MultiSpectatorPlayer
        {
            private readonly IGameplayLeaderboardProvider leaderboard;
            private readonly bool mirrored;

            public TournamentSpectatorPlayer(Score score, SpectatorPlayerClock clock, IGameplayLeaderboardProvider leaderboard, bool mirrored)
                : base(score, clock)
            {
                this.leaderboard = leaderboard;
                this.mirrored = mirrored;
            }

            protected override ResultsScreen CreateResults(ScoreInfo score)
                => new TournamentSpectatorResultsScreen(score, leaderboard, mirrored);
        }

        private partial class TournamentSpectatorResultsScreen : TournamentResultsScreen
        {
            [Resolved]
            private MatchIPCInfo matchInfo { get; set; } = null!;

            // MultiSpectatorPlayer suppresses its gameplay leaderboard. Restore the shared
            // tournament provider only for the results card, where placement remains live.
            [Cached(typeof(IGameplayLeaderboardProvider))]
            private readonly IGameplayLeaderboardProvider leaderboard;

            protected override int? GetPlacement(ScoreInfo score, IReadOnlyList<GameplayLeaderboardScore> scores)
            {
                int[] teamIds = matchInfo.GetTeamIds(TeamColour.Red);
                if (!teamIds.Contains(score.UserID))
                    teamIds = matchInfo.GetTeamIds(TeamColour.Blue);

                if (!teamIds.Contains(score.UserID))
                    return null;

                int? position = base.GetPlacement(score, scores);
                if (position == null || position <= 0)
                    return null;

                return 1 + scores.Count(s => teamIds.Contains(s.User.OnlineID) && s.Position.Value > 0 && s.Position.Value < position);
            }

            public TournamentSpectatorResultsScreen(ScoreInfo score, IGameplayLeaderboardProvider leaderboard, bool mirrored)
                : base(score, mirrored)
            {
                this.leaderboard = leaderboard;
                AllowRetry = false;
                HideBottomPanel = true;
            }
        }
    }
}
