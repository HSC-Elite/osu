// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Rooms;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.StableClient.IPC
{
    public partial class StableMatchIPCInfo : MatchIPCInfo
    {
        [Resolved]
        private StableBanchoClient client { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        [Resolved]
        private TournamentGameBase game { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        public readonly Bindable<MultiplayerMatch?> CurrentMatch = new Bindable<MultiplayerMatch?>();

        private readonly StableBeatmapAvailabilityTracker beatmapAvailabilityTracker = new StableBeatmapAvailabilityTracker();

        private double waitingForIdle;
        private const int time_to_idle_from_ranking = 15000;

        private string credentialsUsername = string.Empty;
        private string credentialsPasswordHash = string.Empty;

        private readonly StableSpectatorHandler?[] spectatorHandlers = new StableSpectatorHandler?[16];
        private readonly Dictionary<int, long> userScores = new Dictionary<int, long>();

        private int lastBeatmapId;

        public StableMatchIPCInfo()
        {
        }

        public void SetCredentials(string username, string passwordHash)
        {
            credentialsUsername = username;
            credentialsPasswordHash = passwordHash;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            game.Add(beatmapAvailabilityTracker);

            client.OnMatchCreated += onMatchUpdated;
            client.OnMatchUpdated += onMatchUpdated;
            client.OnMatchDisbanded += onMatchDisbanded;

            CurrentMatch.BindValueChanged(e =>
            {
                if (e.NewValue != null)
                {
                    updateMatchState(e.NewValue, true);
                    updateSpectatorHandlers(e.NewValue);
                }
            });

            beatmapAvailabilityTracker.Availability.BindValueChanged(avail =>
            {
                if (avail.NewValue.State == DownloadState.LocallyAvailable && lastBeatmapId > 0)
                {
                    var local = beatmaps.QueryBeatmap(b => b.OnlineID == lastBeatmapId);
                    if (local != null)
                        Schedule(() => workingBeatmap.Value = beatmaps.GetWorkingBeatmap(local));
                }
            });
        }

        private void onMatchUpdated(MultiplayerMatch match)
        {
            if (CurrentMatch.Value?.Id == match.Id)
            {
                var previousMatch = CurrentMatch.Value;
                CurrentMatch.Value = match;

                if (previousMatch != null && !previousMatch.InProgress && match.InProgress)
                {
                    State.Value = TourneyState.Playing;
                    userScores.Clear(); 
                }
                else if (previousMatch != null && previousMatch.InProgress && !match.InProgress)
                {
                    State.Value = TourneyState.Ranking;
                    waitingForIdle = 0;
                }

                updateMatchState(match, false);
                updateSpectatorHandlers(match);
            }
        }

        private void onMatchDisbanded(int matchId)
        {
            if (CurrentMatch.Value?.Id == matchId)
            {
                CurrentMatch.Value = null;
                State.Value = TourneyState.Idle;
                clearSpectatorHandlers();
            }
        }

        private void updateMatchState(MultiplayerMatch match, bool forceUpdate)
        {
            if (forceUpdate || match.BeatmapId != lastBeatmapId)
            {
                lastBeatmapId = match.BeatmapId;

                beatmapAvailabilityTracker.PlaylistItem.Value = new PlaylistItem(new APIBeatmap { OnlineID = match.BeatmapId });

                var existing = ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == match.BeatmapId);

                if (existing != null)
                {
                    Beatmap.Value = existing.Beatmap;
                }
                else
                {
                    beatmapLookupCache.GetBeatmapAsync(match.BeatmapId).ContinueWith(t =>
                    {
                        var apiBeatmap = t.GetResultSafely();
                        if (apiBeatmap != null && lastBeatmapId == match.BeatmapId)
                        {
                            Schedule(() => Beatmap.Value = new TournamentBeatmap(apiBeatmap));
                        }
                    });
                }

                var localBeatmap = beatmaps.QueryBeatmap(b => b.OnlineID == match.BeatmapId);
                if (localBeatmap != null)
                {
                    Schedule(() => workingBeatmap.Value = beatmaps.GetWorkingBeatmap(localBeatmap));
                }
                else
                {
                    beatmapLookupCache.GetBeatmapAsync(match.BeatmapId).ContinueWith(t =>
                    {
                        var apiBeatmap = t.GetResultSafely();
                        if (apiBeatmap?.BeatmapSet != null)
                        {
                            if (!beatmaps.IsAvailableLocally(new BeatmapSetInfo { OnlineID = apiBeatmap.BeatmapSet.OnlineID }))
                                beatmapDownloader.Download(apiBeatmap.BeatmapSet);
                        }
                    });
                }
            }

            var rulesetInfo = rulesets.GetRuleset(match.PlayMode);
            if (rulesetInfo != null)
                Schedule(() => ruleset.Value = rulesetInfo);

            Mods.Value = (LegacyMods)match.Mods;

            fetchChannelId(match.Id);
        }

        private void fetchChannelId(int matchId)
        {
            if (string.IsNullOrEmpty(credentialsUsername) || string.IsNullOrEmpty(credentialsPasswordHash))
                return;

            Task.Run(async () =>
            {
                try
                {
                    var req = new OsuWebRequest(
                        $"https://osu.ppy.sh/web/osu-getchannelid.php?u={Uri.EscapeDataString(credentialsUsername)}&h={Uri.EscapeDataString(credentialsPasswordHash)}&mp={matchId}");
                    await req.PerformAsync().ConfigureAwait(false);

                    if (int.TryParse(req.GetResponseString(), out int channelId) && channelId > 0)
                    {
                        Schedule(() => ChatChannel.Value = channelId.ToString());
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to retrieve multiplayer channel ID.");
                }
            });
        }

        private void updateSpectatorHandlers(MultiplayerMatch match)
        {
            for (int i = 0; i < 16; i++)
            {
                int userId = match.SlotUserIds[i];

                if (spectatorHandlers[i]?.UserId != userId)
                {
                    if (spectatorHandlers[i] != null)
                    {
                        spectatorHandlers[i]!.Expire();
                        spectatorHandlers[i] = null;
                    }

                    if (userId > 0)
                    {
                        var handler = new StableSpectatorHandler(userId, credentialsUsername, credentialsPasswordHash);
                        handler.OnFramesReceived += bundle => onFramesReceived(userId, bundle);
                        spectatorHandlers[i] = handler;
                        game.Add(handler);
                    }
                }
            }
        }

        private void clearSpectatorHandlers()
        {
            for (int i = 0; i < 16; i++)
            {
                if (spectatorHandlers[i] != null)
                {
                    spectatorHandlers[i]!.Expire();
                    spectatorHandlers[i] = null;
                }
            }
        }

        private void onFramesReceived(int userId, FrameDataBundle bundle)
        {
            userScores[userId] = bundle.Header.TotalScore;
        }

        protected override void Update()
        {
            base.Update();

            if (State.Value == TourneyState.Ranking)
            {
                waitingForIdle += Time.Elapsed;
                if (waitingForIdle >= time_to_idle_from_ranking)
                    State.Value = TourneyState.Idle;
            }
            else
            {
                waitingForIdle = 0;
            }

            updateTeamScores();
        }

        private void updateTeamScores()
        {
            var match = CurrentMatch.Value;
            if (match == null) return;

            long blueScore = 0;
            long redScore = 0;

            for (int i = 0; i < 16; i++)
            {
                int userId = match.SlotUserIds[i];

                if (userId > 0 && userScores.TryGetValue(userId, out long score))
                {
                    if (match.SlotTeams[i] == 1) // Blue
                        blueScore += score;
                    else if (match.SlotTeams[i] == 2) // Red
                        redScore += score;
                }
            }

            Score1.Value = blueScore;
            Score2.Value = redScore;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (client != null)
            {
                client.OnMatchCreated -= onMatchUpdated;
                client.OnMatchUpdated -= onMatchUpdated;
                client.OnMatchDisbanded -= onMatchDisbanded;
            }

            clearSpectatorHandlers();
            base.Dispose(isDisposing);
        }

        private partial class StableBeatmapAvailabilityTracker : OnlinePlayBeatmapAvailabilityTracker
        {
            public new Bindable<PlaylistItem?> PlaylistItem => base.PlaylistItem;
        }
    }
}
