// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Game.Tournament.Configuration;
using TSLib;
using TSLib.Commands;
using TSLib.Full;
using TSLib.Helper;
using TeamSpeakClient = TSLib.Full.Book.Client;
using TSLib.Scheduler;

namespace osu.Game.Tournament.Audio
{
    internal partial class TeamSpeakSession : Component
    {
        private DedicatedTaskScheduler? networkScheduler;
        private DedicatedTaskScheduler network => networkScheduler ??= new DedicatedTaskScheduler(Id.Null);
        private TsFullClient? client;
        private readonly HashSet<ChannelId> subscriptionAttempts = new HashSet<ChannelId>();
        private bool subscribing;
        private IdentityData? identity;
        private TournamentConfigManager config = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private AudioMixer? commentaryMixer;
        private TeamSpeakAudioReceiver? audioReceiver;
        private IRenderer renderer = null!;
        private bool polling;
        private int revision;
        private volatile bool disposed;

        public readonly BindableDouble MixerVolume = new BindableDouble(1) { MinValue = 0, MaxValue = 1, Precision = 0.01 };
        public readonly Bindable<ConnectionStatus> Status = new Bindable<ConnectionStatus>();
        public readonly Bindable<bool> ChannelPasswordRejected = new Bindable<bool>();
        public readonly Bindable<string?> LastError = new Bindable<string?>();
        public readonly Bindable<string?> DirectoryWarning = new Bindable<string?>();

        private bool isBusy => Status.Value is ConnectionStatus.Connecting or ConnectionStatus.JoiningChannel or ConnectionStatus.Disconnecting;
        public readonly Bindable<IReadOnlyList<ChannelMember>> VisibleMembers = new Bindable<IReadOnlyList<ChannelMember>>(Array.Empty<ChannelMember>());
        public readonly Bindable<string> OwnNickname = new Bindable<string>(string.Empty);
        public readonly Bindable<ChannelId> CurrentChannel = new Bindable<ChannelId>();
        public readonly Bindable<IReadOnlyList<Channel>> Channels = new Bindable<IReadOnlyList<Channel>>(Array.Empty<Channel>());
        public readonly Bindable<IReadOnlyList<TeamSpeakSpeaker>> Speakers = new Bindable<IReadOnlyList<TeamSpeakSpeaker>>(Array.Empty<TeamSpeakSpeaker>());

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, TournamentConfigManager config)
        {
            this.renderer = renderer;
            this.config = config;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Scheduler.AddDelayed(refresh, 1000, repeat: true);
        }

        public void Connect(string address, string password, string nickname)
        {
            if (disposed || Status.Value != ConnectionStatus.Disconnected || string.IsNullOrWhiteSpace(address))
                return;

            commentaryMixer ??= audio.CreateAudioMixer("TeamSpeak commentary");
            audioReceiver ??= new TeamSpeakAudioReceiver(audio, commentaryMixer, MixerVolume);
            var input = audioReceiver.Input;
            string storedIdentity = config.Get<string>(StorageConfig.TeamSpeakIdentity);

            run(ConnectionStatus.Connecting, async () =>
            {
                if (client != null)
                    return;

                var connection = new TsFullClient(network) { OutStream = input };
                client = connection;
                subscriptionAttempts.Clear();

                try
                {
                    try
                    {
                        TeamSpeakAudioReceiver.CheckDecoder();
                    }
                    catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                    {
                        string message = "TeamSpeak could not load libopus.";
                        if (OperatingSystem.IsMacOS())
                            message += " Run 'brew install opus', then restart.";
                        else if (OperatingSystem.IsLinux())
                            message += " Install your distribution's Opus runtime (Debian: libopus0).";
                        else if (OperatingSystem.IsWindows())
                            message += " Check that the matching libopus.dll is next to osu!.";

                        throw new InvalidOperationException(message, exception);
                    }

                    identity ??= loadOrCreateIdentity(storedIdentity);
                    var result = await connection.Connect(new ConnectionDataFull(address.Trim(), identity,
                        username: nickname.Trim(), serverPassword: password.Length == 0 ? Password.Empty : Password.FromPlain(password))).ConfigureAwait(true);

                    if (!result.Ok)
                        throw new InvalidOperationException(result.Error.ErrorFormat());

                    connection.OnDisconnected += (_, _) => Schedule(() => onDisconnected(connection));
                }
                catch
                {
                    connection.Dispose();
                    client = null;
                    throw;
                }
            });
        }

