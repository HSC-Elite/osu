// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Tournament;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea;
using osuTK.Graphics;

namespace osu.Game.Tournament.IPC
{
    public partial class MatchIPCInfo : CompositeComponent, ITeamColorProvider
    {
        [Resolved]
        private IBindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        protected LadderInfo Ladder { get; private set; } = null!;

        private CancellationTokenSource? downloadCheckCancellation;
        private int lastAutoDownloadBeatmap;
        private TournamentLiveLeaderboardProvider? leaderboardProvider;
        private readonly Dictionary<TeamColour, int[]> teamIdsCache = new Dictionary<TeamColour, int[]>();
        private readonly Dictionary<int, double> userMultiplierCache = new Dictionary<int, double>();

        public Bindable<TournamentBeatmap?> Beatmap { get; } = new Bindable<TournamentBeatmap?>();
        public Bindable<LegacyMods> Mods { get; } = new Bindable<LegacyMods>();
        public Bindable<TournamentScoringMode> ScoringMode { get; } = new Bindable<TournamentScoringMode>(TournamentScoringMode.Score);
        public Bindable<TourneyState> State { get; } = new Bindable<TourneyState>();
        public Bindable<Channel?> ChatChannel { get; } = new Bindable<Channel?>();
        public BindableLong Score1 { get; } = new BindableLong();
        public BindableLong Score2 { get; } = new BindableLong();

        public BindableInt Team1Combo { get; } = new BindableInt();
        public BindableInt Team2Combo { get; } = new BindableInt();

        public BindableBool HasActiveMatch { get; } = new BindableBool();

        protected readonly BindableList<MatchRoomPlayerInfo> RoomPlayersInternal = new BindableList<MatchRoomPlayerInfo>();

        public IBindableList<MatchRoomPlayerInfo> RoomPlayers => RoomPlayersInternal;

        // IPC sources can override this when their state is controlled externally.
        internal virtual bool UsesLocalGameplayCompletion => true;

        internal bool ServerResultsReady { get; private set; }

        protected void NotifyServerResultsReady()
        {
            if (State.Value is TourneyState.WaitingForClients or TourneyState.Playing or TourneyState.Ranking)
                ServerResultsReady = true;
        }

        internal void NotifyGameplayCompleted()
        {
            if (UsesLocalGameplayCompletion && State.Value is TourneyState.WaitingForClients or TourneyState.Playing)
                State.Value = TourneyState.Ranking;
        }

