// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ExceptionExtensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea;

namespace osu.Game.Tournament.IPC
{
    public partial class MatchIPCInfo : CompositeComponent
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
        public Bindable<TourneyState> State { get; } = new Bindable<TourneyState>();
        public Bindable<Channel?> ChatChannel { get; } = new Bindable<Channel?>();
        public BindableLong Score1 { get; } = new BindableLong();
        public BindableLong Score2 { get; } = new BindableLong();

        public BindableInt Team1Combo { get; } = new BindableInt();
        public BindableInt Team2Combo { get; } = new BindableInt();

        public BindableBool HasActiveMatch { get; } = new BindableBool();

        protected readonly BindableList<MatchRoomPlayerInfo> RoomPlayersInternal = new BindableList<MatchRoomPlayerInfo>();

        public IBindableList<MatchRoomPlayerInfo> RoomPlayers => RoomPlayersInternal;

        public MatchIPCInfo()
        {
            State.BindValueChanged(s => Logger.Log($"Tourney State turn {s.OldValue} to {s.NewValue}"));
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

        private void updateTrackLooping() => workingBeatmap.Value.PrepareTrackForPreview(State.Value != TourneyState.Idle);

        protected override void Update()
        {
            base.Update();

            if (State.Value == TourneyState.Playing)
                updateScore();
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

        private void updateScore()
        {
            if (leaderboardProvider == null)
                return;

            GameplayLeaderboardScore[] team1Score = GetTeamScore(TeamColour.Red).ToArray();
            GameplayLeaderboardScore[] team2Score = GetTeamScore(TeamColour.Blue).ToArray();

            Score1.Value = team1Score.Sum(CalculateModMultiplier);
            Score2.Value = team2Score.Sum(CalculateModMultiplier);
            Team1Combo.Value = team1Score.Sum(s => s.Combo.Value);
            Team2Combo.Value = team2Score.Sum(s => s.Combo.Value);
        }

        protected virtual IEnumerable<GameplayLeaderboardScore> GetTeamScore(TeamColour colour)
        {
            int[] teamIds = GetTeamIds(colour);
            return leaderboardProvider!.Scores.Where(u => teamIds.Any(t => t == u.User.OnlineID));
        }

        protected int[] GetTeamIds(TeamColour colour)
        {
            if (teamIdsCache.TryGetValue(colour, out int[]? ids))
                return ids;

            return teamIdsCache[colour] = Ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players
                                                       .Select(p => p.OnlineID)
                                                       .ToArray() ?? Array.Empty<int>();
        }

        private long CalculateModMultiplier(GameplayLeaderboardScore score)
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
