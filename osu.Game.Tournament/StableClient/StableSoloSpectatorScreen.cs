// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Spectator;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osu.Game.Screens.Play;
using osu.Game.Scoring;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableSoloSpectatorScreen : OsuScreen
    {
        public override string Title => "Stable Solo Spectator";

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        private OsuTextBox usernameBox = null!;
        private OsuPasswordTextBox passwordBox = null!;
        private OsuTextBox userIdBox = null!;
        private PurpleRoundedButton connectButton = null!;
        private OsuSpriteText statusText = null!;
        private Container userPanelContainer = null!;
        private Container beatmapPanelContainer = null!;
        private SettingsCheckbox automaticDownload = null!;
        private Container trackerContainer = null!;

        private readonly List<FrameDataBundle> pendingBundles = new List<FrameDataBundle>();

        private StableSpectatorHandler? handler;
        private Score? score;
        private bool playerPushed;
        private ScheduledDelegate? beatmapFetchCallback;
        private APIBeatmapSet? beatmapSet;
        private BeatmapDownloadTracker? downloadTracker;
        private int displayedUserId;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            InternalChild = new Container
            {
                Masking = true,
                CornerRadius = 20,
                AutoSizeAxes = Axes.Both,
                AutoSizeDuration = 500,
                AutoSizeEasing = Easing.OutQuint,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    new Box
                    {
                        Colour = colourProvider.Background5,
                        RelativeSizeAxes = Axes.Both,
                    },
                    new FillFlowContainer
                    {
                        Margin = new MarginPadding(20),
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Spacing = new Vector2(15),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "Stable Spectator Mode",
                                Font = OsuFont.Default.With(size: 30),
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Spacing = new Vector2(10),
                                Children = new Drawable[]
                                {
                                    usernameBox = new OsuTextBox
                                    {
                                        Width = 320,
                                        PlaceholderText = "Username",
                                    },
                                    passwordBox = new OsuPasswordTextBox
                                    {
                                        Width = 320,
                                        PlaceholderText = "Password (MD5 or Plain)",
                                    },
                                    userIdBox = new OsuTextBox
                                    {
                                        Width = 320,
                                        PlaceholderText = "Target User ID",
                                    },
                                    connectButton = new PurpleRoundedButton
                                    {
                                        Width = 320,
                                        Text = "Connect & Spectate",
                                        Action = connectAndSpectate,
                                    },
                                }
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Spacing = new Vector2(15),
                                Children = new Drawable[]
                                {
                                    userPanelContainer = new Container
                                    {
                                        AutoSizeAxes = Axes.Both,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Child = createPlaceholderPanel("Waiting for target user")
                                    },
                                    new SpriteIcon
                                    {
                                        Size = new Vector2(40),
                                        Icon = FontAwesome.Solid.ArrowRight,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                    beatmapPanelContainer = new Container
                                    {
                                        AutoSizeAxes = Axes.Both,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Child = createPlaceholderPanel("Waiting for beatmap info")
                                    },
                                }
                            },
                            automaticDownload = new SettingsCheckbox
                            {
                                LabelText = OnlineSettingsStrings.AutomaticallyDownloadMissingBeatmaps,
                                Current = config.GetBindable<bool>(OsuSetting.AutomaticallyDownloadMissingBeatmaps),
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            },
                            statusText = new OsuSpriteText
                            {
                                Text = "Idle",
                                Font = OsuFont.Default.With(size: 20),
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            }
                        }
                    },
                    trackerContainer = new Container()
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            automaticDownload.Current.BindValueChanged(_ => checkForAutomaticDownload());
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            if (playerPushed && this.GetChildScreen() == null)
                resetGameplayState("Waiting for replay frames...");
        }

        private void connectAndSpectate()
        {
            if (!int.TryParse(userIdBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int userId) || userId <= 0)
            {
                setStatus("Invalid user id.");
                return;
            }

            string username = usernameBox.Text.Trim();
            string passwordHash = hashPasswordIfNeeded(passwordBox.Text.Trim());

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordHash))
            {
                setStatus("Username and password are required.");
                return;
            }

            clearCurrentSession();

            connectButton.Enabled.Value = false;
            setStatus($"Connecting and spectating user {userId}...");
            updateUserPanel(new APIUser
            {
                Id = userId,
                Username = username,
            });
            displayedUserId = userId;
            beatmapPanelContainer.Child = createPlaceholderPanel("Waiting for beatmap info");

            handler = new StableSpectatorHandler(userId, username, passwordHash);
            handler.Beatmap.BindValueChanged(beatmap => Schedule(() =>
            {
                if (playerPushed && score?.ScoreInfo.BeatmapInfo?.OnlineID != beatmap.NewValue?.BeatmapInfo.OnlineID)
                    resetGameplayState("Beatmap changed. Waiting for first frames...");

                refreshDisplayedUser();
                refreshBeatmapDisplay();
                tryStartGameplay();
            }));
            handler.OnlineBeatmap.BindValueChanged(_ => Schedule(refreshBeatmapDisplay));
            handler.Ruleset.BindValueChanged(_ => Schedule(tryStartGameplay));
            handler.OnFramesReceived += onFramesReceived;
            handler.OnReplayActionReceived += onReplayActionReceived;
            AddInternal(handler);

            connectButton.Enabled.Value = true;
            connectButton.Text = "Reconnect & Spectate";
        }

        private void onFramesReceived(FrameDataBundle bundle)
        {
            Schedule(() =>
            {
                pendingBundles.Add(bundle);
                Logger.Log(
                    $"StableSoloSpectatorScreen: received bundle #{pendingBundles.Count}, frames={bundle.Frames.Count}, " +
                    $"score={bundle.Header.TotalScore}, acc={bundle.Header.Accuracy:P2}, " +
                    $"firstTime={(bundle.Frames.Count > 0 ? bundle.Frames[0].Time : -1)}, lastTime={(bundle.Frames.Count > 0 ? bundle.Frames[^1].Time : -1)}");
                refreshDisplayedUser();
                setStatus($"Receiving frames... score {bundle.Header.TotalScore}, acc {bundle.Header.Accuracy:P2}");
                tryStartGameplay();
            });
        }

        private void onReplayActionReceived(Protocol.ReplayAction action, int extra)
        {
            Schedule(() =>
            {
                switch (action)
                {
                    case Protocol.ReplayAction.NewSong:
                        resetGameplayState("New song detected. Waiting for first frames...");
                        break;

                    case Protocol.ReplayAction.Completion:
                        resetGameplayState("Player finished. Waiting for next beatmap...");
                        break;

                    case Protocol.ReplayAction.Fail:
                        resetGameplayState("Player failed. Waiting for next beatmap...");
                        break;

                    case Protocol.ReplayAction.WatchingOther:
                        resetGameplayState("Target changed. Waiting for next replay target...");
                        break;

                    default:
                        setStatus($"Replay action: {action} ({extra})");
                        break;
                }
            });
        }

        private void tryStartGameplay()
        {
            if (handler?.Beatmap.Value == null || handler.Ruleset.Value == null || pendingBundles.Count == 0)
                return;

            if (score == null)
            {
                var ruleset = handler.Ruleset.Value.CreateInstance();
                Mod[] mods = handler.CreateMods(ruleset);

                score = new Score
                {
                    ScoreInfo = new ScoreInfo
                    {
                        User = new APIUser
                        {
                            Id = handler.UserId,
                            Username = handler.Username ?? handler.UserId.ToString(),
                        },
                        BeatmapInfo = handler.Beatmap.Value.BeatmapInfo,
                        Ruleset = handler.Ruleset.Value,
                        Mods = mods,
                    },
                    Replay = new Replay(),
                };

                foreach (var bundle in pendingBundles)
                    appendBundle(score, handler.Beatmap.Value, ruleset, bundle);

                score.Replay.Frames = score.Replay.Frames.OrderBy(f => f.Time).ToList();

                Logger.Log(
                    $"StableSoloSpectatorScreen: created replay score with {score.Replay.Frames.Count} converted frames " +
                    $"across {pendingBundles.Count} buffered bundles for beatmap={handler.Beatmap.Value.BeatmapInfo.OnlineID} ruleset={handler.Ruleset.Value.ShortName}");

                Beatmap.Value = handler.Beatmap.Value;
                Ruleset.Value = handler.Ruleset.Value;
                Mods.Value = mods;
            }

            if (playerPushed || score == null)
                return;

            playerPushed = true;
            connectButton.Enabled.Value = true;
            setStatus("Gameplay started.");
            Logger.Log($"StableSoloSpectatorScreen: pushing PlayerLoader with replay frame count={score.Replay.Frames.Count}");

            this.Push(new PlayerLoader(() =>
            {
                var player = new StableSoloSpectatorPlayer(score, handler);
                player.PlayerFinished += onPlayerFinished;
                return player;
            }));
        }

        private void onPlayerFinished()
        {
            Schedule(() => resetGameplayState("Player finished. Waiting for next beatmap..."));
        }

        private void refreshDisplayedUser()
        {
            if (handler == null)
                return;

            updateUserPanel(new APIUser
            {
                Id = displayedUserId > 0 ? displayedUserId : handler.UserId,
                Username = handler.Username ?? displayedUserId.ToString(),
            });
        }

        private void refreshBeatmapDisplay()
        {
            beatmapFetchCallback?.Cancel();

            if (handler?.OnlineBeatmap.Value?.BeatmapSet != null)
            {
                showBeatmapPanel(handler.OnlineBeatmap.Value);
                return;
            }

            var localBeatmap = handler?.Beatmap.Value?.BeatmapInfo;
            if (localBeatmap?.OnlineID > 0)
            {
                beatmapLookupCache.GetBeatmapAsync(localBeatmap.OnlineID).ContinueWith(t => beatmapFetchCallback = Schedule(() =>
                {
                    var beatmap = t.GetResultSafely();

                    if (beatmap?.BeatmapSet != null)
                        showBeatmapPanel(beatmap);
                }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }

        private void showBeatmapPanel(APIBeatmap beatmap)
        {
            if (beatmap.BeatmapSet == null)
                return;

            beatmapSet = beatmap.BeatmapSet;
            beatmapPanelContainer.Child = new BeatmapCardNormal(beatmapSet, allowExpansion: false);
            attachDownloadTracker(beatmapSet);
            checkForAutomaticDownload();
        }

        private void attachDownloadTracker(APIBeatmapSet set)
        {
            if (downloadTracker != null)
            {
                downloadTracker.State.UnbindAll();
                trackerContainer.Remove(downloadTracker, true);
                downloadTracker = null;
            }

            trackerContainer.Add(downloadTracker = new BeatmapDownloadTracker(set));
            downloadTracker.State.BindValueChanged(state =>
            {
                if (state.NewValue == DownloadState.LocallyAvailable)
                {
                    handler?.RefreshBeatmapAvailability();
                    refreshBeatmapDisplay();
                }
            }, true);
        }

        private static void appendBundle(Score score, Beatmaps.WorkingBeatmap beatmap, Rulesets.Ruleset ruleset, FrameDataBundle bundle)
        {
            Rulesets.Replays.ReplayFrame? lastFrame = score.Replay.Frames.LastOrDefault();

            foreach (var frame in bundle.Frames)
            {
                var convertibleFrame = ruleset.CreateConvertibleReplayFrame()!;
                convertibleFrame.FromLegacy(frame, beatmap.Beatmap, lastFrame);

                var convertedFrame = (Rulesets.Replays.ReplayFrame)convertibleFrame;
                convertedFrame.Time = frame.Time;
                convertedFrame.Header = frame.Header;

                score.Replay.Frames.Add(convertedFrame);
                lastFrame = convertedFrame;
            }
        }

        private void checkForAutomaticDownload()
        {
            if (beatmapSet == null)
                return;

            if (!automaticDownload.Current.Value)
                return;

            if (beatmaps.IsAvailableLocally(new BeatmapSetInfo { OnlineID = beatmapSet.OnlineID }))
                return;

            beatmapDownloader.Download(beatmapSet);
        }

        private void clearCurrentSession()
        {
            resetGameplayState("Idle", clearBeatmapPanel: true, exitChildScreen: true);

            displayedUserId = 0;
            beatmapSet = null;
            beatmapFetchCallback?.Cancel();
            beatmapFetchCallback = null;

            if (downloadTracker != null)
            {
                downloadTracker.State.UnbindAll();
                trackerContainer.Remove(downloadTracker, true);
                downloadTracker = null;
            }

            if (handler != null)
            {
                handler.Beatmap.UnbindAll();
                handler.OnlineBeatmap.UnbindAll();
                handler.Ruleset.UnbindAll();
                handler.OnFramesReceived -= onFramesReceived;
                handler.OnReplayActionReceived -= onReplayActionReceived;
                handler.Expire();
                handler = null;
            }

            userPanelContainer.Child = createPlaceholderPanel("Waiting for target user");
            beatmapPanelContainer.Child = createPlaceholderPanel("Waiting for beatmap info");
        }

        private void resetGameplayState(string status, bool clearBeatmapPanel = false, bool exitChildScreen = true)
        {
            pendingBundles.Clear();
            score = null;
            playerPushed = false;

            if (exitChildScreen)
            {
                if (this.GetChildScreen() is PlayerLoader loader && loader.CurrentPlayer is StableSoloSpectatorPlayer player)
                    player.PlayerFinished -= onPlayerFinished;

                this.GetChildScreen()?.Exit();
            }

            if (clearBeatmapPanel)
                beatmapPanelContainer.Child = createPlaceholderPanel("Waiting for beatmap info");

            setStatus(status);
        }

        private void setStatus(string text)
        {
            statusText.Text = text;
        }

        private void updateUserPanel(APIUser user)
        {
            userPanelContainer.Child = new UserGridPanel(user)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Height = 145,
                Width = 290,
            };
        }

        private Drawable createPlaceholderPanel(string text) =>
            new Container
            {
                Width = BeatmapCardNormal.WIDTH,
                Height = BeatmapCardNormal.HEIGHT,
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background4,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text,
                        Font = OsuFont.Default.With(size: 16),
                    }
                }
            };

        private static string hashPasswordIfNeeded(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length == 32)
                return password;

            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(password));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public override bool OnExiting(osu.Framework.Screens.ScreenExitEvent e)
        {
            clearCurrentSession();
            connectButton.Enabled.Value = true;
            return base.OnExiting(e);
        }
    }
}
