// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Spectator;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.StableClient
{
    /// <summary>
    /// 绑定到特定用户 ID 的消费者，负责将 Stable 协议数据转换为 Lazer 可消费的 FrameDataBundle。
    /// </summary>
    public partial class StableSpectatorHandler : CompositeDrawable
    {
        public readonly int UserId;

        private readonly StableBanchoClient banchoClient;
        private readonly string username_credential;
        private readonly string passwordHash;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        /// <summary>
        /// 当前玩家加载的谱面。
        /// </summary>
        public readonly Bindable<WorkingBeatmap?> Beatmap = new Bindable<WorkingBeatmap?>();

        /// <summary>
        /// 当前谱面在线元数据。
        /// </summary>
        public readonly Bindable<APIBeatmap?> OnlineBeatmap = new Bindable<APIBeatmap?>();

        /// <summary>
        /// 当前玩家的规则集。
        /// </summary>
        public readonly Bindable<RulesetInfo?> Ruleset = new Bindable<RulesetInfo?>();

        /// <summary>
        /// 当新的回放数据包转换完成时触发。
        /// </summary>
        public event Action<FrameDataBundle>? OnFramesReceived;

        /// <summary>
        /// 当收到 stable 侧的非标准回放动作时触发。
        /// </summary>
        public event Action<ReplayAction, int>? OnReplayActionReceived;

        public string? Username { get; private set; }

        private const double spectate_retry_interval = 5000;
        private const double frame_timeout = 7000;

        private string? currentBeatmapHash;
        private LegacyMods currentLegacyMods;
        private bool currentScoreV2;
        private byte currentStatus;
        private GetBeatmapRequest? beatmapLookupRequest;
        private LegacyReplayFrame? lastReplayFrame;
        private bool receivedFramesForCurrentBeatmap;
        private double lastStatusUpdateTime;
        private double lastFrameReceivedTime;
        private double lastSpectateAttemptTime;

        public StableSpectatorHandler(int userId, string username, string passwordHash)
        {
            UserId = userId;
            this.username_credential = username;
            this.passwordHash = passwordHash;
            banchoClient = new StableBanchoClient();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(banchoClient);

            banchoClient.OnUserStatusChanged += handleUserStatus;
            banchoClient.OnReplayFramesReceived += handleReplayFrames;
            banchoClient.OnSpectatorJoined += handleSpectatorJoined;
            banchoClient.OnSpectatorLeft += handleSpectatorLeft;
            banchoClient.OnFellowSpectatorJoined += handleFellowSpectatorJoined;
            banchoClient.OnFellowSpectatorLeft += handleFellowSpectatorLeft;
            banchoClient.OnSpectatorCantSpectate += handleSpectatorCantSpectate;

            banchoClient.ConnectAsync(username_credential, passwordHash).ContinueWith(t =>
            {
                if (!t.IsFaulted)
                    Schedule(requestSpectate);
            });
        }

        private void handleUserStatus(StableUserStatus status)
        {
            if (status.UserId != UserId)
                return;

            Username = status.Username;
            currentLegacyMods = status.Mods;
            currentStatus = status.Status;
            lastStatusUpdateTime = Time.Current;

            if (currentBeatmapHash == status.BeatmapChecksum && Ruleset.Value?.OnlineID == status.PlayMode)
                return;

            currentBeatmapHash = status.BeatmapChecksum;
            receivedFramesForCurrentBeatmap = false;
            lastReplayFrame = null;
            OnlineBeatmap.Value = null;

            Schedule(() =>
            {
                var rulesetInfo = rulesets.GetRuleset(status.PlayMode);
                Ruleset.Value = rulesetInfo;

                var beatmap = beatmapManager.QueryBeatmap(b => b.MD5Hash == status.BeatmapChecksum);

                if (beatmap != null)
                {
                    Beatmap.Value = beatmapManager.GetWorkingBeatmap(beatmap);
                }
                else
                {
                    Logger.Log($"StableSpectatorHandler: Beatmap missing locally (Hash: {status.BeatmapChecksum}), attempting lookup...");

                    beatmapLookupRequest?.Cancel();
                    beatmapLookupRequest = new GetBeatmapRequest(md5Hash: status.BeatmapChecksum);
                    beatmapLookupRequest.Success += lookup => Schedule(() =>
                    {
                        if (currentBeatmapHash != status.BeatmapChecksum)
                            return;

                        OnlineBeatmap.Value = lookup;
                        Logger.Log($"StableSpectatorHandler: Found beatmap online: {lookup.BeatmapSet?.Title}");
                    });
                    beatmapLookupRequest.Failure += _ => Schedule(() =>
                    {
                        if (currentBeatmapHash != status.BeatmapChecksum)
                            return;

                        Logger.Log($"StableSpectatorHandler: Beatmap lookup failed for hash {status.BeatmapChecksum}");
                    });
                    api.Queue(beatmapLookupRequest);
                }
            });
        }

        public void RefreshBeatmapAvailability()
        {
            Schedule(() =>
            {
                if (string.IsNullOrEmpty(currentBeatmapHash))
                    return;

                var beatmap = beatmapManager.QueryBeatmap(b => b.MD5Hash == currentBeatmapHash);

                if (beatmap != null)
                    Beatmap.Value = beatmapManager.GetWorkingBeatmap(beatmap);
            });
        }

        private void handleReplayFrames(bReplayFrameBundle bundle)
        {
            lastFrameReceivedTime = Time.Current;
            receivedFramesForCurrentBeatmap = true;

            //Logger.Log(
            //    $"StableSpectatorHandler: action={bundle.Action}, extra={bundle.Extra}, frames={bundle.Frames.Count}, " +
            //    $"scoreTime={bundle.ScoreFrame.Time}, score={bundle.ScoreFrame.TotalScore}, combo={bundle.ScoreFrame.CurrentCombo}, passed={bundle.ScoreFrame.Pass}");

            if (bundle.Action != ReplayAction.Standard)
                OnReplayActionReceived?.Invoke(bundle.Action, bundle.Extra);

            if (bundle.Action == ReplayAction.WatchingOther)
                return;

            if (bundle.Action == ReplayAction.NewSong)
            {
                lastReplayFrame = null;
            }

            currentScoreV2 = bundle.ScoreFrame.ScoreV2;

            var convertedFrames = new List<LegacyReplayFrame>();

            foreach (var bFrame in bundle.Frames)
            {
                var replayFrame = new LegacyReplayFrame(bFrame.Time, bFrame.MouseX, bFrame.MouseY, (ReplayButtonState)bFrame.ButtonState);
                convertedFrames.Add(replayFrame);
                lastReplayFrame = replayFrame;
            }

            if (convertedFrames.Count > 0)
            {
                var first = convertedFrames[0];
                var last = convertedFrames[^1];
                //Logger.Log(
                //    $"StableSpectatorHandler: converted {convertedFrames.Count} legacy frames, " +
                //    $"first=({first.Time}, {first.MouseX}, {first.MouseY}, {first.ButtonState}), " +
                //    $"last=({last.Time}, {last.MouseX}, {last.MouseY}, {last.ButtonState})");
            }

            if (convertedFrames.Count == 0)
            {
                if (bundle.Action == ReplayAction.Standard)
                    Logger.Log("StableSpectatorHandler: Received standard replay bundle without frames.");

                return;
            }

            var rulesetInfo = Ruleset.Value;

            if (rulesetInfo == null)
            {
                Logger.Log($"StableSpectatorHandler: Received replay frames before ruleset was known. BeatmapHash={currentBeatmapHash ?? "<null>"}");
                return;
            }

            var rulesetInstance = rulesetInfo.CreateInstance();
            var scoreState = computeScoreState(bundle.ScoreFrame, rulesetInfo.OnlineID);

            var scoreInfo = new ScoreInfo
            {
                User = new APIUser { Id = UserId, Username = Username ?? UserId.ToString() },
                Ruleset = rulesetInfo,
                BeatmapInfo = Beatmap.Value?.BeatmapInfo,
                BeatmapHash = currentBeatmapHash ?? string.Empty,
                TotalScore = bundle.ScoreFrame.TotalScore,
                TotalScoreWithoutMods = bundle.ScoreFrame.TotalScore,
                MaxCombo = bundle.ScoreFrame.MaxCombo,
                Accuracy = scoreState.accuracy,
                Combo = bundle.ScoreFrame.CurrentCombo,
                Passed = bundle.ScoreFrame.Pass,
                Mods = CreateMods(rulesetInstance)
            };

            scoreInfo.SetCount300(bundle.ScoreFrame.Count300);
            scoreInfo.SetCount100(bundle.ScoreFrame.Count100);
            scoreInfo.SetCount50(bundle.ScoreFrame.Count50);
            scoreInfo.SetCountGeki(bundle.ScoreFrame.CountGeki);
            scoreInfo.SetCountKatu(bundle.ScoreFrame.CountKatu);
            scoreInfo.SetCountMiss(bundle.ScoreFrame.CountMiss);

            var header = new FrameHeader(scoreInfo, scoreState.statistics)
            {
                ScoreSource = FrameScoreSource.StableRaw,
                Passed = bundle.ScoreFrame.Pass
            };

            //Logger.Log(
            //    $"StableSpectatorHandler: emitting bundle for ruleset={rulesetInfo.ShortName}, " +
            //    $"headerScore={header.TotalScore}, headerAcc={header.Accuracy:P2}, headerCombo={header.Combo}, frameCount={convertedFrames.Count}");

            convertedFrames[^1].Header = header;

            var lazerBundle = new FrameDataBundle(header, convertedFrames);
            OnFramesReceived?.Invoke(lazerBundle);
        }

        protected override void Update()
        {
            base.Update();

            if (Time.Current - lastSpectateAttemptTime < spectate_retry_interval)
                return;

            if (!isActiveGameplayStatus(currentStatus))
                return;

            if (string.IsNullOrEmpty(currentBeatmapHash))
                return;

            bool needsRetry = !receivedFramesForCurrentBeatmap
                              ? Time.Current - lastStatusUpdateTime >= spectate_retry_interval
                              : Time.Current - lastFrameReceivedTime >= frame_timeout;

            if (!needsRetry)
                return;

            Logger.Log(
                $"StableSpectatorHandler: no replay frames for user {UserId} while status={currentStatus}, " +
                $"re-requesting spectate (beatmapHash={currentBeatmapHash ?? "<null>"}).");

            requestSpectate();
        }

        private void requestSpectate()
        {
            lastSpectateAttemptTime = Time.Current;
            banchoClient.StartSpectating(UserId);
        }

        private void handleSpectatorJoined(int userId)
        {
            if (userId == banchoClient.LocalUserId)
                Logger.Log($"StableSpectatorHandler: server confirmed spectator join for target {UserId}.");
        }

        private void handleSpectatorLeft(int userId)
        {
            if (userId == banchoClient.LocalUserId)
            {
                Logger.Log($"StableSpectatorHandler: server reported local spectator left for target {UserId}, requesting spectate again.");
                Schedule(requestSpectate);
            }
        }

        private void handleFellowSpectatorJoined(int userId)
        {
            Logger.Log($"StableSpectatorHandler: fellow spectator {userId} joined target {UserId}.");
        }

        private void handleFellowSpectatorLeft(int userId)
        {
            Logger.Log($"StableSpectatorHandler: fellow spectator {userId} left target {UserId}.");
        }

        private void handleSpectatorCantSpectate(int userId)
        {
            Logger.Log($"StableSpectatorHandler: spectator {userId} cannot spectate target {UserId}.");
        }

        private static bool isActiveGameplayStatus(byte status) =>
            status is 2 or 6 or 8 or 10 or 12;

        public Mod[] CreateMods(Ruleset rulesetInstance)
        {
            var mods = rulesetInstance.ConvertFromLegacyMods(currentLegacyMods)
                                      .Where(m => m is not ModClassic && m is not ModScoreV2)
                                      .ToList();

            if (currentScoreV2)
            {
                mods.Add(rulesetInstance.ConvertFromLegacyMods(LegacyMods.ScoreV2).First());
            }
            else if (rulesetInstance.CreateMod<ModClassic>() is ModClassic classicMod)
            {
                mods.Add(classicMod);
            }

            return mods.ToArray();
        }

        private static (double accuracy, ScoreProcessorStatistics statistics) computeScoreState(bScoreFrame scoreFrame, int rulesetId)
        {
            int totalJudgements;
            double baseScore;
            double maximumBaseScore;
            double emptyAccuracy = 1;

            switch (rulesetId)
            {
                case 1:
                    totalJudgements = scoreFrame.Count300 + scoreFrame.Count100 + scoreFrame.CountMiss;
                    baseScore = scoreFrame.Count300 * 300 + scoreFrame.Count100 * 150;
                    maximumBaseScore = totalJudgements * 300;
                    emptyAccuracy = 0;
                    break;

                case 2:
                    totalJudgements = scoreFrame.Count300 + scoreFrame.Count100 + scoreFrame.Count50 + scoreFrame.CountKatu + scoreFrame.CountMiss;
                    baseScore = scoreFrame.Count300 + scoreFrame.Count100 + scoreFrame.Count50;
                    maximumBaseScore = totalJudgements;
                    break;

                case 3:
                    totalJudgements = scoreFrame.Count300 + scoreFrame.Count100 + scoreFrame.Count50 + scoreFrame.CountGeki + scoreFrame.CountKatu + scoreFrame.CountMiss;

                    if (scoreFrame.ScoreV2)
                    {
                        baseScore = scoreFrame.Count50 * 50
                                    + scoreFrame.Count100 * 100
                                    + scoreFrame.CountKatu * 200
                                    + scoreFrame.Count300 * 300
                                    + scoreFrame.CountGeki * 305;
                        maximumBaseScore = totalJudgements * 305;
                    }
                    else
                    {
                        baseScore = scoreFrame.Count50 * 50
                                    + scoreFrame.Count100 * 100
                                    + scoreFrame.CountKatu * 200
                                    + (scoreFrame.Count300 + scoreFrame.CountGeki) * 300;
                        maximumBaseScore = totalJudgements * 300;
                    }

                    break;

                default:
                    totalJudgements = scoreFrame.Count300 + scoreFrame.Count100 + scoreFrame.Count50 + scoreFrame.CountMiss;
                    baseScore = scoreFrame.Count300 * 300
                                + scoreFrame.Count100 * 100
                                + scoreFrame.Count50 * 50;
                    maximumBaseScore = totalJudgements * 300;
                    break;
            }

            double accuracy = maximumBaseScore > 0 ? baseScore / maximumBaseScore : emptyAccuracy;

            return (accuracy, new ScoreProcessorStatistics
            {
                BaseScore = baseScore,
                MaximumBaseScore = maximumBaseScore,
                AccuracyJudgementCount = totalJudgements,
                ComboPortion = scoreFrame.ComboPortion,
                BonusPortion = scoreFrame.BonusPortion
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            banchoClient.OnUserStatusChanged -= handleUserStatus;
            banchoClient.OnReplayFramesReceived -= handleReplayFrames;
            banchoClient.OnSpectatorJoined -= handleSpectatorJoined;
            banchoClient.OnSpectatorLeft -= handleSpectatorLeft;
            banchoClient.OnFellowSpectatorJoined -= handleFellowSpectatorJoined;
            banchoClient.OnFellowSpectatorLeft -= handleFellowSpectatorLeft;
            banchoClient.OnSpectatorCantSpectate -= handleSpectatorCantSpectate;
            base.Dispose(isDisposing);
        }
    }
}
