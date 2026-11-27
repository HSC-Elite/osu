// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using TSLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Extensions;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tournament.Audio;
using osu.Game.Tournament.Components;
using osuTK;

namespace osu.Game.Tournament.Screens.TeamSpeak
{
    internal partial class TeamSpeakConsoleScreen : TournamentScreen
    {
        private readonly Bindable<TeamSpeakSession.ConnectionStatus> statusBinding = new Bindable<TeamSpeakSession.ConnectionStatus>();
        private IBindable<APIUser> localUser = null!;
        private readonly Bindable<string?> directoryWarning = new Bindable<string?>();
        private readonly Bindable<bool> channelPasswordRejected = new Bindable<bool>();
        private readonly Bindable<string?> lastError = new Bindable<string?>();
        private readonly Bindable<IReadOnlyList<TeamSpeakSession.ChannelMember>> visibleMembers = new Bindable<IReadOnlyList<TeamSpeakSession.ChannelMember>>();
        private readonly Bindable<string> ownNickname = new Bindable<string>();
        private readonly Bindable<ChannelId> currentChannel = new Bindable<ChannelId>();
        private readonly Bindable<IReadOnlyList<TeamSpeakSession.Channel>> channelList = new Bindable<IReadOnlyList<TeamSpeakSession.Channel>>();
        private readonly Bindable<IReadOnlyList<TeamSpeakSpeaker>> speakers = new Bindable<IReadOnlyList<TeamSpeakSpeaker>>();

        [BackgroundDependencyLoader]
        private void load(TeamSpeakSession session, IAPIProvider api)
        {
            var address = new LabelledTextBox { Label = "Server address", Text = "ts.darkhouse.top" };
            var nickname = new LabelledTextBox { Label = "Nickname" };
            var serverPassword = new PasswordField { Label = "Server password" };
            var channelPassword = new PasswordField { Label = "Channel password" };
            var rows = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
            };
            var channels = new OsuScrollContainer { RelativeSizeAxes = Axes.X, Height = 250, Child = rows };
            var highlights = new Dictionary<ChannelId, Box>();
            var collapsed = new HashSet<ChannelId>();
            ChannelId? selectedChannel = null;
            var status = new OsuSpriteText();
            var warning = new OsuSpriteText();
            var connect = new TourneyButton
            {
                Text = "Connect",
                Action = () =>
                {
                    if (statusBinding.Value == TeamSpeakSession.ConnectionStatus.Connected)
                        session.Disconnect();
                    else
                    {
                        session.Connect(address.Text, serverPassword.Text, string.IsNullOrWhiteSpace(nickname.Text) ? defaultNickname() : nickname.Text);
                        serverPassword.Text = string.Empty;
                    }
                },
            };
            var join = new TourneyButton
            {
                Text = "Join channel",
                Action = joinSelectedChannel,
            };