        private IdentityData loadOrCreateIdentity(string storedIdentity)
        {
            if (storedIdentity.Length > 0)
            {
                int separator = storedIdentity.IndexOf(':');

                if (separator > 0 && ulong.TryParse(storedIdentity.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out ulong offset))
                {
                    var restored = TsCrypt.LoadIdentity(storedIdentity[(separator + 1)..], offset);
                    if (restored.Ok)
                        return restored.Value;
                }

                Logger.Log("The saved TeamSpeak identity could not be loaded. A new identity was generated.", level: osu.Framework.Logging.LogLevel.Important);
            }

            var generated = TsCrypt.GenerateNewIdentity();
            string value = $"{generated.ValidKeyOffset.ToString(CultureInfo.InvariantCulture)}:{generated.PrivateKeyString}";
            Schedule(() => config.SetValue(StorageConfig.TeamSpeakIdentity, value));
            return generated;
        }

        public void Disconnect() => run(ConnectionStatus.Disconnecting, async () =>
        {
            if (client is not { } connection)
                return;

            try
            {
                await connection.Disconnect().ConfigureAwait(true);
            }
            finally
            {
                connection.Dispose();
                client = null;
            }
        });

        public void Join(ChannelId channel, string password) => run(ConnectionStatus.JoiningChannel, async () =>
        {
            if (client is not { Connected: true } connection)
                return;

            var result = await connection.ClientMove(connection.ClientId, channel, password.Length == 0 ? null : password).ConfigureAwait(true);

            if (!result.Ok)
            {
                if (result.Error.Id == TsErrorCode.channel_invalid_password)
                    Schedule(() => ChannelPasswordRejected.Value = true);

                throw new InvalidOperationException(result.Error.ErrorFormat());
            }

            Schedule(() =>
            {
                CurrentChannel.Value = channel;
                clearSpeakers();
            });
        });

        private void run(ConnectionStatus status, Func<Task> action)
        {
            if (isBusy || disposed)
                return;

            int operationRevision = ++revision;
            ChannelPasswordRejected.Value = false;
            LastError.Value = null;
            Status.Value = status;
            _ = complete();

            async Task complete()
            {
                string? error = null;
                bool connected = false;

                try
                {
                    await network.InvokeAsync(action).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }

                if (disposed)
                    return;

                try
                {
                    connected = await network.Invoke(() => client?.Connected == true).ConfigureAwait(false);
                }
                catch (InvalidOperationException) when (disposed)
                {
                    return;
                }

                Schedule(() =>
                {
                    bool stillConnected = connected && operationRevision == revision;
                    LastError.Value = error;
                    Status.Value = stillConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;

                    if (stillConnected)
                        refresh();
                    else
                        clearConnection();
                });
            }
        }

        private void onDisconnected(TsFullClient connection)
        {
            if (disposed)
                return;

            revision++;
            clearConnection();
            if (!isBusy)
                Status.Value = ConnectionStatus.Disconnected;
            _ = network.Invoke(() =>
            {
                if (ReferenceEquals(client, connection))
                {
                    connection.Dispose();
                    client = null;
                }
            });
        }

        private void clearConnection()
        {
            DirectoryWarning.Value = null;
            VisibleMembers.Value = Array.Empty<ChannelMember>();
            OwnNickname.Value = string.Empty;
            CurrentChannel.Value = ChannelId.Null;
            Channels.Value = Array.Empty<Channel>();
            clearSpeakers();
        }

        private void clearSpeakers() => applySpeakers(Array.Empty<Member>());

