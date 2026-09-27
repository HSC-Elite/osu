// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.Screens;
using osu.Game.Tournament.StableClient.IPC;
using osu.Game.Tournament.StableClient.Protocol;
using osuTK;

namespace osu.Game.Tournament.StableClient.Screens
{
    public partial class StableSetupScreen : TournamentScreen
    {
        [Resolved]
        private StableBanchoClient banchoClient { get; set; } = null!;

        [Resolved]
        private StableMatchIPCInfo ipcInfo { get; set; } = null!;

        [Resolved]
        private TournamentConfigManager config { get; set; } = null!;

        private FillFlowContainer<MatchButton> matchesFlow = null!;
        private OsuTextBox usernameBox = null!;
        private OsuPasswordTextBox passwordBox = null!;
        private RoundedButton connectButton = null!;
        private Bindable<string> savedUsername = null!;
        private Bindable<string> savedPasswordHash = null!;

        private readonly Dictionary<int, MultiplayerMatch> availableMatches = new Dictionary<int, MultiplayerMatch>();

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            savedUsername = config.GetBindable<string>(TournamentConfig.StableUsername);
            savedPasswordHash = config.GetBindable<string>(TournamentConfig.StablePasswordHash);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colours.Gray1
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(10),
                    Padding = new MarginPadding(20),
                    AutoSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = "Stable Bancho Connection Setup", Font = OsuFont.Torus.With(size: 24, weight: FontWeight.Bold) },
                        usernameBox = new OsuTextBox { PlaceholderText = "Username", Width = 300, Text = savedUsername.Value },
                        passwordBox = new OsuPasswordTextBox { PlaceholderText = "Password (blank reuses saved)", Width = 300 },
                        connectButton = new RoundedButton
                        {
                            Text = "Connect & Fetch Matches",
                            Width = 300,
                            Action = onConnectClicked
                        }
                    }
                },
                new OsuScrollContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Y,
                    Width = 400,
                    Padding = new MarginPadding(20),
                    Child = matchesFlow = new FillFlowContainer<MatchButton>
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(5)
                    }
                }
            };

            banchoClient.OnMatchCreated += addOrUpdateMatch;
            banchoClient.OnMatchUpdated += addOrUpdateMatch;
            banchoClient.OnMatchDisbanded += removeMatch;
        }

        private void onConnectClicked()
        {
            string username = usernameBox.Text.Trim();
            string passwordHash = ResolvePasswordHash(passwordBox.Text, savedPasswordHash.Value);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordHash))
            {
                connectButton.Text = "Username and password are required";
                return;
            }

            savedUsername.Value = username;
            savedPasswordHash.Value = passwordHash;
            ipcInfo.SetCredentials(username, passwordHash);

            connectButton.Enabled.Value = false;
            connectButton.Text = "Connecting...";

            banchoClient.ConnectAsync(username, passwordHash).ContinueWith(t => Schedule(() =>
            {
                if (t.IsFaulted || !t.GetResultSafely())
                {
                    connectButton.Enabled.Value = true;
                    connectButton.Text = "Failed to connect!";
                    return;
                }

                connectButton.Enabled.Value = true;
                connectButton.Text = "Connected! Joining Lobby...";
                banchoClient.JoinLobby();
            }));
        }

        private static string getHashedPassword(string pass)
        {
            if (!string.IsNullOrEmpty(pass) && pass.Length != 32)
            {
                byte[] hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(pass));
                pass = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            return pass;
        }

        internal static string ResolvePasswordHash(string enteredPassword, string savedPasswordHash)
            => string.IsNullOrEmpty(enteredPassword) ? savedPasswordHash : getHashedPassword(enteredPassword);

        private void addOrUpdateMatch(MultiplayerMatch match)
        {
            Schedule(() =>
            {
                availableMatches[match.Id] = match;
                refreshMatchesList();
            });
        }

        private void removeMatch(int matchId)
        {
            Schedule(() =>
            {
                availableMatches.Remove(matchId);
                refreshMatchesList();
            });
        }

        private void refreshMatchesList()
        {
            matchesFlow.Clear();

            foreach (var match in availableMatches.Values)
            {
                matchesFlow.Add(new MatchButton(match, () =>
                {
                    foreach (var userId in match.SlotUserIds)
                    {
                        if (userId > 0)
                        {
                            // banchoClient.StartSpectating(userId); // We don't do this on the manager client anymore.
                        }
                    }

                    ipcInfo.SetCredentials(savedUsername.Value, savedPasswordHash.Value);
                    ipcInfo.CurrentMatch.Value = match;
                    banchoClient.SpecialJoinMatchChannel(match.Id);

                    refreshMatchesList();
                })
                {
                    Selected = ipcInfo.CurrentMatch.Value?.Id == match.Id
                });
            }
        }

        private partial class MatchButton : RoundedButton
        {
            public bool Selected
            {
                set => Background.Colour = value ? Colour4.DarkGreen : Colour4.DarkGray;
            }

            public MatchButton(MultiplayerMatch match, Action action)
            {
                Text = $"[{match.Id}] {match.Name}";
                Action = action;
                RelativeSizeAxes = Axes.X;
                Background.Colour = Colour4.DarkGray;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (banchoClient != null)
            {
                banchoClient.OnMatchCreated -= addOrUpdateMatch;
                banchoClient.OnMatchUpdated -= addOrUpdateMatch;
                banchoClient.OnMatchDisbanded -= removeMatch;
            }
            base.Dispose(isDisposing);
        }
    }
}
