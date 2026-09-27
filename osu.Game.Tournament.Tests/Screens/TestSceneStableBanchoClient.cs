// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Beatmaps;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.StableClient;
using osu.Game.Tournament.StableClient.IPC;
using osu.Game.Tournament.StableClient.Protocol;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Tests.Screens
{
    [TestFixture]
    public partial class TestSceneStableBanchoClient : TournamentScreenTestScene
    {
        protected override bool UseOnlineAPI => true;

        [Resolved]
        private StableBanchoClient sharedBanchoClient { get; set; } = null!;

        [Resolved]
        private StableMatchIPCInfo stableIPC { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private readonly Dictionary<int, MultiplayerMatch> matches = new Dictionary<int, MultiplayerMatch>();

        private StableBanchoClient? client;

        private LabelledTextBox usernameTextBox = null!;
        private LabelledPasswordTextBox passwordTextBox = null!;
        private TournamentSpriteText statusText = null!;
        private TournamentSpriteText replayStatusText = null!;
        private FillFlowContainer roomList = null!;

        [Test]
        public void TestLocalBanchoLoginAndLiveMatch()
        {
            string username = Environment.GetEnvironmentVariable("OSU_TOURNAMENT_BANCHO_USERNAME") ?? string.Empty;
            string passwordHash = Environment.GetEnvironmentVariable("OSU_TOURNAMENT_BANCHO_PASSWORD_HASH") ?? string.Empty;
            string simulatorBaseUrl = Environment.GetEnvironmentVariable("OSU_TOURNAMENT_BANCHO_SIMULATOR_URL") ?? string.Empty;
            string simulatorHost = Environment.GetEnvironmentVariable("OSU_TOURNAMENT_BANCHO_HOST") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(simulatorBaseUrl) || string.IsNullOrWhiteSpace(simulatorHost))
                Assert.Ignore("Set the OSU_TOURNAMENT_BANCHO_* test variables to run this local integration test.");

            var receivedMatches = new ConcurrentQueue<MultiplayerMatch>();
            Task<bool>? connectTask = null;
            Task<bool>? completeSimulatorTask = null;
            Task<bool>? resetSimulatorTask = null;

            AddStep("set Stable IPC credentials and observe packets", () =>
            {
                stableIPC.SetCredentials(username, passwordHash);
                Action<MultiplayerMatch> observeAndSelectMatch = match =>
                {
                    receivedMatches.Enqueue(match);
                    Schedule(() =>
                    {
                        if (stableIPC.CurrentMatch.Value?.Id == null || stableIPC.CurrentMatch.Value.Id == match.Id)
                            stableIPC.CurrentMatch.Value = match;
                    });
                };

                sharedBanchoClient.OnMatchCreated += observeAndSelectMatch;
                sharedBanchoClient.OnMatchUpdated += observeAndSelectMatch;
            });
            AddStep("connect shared Stable client", () => connectTask = sharedBanchoClient.ConnectAsync(username, passwordHash));
            AddUntilStep("login completes", () => connectTask?.IsCompleted == true);
            AddAssert("login succeeds", () => connectTask?.IsCompletedSuccessfully == true && connectTask.GetResultSafely());

            AddStep("join lobby", () => sharedBanchoClient.JoinLobby());
            AddUntilStep("client receives in-progress match packet", () => receivedMatches.Any(match => match.InProgress));
            AddUntilStep("Stable IPC receives in-progress match", () => stableIPC.CurrentMatch.Value?.InProgress == true);
            AddAssert("live match state reflects beatmap availability", () =>
            {
                var match = stableIPC.CurrentMatch.Value;
                if (match?.InProgress != true)
                    return false;

                bool beatmapAvailableLocally = beatmaps.QueryBeatmap(b => b.OnlineID == match.BeatmapId) != null;
                return stableIPC.State.Value == (beatmapAvailableLocally ? TourneyState.Playing : TourneyState.Idle);
            });

            AddStep("complete simulator match", () => completeSimulatorTask = postSimulatorActionAsync(simulatorBaseUrl, simulatorHost, "complete"));
            AddUntilStep("simulator completes match", () => completeSimulatorTask?.IsCompleted == true);
            AddAssert("simulator completion request succeeds", () => completeSimulatorTask?.IsCompletedSuccessfully == true && completeSimulatorTask.GetResultSafely());
            AddUntilStep("client receives stopped match packet", () => receivedMatches.Any(match => !match.InProgress));
            AddUntilStep("Stable IPC enters ranking", () => stableIPC.CurrentMatch.Value?.InProgress == false && stableIPC.State.Value == TourneyState.Ranking);

            AddStep("reset simulator room", () => resetSimulatorTask = postSimulatorActionAsync(simulatorBaseUrl, simulatorHost, "reset"));
            AddUntilStep("simulator reset completes", () => resetSimulatorTask?.IsCompleted == true);
            AddAssert("simulator reset succeeds", () => resetSimulatorTask?.IsCompletedSuccessfully == true && resetSimulatorTask.GetResultSafely());
            AddUntilStep("Stable IPC clears reset room", () => stableIPC.CurrentMatch.Value == null && stableIPC.State.Value == TourneyState.Idle);
        }

        private static async Task<bool> postSimulatorActionAsync(string simulatorBaseUrl, string simulatorHost, string action)
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{simulatorBaseUrl.TrimEnd('/')}/tourney-sim/{action}");
            request.Headers.Host = simulatorHost;

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(20),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(26, 26, 26, 255),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 16),
                        Children = new Drawable[]
                        {
                            new TournamentSpriteText
                            {
                                Text = "Stable Bancho Minimal Spectator Test",
                                Font = OsuFont.GetFont(size: 32, weight: FontWeight.Bold),
                            },
                            new TournamentSpriteText
                            {
                                Text = "Password will be MD5-hashed before login. After login, lobby rooms will appear below.",
                                Font = OsuFont.GetFont(size: 18),
                                Colour = new Color4(200, 200, 200, 255),
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(16, 0),
                                Children = new Drawable[]
                                {
                                    usernameTextBox = new LabelledTextBox
                                    {
                                        Width = 0.3f,
                                        Label = "Username",
                                        PlaceholderText = "stable username",
                                    },
                                    passwordTextBox = new LabelledPasswordTextBox
                                    {
                                        Width = 0.3f,
                                        Label = "Password",
                                        PlaceholderText = "stable password",
                                    },
                                    new TourneyButton
                                    {
                                        Width = 0.3f,
                                        Height = 50,
                                        Text = "Login",
                                        Action = connect,
                                    },
                                }
                            },
                            statusText = new TournamentSpriteText
                            {
                                Text = "Idle.",
                                Font = OsuFont.GetFont(size: 20),
                            },
                            replayStatusText = new TournamentSpriteText
                            {
                                Text = "No replay frames yet.",
                                Font = OsuFont.GetFont(size: 18),
                                Colour = new Color4(180, 180, 180, 255),
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new OsuScrollContainer(Direction.Vertical)
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = roomList = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 12),
                                    }
                                }
                            }
                        }
                    }
                }
            };

            updateRoomList();
        }

        private void connect()
        {
            string username = usernameTextBox.Text.Trim();
            string password = passwordTextBox.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                statusText.Text = "Username and password are required.";
                return;
            }

            if (client != null)
            {
                Remove(client, true);
                client.Dispose();
                client = null;
            }

            matches.Clear();
            updateRoomList();

            statusText.Text = $"Connecting as {username}...";
            replayStatusText.Text = "No replay frames yet.";

            var newClient = new StableBanchoClient();

            newClient.OnLoginSuccess += userId => Schedule(() =>
            {
                statusText.Text = $"Logged in as {username} (user id {userId}). Joining lobby...";
                newClient.JoinLobby();
            });

            newClient.OnMatchCreated += match => Schedule(() =>
            {
                matches[match.Id] = match;
                updateRoomList();
            });

            newClient.OnMatchUpdated += match => Schedule(() =>
            {
                matches[match.Id] = match;
                updateRoomList();
            });

            newClient.OnMatchDisbanded += matchId => Schedule(() =>
            {
                matches.Remove(matchId);
                updateRoomList();
            });

            newClient.OnReplayFramesReceived += bundle => Schedule(() =>
            {
                replayStatusText.Text = $"Replay: seq {bundle.Sequence}, frames {bundle.Frames.Count}, score {bundle.ScoreFrame.TotalScore}, combo {bundle.ScoreFrame.CurrentCombo}";
            });

            LoadComponentAsync(newClient, loadedClient =>
            {
                client = loadedClient;
                Add(loadedClient);
                loadedClient.ConnectAsync(username, password.ComputeMD5Hash()).ContinueWith(t =>
                {
                    if (!t.IsCompletedSuccessfully || !t.GetResultSafely())
                        Schedule(() => statusText.Text = "Failed to connect.");
                }).FireAndForget();
            });
        }

        private void spectateFirstPlayer(MultiplayerMatch match)
        {
            if (client == null)
            {
                statusText.Text = "Connect first.";
                return;
            }

            int userId = match.SlotUserIds.FirstOrDefault();

            if (userId == 0)
            {
                statusText.Text = $"Room {match.Id} has no players to spectate.";
                return;
            }

            statusText.Text = $"Joining room {match.Id} and spectating user {userId}...";
            client.SpecialJoinMatchChannel(match.Id);
            client.StartSpectating(userId);
        }

        private void updateRoomList()
        {
            roomList.Clear();

            if (matches.Count == 0)
            {
                roomList.Add(new TournamentSpriteText
                {
                    Text = "No rooms yet. Login and wait for lobby updates.",
                    Font = OsuFont.GetFont(size: 22),
                    Colour = new Color4(180, 180, 180, 255),
                });
                return;
            }

            foreach (MultiplayerMatch match in matches.Values.OrderBy(m => m.Id))
                roomList.Add(new RoomPanel(match, () => spectateFirstPlayer(match)));
        }

        private partial class RoomPanel : CompositeDrawable
        {
            public RoomPanel(MultiplayerMatch match, System.Action spectateAction)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                int firstUserId = match.SlotUserIds.FirstOrDefault();

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(42, 42, 42, 255),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(14),
                        Spacing = new Vector2(0, 6),
                        Children = new Drawable[]
                        {
                            new TournamentSpriteText
                            {
                                Text = $"[{match.Id}] {match.Name}",
                                Font = OsuFont.GetFont(size: 24, weight: FontWeight.Bold),
                            },
                            createLine($"Beatmap: {match.BeatmapName} ({match.BeatmapId})"),
                            createLine($"Players: {match.SlotUserIds.Count(i => i > 0)} | Host: {match.HostId} | First user: {(firstUserId == 0 ? "none" : firstUserId.ToString())}"),
                            createLine($"In progress: {match.InProgress} | Mode: {match.PlayMode} | Score type: {match.ScoringType}"),
                            new TourneyButton
                            {
                                Width = 260,
                                Height = 42,
                                Text = "Join + Spectate First Player",
                                Action = spectateAction,
                            }
                        }
                    }
                };

                Masking = true;
                CornerRadius = 10;
            }

            private static TournamentSpriteText createLine(LocalisableString text) => new TournamentSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 18),
                Colour = new Color4(200, 200, 200, 255),
            };
        }

        private partial class LabelledPasswordTextBox : LabelledTextBox
        {
            protected override OsuTextBox CreateTextBox() => new OsuPasswordTextBox();
        }
    }
}