        private static Channel[] getChannels(TsFullClient connection)
        {
            return connection.Book.Channels.Values
                             .Select(channel => new Channel(channel.Id, channel.Parent, channel.Order, channel.Name, channel.HasPassword == true))
                             .ToArray();
        }

        private void refresh()
        {
            if (polling || Status.Value != ConnectionStatus.Connected)
                return;

            polling = true;
            int snapshotRevision = revision;
            _ = poll();

            bool canApplySnapshot() => snapshotRevision == revision && Status.Value == ConnectionStatus.Connected;

            async Task poll()
            {
                try
                {
                    await network.Invoke(() =>
                    {
                        if (client is not { Connected: true } connection)
                            return;

                        var channels = getChannels(connection);
                        if (!subscribing)
                        {
                            var pending = connection.Book.Channels.Values.Where(channel => !channel.Subscribed && subscriptionAttempts.Add(channel.Id))
                                                    .Select(channel => channel.Id).ToArray();
                            if (pending.Length > 0)
                                _ = subscribeChannels(connection, pending);
                        }
                        var clients = connection.Book.Clients;
                        clients.TryGetValue(connection.ClientId, out TeamSpeakClient? self);
                        ChannelId ownChannel = self?.Channel ?? ChannelId.Null;
                        string ownNickname = self?.Name ?? string.Empty;
                        var visibleMembers = clients.Values.Where(member => member.Id != connection.ClientId && member.ClientType == ClientType.Full)
                                                    .Select(member => new ChannelMember(member.Id, member.Channel, member.Name)).ToArray();
                        var members = clients.Values.Where(member => ownChannel != ChannelId.Null && member.Channel == ownChannel
                                                                     && member.Id != connection.ClientId && member.ClientType == ClientType.Full)
                                             .Select(member => new Member(member.Id, member.Name, member.Uid)).ToArray();

                        Schedule(() =>
                        {
                            if (!canApplySnapshot())
                                return;

                            DirectoryWarning.Value = channels.Length == 0 ? "The server has not provided any visible channels." : null;

                            if (!Channels.Value.SequenceEqual(channels))
                                Channels.Value = channels;

                            if (!VisibleMembers.Value.SequenceEqual(visibleMembers))
                                VisibleMembers.Value = visibleMembers;

                            if (ownChannel != ChannelId.Null)
                            {
                                OwnNickname.Value = ownNickname;
                                CurrentChannel.Value = ownChannel;
                                applySpeakers(members);
                            }
                        });
                    }).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to read the TeamSpeak channel and member list.");
                    Schedule(() =>
                    {
                        if (canApplySnapshot())
                            DirectoryWarning.Value = "Could not update the TeamSpeak channel and member list.";
                    });
                }
                finally
                {
                    Schedule(() => polling = false);
                }
            }
        }