        public MatchIPCInfo()
        {
            State.BindValueChanged(s =>
            {
                if (s.NewValue is TourneyState.Idle or TourneyState.WaitingForClients)
                    ServerResultsReady = false;

                Logger.Log($"Tourney State turn {s.OldValue} to {s.NewValue}");
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            State.BindValueChanged(_ =>
            {
                updateTrackLooping();

                if (State.Value != TourneyState.Playing)
                    userMultiplierCache.Clear();
            });
            workingBeatmap.BindValueChanged(onWorkingBeatmapChanged, true);
            Beatmap.BindValueChanged(onBeatmapChanged);
            Ladder.CurrentMatch.BindValueChanged(_ => teamIdsCache.Clear(), true);
        }

        protected override void Dispose(bool isDisposing)
        {
            workingBeatmap.ValueChanged -= onWorkingBeatmapChanged;
            Beatmap.ValueChanged -= onBeatmapChanged;
            downloadCheckCancellation?.Cancel();

            base.Dispose(isDisposing);
        }

        private void onWorkingBeatmapChanged(ValueChangedEvent<WorkingBeatmap> _) => updateTrackLooping();

        private void updateTrackLooping() => workingBeatmap.Value.PrepareTrackForPreview(State.Value == TourneyState.Idle);

        protected override void Update()
        {
            base.Update();

            ScoringMode.Value = getScoringMode();

            if (State.Value == TourneyState.Playing)
                updateScore();
        }

        private TournamentScoringMode getScoringMode()
        {
            int beatmapId = Beatmap.Value?.OnlineID ?? 0;
            var beatmaps = Ladder.CurrentMatch.Value?.Round.Value?.Beatmaps;

            if (beatmapId > 0 && beatmaps != null)
            {
                for (int i = 0; i < beatmaps.Count; i++)
                {
                    var beatmap = beatmaps[i];

                    if (beatmap.ID == beatmapId || beatmap.Beatmap?.OnlineID == beatmapId)
                        return beatmap.ScoringMode;
                }
            }

            return TournamentScoringMode.Score;
        }

        private void onBeatmapChanged(ValueChangedEvent<TournamentBeatmap?> beatmap)
        {
            int beatmapId = beatmap.NewValue?.OnlineID ?? 0;

            if (beatmapId == lastAutoDownloadBeatmap)
                return;

            lastAutoDownloadBeatmap = beatmapId;
            downloadCheckCancellation?.Cancel();

            if (beatmapId <= 0)
                return;

            var cancellation = downloadCheckCancellation = new CancellationTokenSource();

            beatmapLookupCache.GetBeatmapAsync(beatmapId, cancellation.Token).ContinueWith(resolved => Schedule(() =>
            {
                if (cancellation.IsCancellationRequested)
                    return;

                APIBeatmap? map = resolved.GetResultSafely();
                var beatmapSet = map?.BeatmapSet;

                if (map == null || beatmapSet == null || beatmapManager.IsAvailableLocally(map))
                    return;

                beatmapDownloader.Download(beatmapSet);
            }));
        }

        protected void SetRoomPlayers(IEnumerable<MatchRoomPlayerInfo> players)
        {
            var playerArray = players.ToArray();

            if (RoomPlayersInternal.SequenceEqual(playerArray))
                return;

            RoomPlayersInternal.ReplaceRange(0, RoomPlayersInternal.Count, playerArray);
        }

        public virtual void RefreshChatChannel()
        {
        }

        public virtual bool PostChatMessage(string message) => false;

        internal virtual void SetLiveLeaderboardProvider(TournamentLiveLeaderboardProvider? provider)
        {
            leaderboardProvider = provider;

            if (provider == null)
                userMultiplierCache.Clear();
        }

        public virtual void RequestAuthoritativeScores(Action<IReadOnlyList<TournamentPlayerScoreResult>?> onComplete)
        {
            onComplete(null);
        }

        private void updateScore()
        {
            if (leaderboardProvider == null)
                return;

            var (score1, combo1) = getTeamTotals(TeamColour.Red);
            var (score2, combo2) = getTeamTotals(TeamColour.Blue);

            Score1.Value = score1;
            Score2.Value = score2;
            Team1Combo.Value = combo1;
            Team2Combo.Value = combo2;
        }

        private (long Score, int Combo) getTeamTotals(TeamColour colour)
        {
            bool usePerformancePoints = ScoringMode.Value == TournamentScoringMode.PerformancePoint;
            long totalScore = 0;
            int totalCombo = 0;

            foreach (var score in GetTeamScore(colour))
            {
                long playerScore = usePerformancePoints
                    ? leaderboardProvider!.GetPlayerPerformancePoints(score.User.OnlineID)
                    : calculateModMultiplier(score);

                totalScore = checked(totalScore + playerScore);
                totalCombo = checked(totalCombo + score.Combo.Value);
            }

            return (totalScore, totalCombo);
        }

        protected virtual IEnumerable<GameplayLeaderboardScore> GetTeamScore(TeamColour colour)
        {
            int[] teamIds = GetTeamIds(colour);
            var scores = leaderboardProvider!.Scores;

            for (int i = 0; i < scores.Count; i++)
            {
                var score = scores[i];

                if (Array.IndexOf(teamIds, score.User.OnlineID) >= 0)
                    yield return score;
            }
        }

        public int[] GetTeamIds(TeamColour colour)
        {
            if (teamIdsCache.TryGetValue(colour, out int[]? ids))
                return ids;

            return teamIdsCache[colour] = Ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players
                                                .Select(p => p.OnlineID)
                                                .ToArray() ?? Array.Empty<int>();
        }

        public ColourInfo GetTeamColor(int userId)
        {
            if (GetTeamIds(TeamColour.Red).Contains(userId))
                return Color4Extensions.FromHex("#FB8B96");
            if (GetTeamIds(TeamColour.Blue).Contains(userId))
                return Color4Extensions.FromHex("#AFF0F7");

            return Color4.White;
        }

        private long calculateModMultiplier(GameplayLeaderboardScore score)
        {
            if (!userMultiplierCache.TryGetValue(score.User.OnlineID, out double multiplier))
            {
                Mod[] mods = leaderboardProvider?.GetPlayerMods(score.User.OnlineID) ?? Array.Empty<Mod>();

                multiplier = userMultiplierCache[score.User.OnlineID] = mods.Aggregate(
                    1.0,
                    (acc, mod) =>
                        acc * (Ladder.ModMultiplierSettings.FirstOrDefault(s => s.ModAcronym.Value == mod.Acronym)?.Multiplier.Value ?? 1.0));
            }

            return (long)(multiplier * score.TotalScore.Value);
        }

        internal virtual void ForceReSpectate()
        {
        }
    }
}