            var volumes = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(6)
            };

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = ColourProvider.Background5 },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(10),
                        Spacing = new Vector2(10),
                        Children = new Drawable[]
                        {
                            new VolumeSlider
                            {
                                Label = "TeamSpeak mixer volume",
                                Current = session.MixerVolume,
                            },
                            address, nickname, serverPassword, connect, status, warning, new OsuSpriteText { Text = "Channels" }, channels, channelPassword, join, volumes,
                        }
                    }
                }
            };

            localUser = api.LocalUser.GetBoundCopy();
            localUser.BindValueChanged(_ => nickname.PlaceholderText = defaultNickname(), true);
            statusBinding.BindTo(session.Status);
            lastError.BindTo(session.LastError);
            channelPasswordRejected.BindTo(session.ChannelPasswordRejected);
            channelPasswordRejected.BindValueChanged(change =>
            {
                if (change.NewValue)
                    channelPassword.ShowError();
            });
            directoryWarning.BindTo(session.DirectoryWarning);
            channelList.BindTo(session.Channels);
            currentChannel.BindTo(session.CurrentChannel);
            ownNickname.BindTo(session.OwnNickname);
            visibleMembers.BindTo(session.VisibleMembers);
            speakers.BindTo(session.Speakers);

            statusBinding.BindValueChanged(_ =>
            {
                updateStatusText();
                updateButtons();
            }, true);
            lastError.BindValueChanged(_ => updateStatusText());
            directoryWarning.BindValueChanged(change =>
            {
                warning.Text = change.NewValue ?? string.Empty;
                warning.Alpha = change.NewValue == null ? 0 : 1;
            }, true);
            address.Current.BindValueChanged(_ => updateButtons());
            channelList.BindValueChanged(_ =>
            {
                if (!channelList.Value.Any(channel => channel.Id == selectedChannel))
                    selectedChannel = null;

                if (channelList.Value.Count == 0)
                    collapsed.Clear();

                rebuildChannels();
            }, true);
            currentChannel.BindValueChanged(_ =>
            {
                selectedChannel = currentChannel.Value == ChannelId.Null ? null : currentChannel.Value;
                rebuildChannels();
            }, true);
            ownNickname.BindValueChanged(_ => rebuildChannels());
            visibleMembers.BindValueChanged(_ => rebuildChannels());
            speakers.BindValueChanged(change =>
            {
                volumes.Clear();
                foreach (var speaker in change.NewValue)
                {
                    volumes.Add(new VolumeSlider
                    {
                        Label = speaker.Name,
                        Current = speaker.Volume,
                    });
                }
            }, true);

            string defaultNickname()
            {
                string? username = localUser.Value?.Username;
                return string.IsNullOrWhiteSpace(username) ? "osu! Tournament" : username;
            }

            void updateStatusText()
            {
                status.Text = lastError.Value ?? statusBinding.Value switch
                {
                    TeamSpeakSession.ConnectionStatus.Connecting => "Connecting…",
                    TeamSpeakSession.ConnectionStatus.Connected => "Connected",
                    TeamSpeakSession.ConnectionStatus.JoiningChannel => "Joining channel…",
                    TeamSpeakSession.ConnectionStatus.Disconnecting => "Disconnecting…",
                    _ => "Disconnected",
                };
            }

            void updateButtons()
            {
                bool connected = statusBinding.Value == TeamSpeakSession.ConnectionStatus.Connected;
                bool disconnected = statusBinding.Value == TeamSpeakSession.ConnectionStatus.Disconnected;
                connect.Text = statusBinding.Value is TeamSpeakSession.ConnectionStatus.Connected or TeamSpeakSession.ConnectionStatus.JoiningChannel
                    or TeamSpeakSession.ConnectionStatus.Disconnecting ? "Disconnect" : "Connect";
                connect.Enabled.Value = connected || (disconnected && !string.IsNullOrWhiteSpace(address.Text));
                join.Enabled.Value = connected && selectedChannel != null && selectedChannel != currentChannel.Value;
            }

            void joinSelectedChannel()
            {
                if (statusBinding.Value != TeamSpeakSession.ConnectionStatus.Connected || selectedChannel is not { } channel || channel == currentChannel.Value)
                    return;

                session.Join(channel, channelPassword.Text);
                channelPassword.Text = string.Empty;
            }

            void selectChannel(ChannelId channel)
            {
                if (selectedChannel is { } previous && highlights.TryGetValue(previous, out var previousHighlight))
                    previousHighlight.Alpha = 0;

                selectedChannel = channel;
                highlights[channel].Alpha = 1;
                updateButtons();
            }

            void rebuildChannels()
            {
                rows.Clear();
                highlights.Clear();
                var membersByChannel = visibleMembers.Value.ToLookup(member => member.Channel);
                var ids = channelList.Value.Select(channel => channel.Id).ToHashSet();
                var children = channelList.Value.ToLookup(channel => ids.Contains(channel.Parent) ? channel.Parent : ChannelId.Null);
                var visited = new HashSet<ChannelId>();
                addChildren(ChannelId.Null, 0);
                updateButtons();

                void addChildren(ChannelId parent, int depth)
                {
                    var siblings = children[parent].ToArray();
                    var siblingIds = siblings.Select(channel => channel.Id).ToHashSet();
                    var successors = siblings.ToLookup(channel => siblingIds.Contains(channel.Previous) ? channel.Previous : ChannelId.Null);
                    addFollowing(ChannelId.Null);

                    // Keep channels visible even if the server provided an incomplete or cyclic sibling order.
                    foreach (var sibling in siblings)
                        addChannel(sibling);

                    void addFollowing(ChannelId previous)
                    {
                        foreach (var channel in successors[previous])
                            addChannel(channel);
                    }

                    void addChannel(TeamSpeakSession.Channel channel)
                    {
                        if (!visited.Add(channel.Id))
                            return;

                        float indent = 10 + depth * 18;
                        var spacer = Regex.Match(channel.Name, @"^\[([clr]?)spacer[^\]]*\](.*)$", RegexOptions.IgnoreCase);
                        bool isCurrentChannel = channel.Id == currentChannel.Value;
                        bool expandable = children.Contains(channel.Id) || isCurrentChannel || membersByChannel.Contains(channel.Id);
                        bool expanded = !collapsed.Contains(channel.Id);
                        Anchor alignment = spacer.Groups[1].Value.ToLowerInvariant() switch
                        {
                            "c" => Anchor.Centre,
                            "r" => Anchor.CentreRight,
                            _ => Anchor.CentreLeft,
                        };
                        string title = spacer.Success ? spacer.Groups[2].Value : (isCurrentChannel ? "●  " : "○  ") + channel.Name;
                        if (channel.HasPassword)
                            title += "  (locked)";

                        var row = new Container { RelativeSizeAxes = Axes.X, Height = 32, Padding = new MarginPadding { Left = indent, Right = 10 } };
                        var highlight = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourProvider.Background2,
                            Alpha = channel.Id == selectedChannel ? 1 : 0,
                        };
                        highlights.Add(channel.Id, highlight);

                        row.Add(new ChannelRow
                        {
                            RelativeSizeAxes = Axes.Both,
                            Action = () => selectChannel(channel.Id),
                            DoubleClickAction = () =>
                            {
                                selectChannel(channel.Id);
                                joinSelectedChannel();
                            },
                            Children = new Drawable[]
                            {
                                highlight,
                                new OsuSpriteText
                                {
                                    Text = title,
                                    X = alignment == Anchor.CentreLeft ? 30 : 0,
                                    Anchor = alignment,
                                    Origin = alignment,
                                }
                            }
                        });

                        if (expandable)
                        {
                            row.Add(new ClickableContainer
                            {
                                Size = new Vector2(28, 32),
                                Action = () =>
                                {
                                    if (!collapsed.Remove(channel.Id))
                                        collapsed.Add(channel.Id);
                                    rebuildChannels();
                                },
                                Child = new OsuSpriteText
                                {
                                    Text = expanded ? "⌄" : "›",
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            });
                        }
                        rows.Add(row);

                        if (expanded)
                        {
                            if (isCurrentChannel && !string.IsNullOrEmpty(ownNickname.Value))
                                addMember(ownNickname.Value, indent);

                            foreach (var member in membersByChannel[channel.Id])
                                addMember(member.Name, indent);

                            addChildren(channel.Id, depth + 1);
                        }

                        addFollowing(channel.Id);
                    }
                }

                void addMember(string name, float indent)
                {
                    rows.Add(new OsuSpriteText
                    {
                        Text = "•  " + name,
                        Margin = new MarginPadding { Left = indent + 48, Vertical = 3 },
                    });
                }
            }
        }

        private partial class ChannelRow : ClickableContainer
        {
            public Action DoubleClickAction { get; init; } = null!;

            protected override bool OnDoubleClick(DoubleClickEvent e)
            {
                DoubleClickAction();
                return true;
            }
        }

        private partial class VolumeSlider : LabelledSliderBar<double>
        {
            protected override SettingsSlider<double> CreateComponent()
            {
                var slider = base.CreateComponent();
                slider.DisplayAsPercentage = true;
                return slider;
            }
        }

        private partial class PasswordField : LabelledTextBox
        {
            public void ShowError() => Component.Shake();

            protected override OsuTextBox CreateTextBox() => new OsuPasswordTextBox();
        }
    }
}