        private async Task subscribeChannels(TsFullClient connection, ChannelId[] channels)
        {
            subscribing = true;

            try
            {
                foreach (var channel in channels)
                {
                    if (!ReferenceEquals(client, connection) || !connection.Connected)
                        return;

                    if (connection.Book.Channels.TryGetValue(channel, out var entry) && !entry.Subscribed)
                        await connection.SendVoid(new TsCommand("channelsubscribe") { { "cid", channel } }).ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to subscribe to TeamSpeak channels.");
            }
            finally
            {
                subscribing = false;
            }
        }

        private void applySpeakers(IReadOnlyList<Member> members)
        {
            var previous = Speakers.Value;
            var current = new List<TeamSpeakSpeaker>(members.Count);

            foreach (var member in members)
            {
                var speaker = previous.FirstOrDefault(existing => existing.Id == member.Id && existing.Uid == member.Uid && existing.Name == member.Name);

                if (speaker == null)
                {
                    speaker = new TeamSpeakSpeaker(member.Id, member.Name, member.Uid);
                    _ = loadAvatar(speaker);
                }

                current.Add(speaker);
            }

            if (previous.SequenceEqual(current))
                return;

            audioReceiver?.SetSpeakers(current);
            Speakers.Value = current;

            foreach (var removed in previous.Except(current))
                removed.Avatar?.Dispose();
        }

        private readonly record struct Member(ClientId Id, string Name, Uid? Uid);

        private async Task loadAvatar(TeamSpeakSpeaker speaker)
        {
            if (speaker.Uid is not { } uid)
                return;

            try
            {
                byte[] uidBytes = Convert.FromBase64String(uid.ToString());
                string file = string.Concat(uidBytes.Select(value => $"{(char)('a' + (value >> 4))}{(char)('a' + (value & 15))}"));

                using var stream = new AvatarStream();
                bool received = await network.InvokeAsync(async () =>
                {
                    if (client is not { Connected: true } connection)
                        return false;

                    var result = await connection.DownloadFile(stream, ChannelId.Null, "/avatar_" + file, closeStream: false).ConfigureAwait(true);
                    return result.Ok && result.Value.Status == TransferStatus.Done;
                }).ConfigureAwait(false);

                if (!received || disposed)
                    return;

                stream.Position = 0;
                var info = await SixLabors.ImageSharp.Image.IdentifyAsync(stream).ConfigureAwait(false);
                if (info.Width > 512 || info.Height > 512)
                    return;

                stream.Position = 0;
                var upload = new TextureUpload(stream);

                Schedule(() =>
                {
                    if (!Speakers.Value.Contains(speaker))
                    {
                        upload.Dispose();
                        return;
                    }

                    speaker.Avatar = renderer.CreateTexture(upload.Width, upload.Height);
                    speaker.Avatar.SetData(upload);
                });
            }
            catch (Exception exception)
            {
                Logger.Log($"Could not load TeamSpeak avatar: {exception.GetType().Name}", level: osu.Framework.Logging.LogLevel.Debug);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                disposed = true;

                if (networkScheduler is { } scheduler)
                {
                    var shutdown = scheduler.InvokeAsync(async () =>
                    {
                        var connection = client;
                        client = null;

                        try
                        {
                            if (connection?.Connected == true)
                                await connection.Disconnect().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
                        }
                        catch (TimeoutException)
                        {
                            Logger.Log("TeamSpeak did not acknowledge disconnect before shutdown.");
                        }
                        finally
                        {
                            try
                            {
                                connection?.Dispose();
                            }
                            finally
                            {
                                scheduler.Dispose();
                            }
                        }
                    });

                    try
                    {
                        if (!shutdown.Wait(TimeSpan.FromSeconds(2)))
                            Logger.Log("TeamSpeak shutdown is taking longer than expected.");
                    }
                    catch (AggregateException exception)
                    {
                        Logger.Error(exception.GetBaseException(), "Failed to shut down TeamSpeak.");
                    }
                }

                foreach (var speaker in Speakers.Value)
                    speaker.Avatar?.Dispose();
                audioReceiver?.Dispose();
                commentaryMixer?.Dispose();
            }

            base.Dispose(isDisposing);
        }

        public enum ConnectionStatus
        {
            Disconnected,
            Connecting,
            Connected,
            JoiningChannel,
            Disconnecting,
        }

        public readonly record struct ChannelMember(ClientId Id, ChannelId Channel, string Name);

        public sealed record Channel(ChannelId Id, ChannelId Parent, ChannelId Previous, string Name, bool HasPassword);

        private sealed class AvatarStream : MemoryStream
        {
            private const int maximum_size = 1024 * 1024;

            public override void SetLength(long value)
            {
                if (value > maximum_size)
                    throw new IOException("TeamSpeak avatar exceeds the download limit.");

                base.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (Position + buffer.Length > maximum_size)
                    throw new IOException("TeamSpeak avatar exceeds the download limit.");

                // Call the byte-array implementation to avoid MemoryStream's subclass fallback.
                base.Write(buffer.ToArray(), 0, buffer.Length);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Write(buffer.Span);
                return ValueTask.CompletedTask;
            }
        }
    }
}
