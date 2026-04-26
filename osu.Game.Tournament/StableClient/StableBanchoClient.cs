// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.StableClient
{
    /// <summary>
    /// 模拟 osu!stable 的 Bancho 客户端实现，支持锦标赛多重登录。
    /// </summary>
    public partial class StableBanchoClient : Component, IDisposable
    {
        private readonly string username;
        private readonly string passwordHash;
        private string clientHashes;
        private string? version;

        private string? token;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> initializationSource = new TaskCompletionSource<bool>();

        public event Action<int>? OnLoginSuccess;
        public event Action<bReplayFrameBundle>? OnReplayFramesReceived;
        public event Action<MultiplayerMatch>? OnMatchCreated;
        public event Action<MultiplayerMatch>? OnMatchUpdated;
        public event Action<int>? OnMatchDisbanded;
        public event Action<int, string, byte>? OnUserStatusChanged;

        public StableBanchoClient(string username, string passwordHash, string clientHashes = "", string version = "")
        {
            this.username = username;
            this.passwordHash = passwordHash;
            this.clientHashes = clientHashes;
            this.version = version;
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrEmpty(version))
                    {
                        var info = await new StableVersionManager(api).GetLatestVersionAsync().ConfigureAwait(false);

                        if (info != null)
                        {
                            version = info.Version;
                            if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(clientHashes))
                                clientHashes = HardwareIdentification.GenerateClientHashes(info.FileHash);
                        }
                    }

                    if (string.IsNullOrEmpty(clientHashes))
                    {
                        if (OperatingSystem.IsWindows())
                            clientHashes = HardwareIdentification.GenerateClientHashes();
                        else if (string.IsNullOrEmpty(clientHashes))
                            throw new InvalidOperationException("ClientHashes must be provided on non-Windows platforms.");
                    }

                    initializationSource.TrySetResult(true);
                }
                catch (Exception e)
                {
                    initializationSource.TrySetException(e);
                }
            });
        }

        public async Task ConnectAsync()
        {
            try
            {
                // 等待 load 中的异步初始化完成
                await initializationSource.Task.ConfigureAwait(false);

                await loginAsync().ConfigureAwait(false);
                _ = Task.Run(pollLoop, cts.Token);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"StableClient [{username}] connection/initialization failed.");
            }
        }

        public void JoinLobby() => sendEmptyPacket(PacketType.Osu_LobbyJoin).FireAndForget();
        public void PartLobby() => sendEmptyPacket(PacketType.Osu_LobbyPart).FireAndForget();
        public void SpecialJoinMatchChannel(int matchId) => sendPacket(PacketType.Osu_SpecialJoinMatchChannel, matchId).FireAndForget();

        private async Task loginAsync()
        {
            var loginData = new StringBuilder();
            loginData.AppendLine(username);
            loginData.AppendLine(passwordHash);
            loginData.AppendLine($"{version}|8|1|{clientHashes}|0");

            var request = new OsuWebRequest("https://c.ppy.sh");
            request.Method = HttpMethod.Post;
            request.AddRaw(Encoding.UTF8.GetBytes(loginData.ToString()));
            request.AddHeader("osu-version", version!);

            await request.PerformAsync(cts.Token).ConfigureAwait(false);

            if (request.ResponseHeaders!.TryGetValues("cho-token", out var tokens))
                token = tokens.FirstOrDefault();

            if (!string.IsNullOrEmpty(token))
                Logger.Log($"StableClient [{username}] logged in, token: {token}");

            using (var stream = request.ResponseStream)
            using (var reader = new BinaryReader(stream))
            {
                parsePackets(reader);
            }
        }

        private async Task pollLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    var request = new OsuWebRequest("https://c.ppy.sh");
                    request.Method = HttpMethod.Post;
                    request.AddHeader("osu-token", token);

                    await request.PerformAsync(cts.Token).ConfigureAwait(false);

                    using (var stream = request.ResponseStream)
                    using (var reader = new BinaryReader(stream))
                    {
                        parsePackets(reader);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Bancho poll error");
                }

                await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            }
        }

        private void parsePackets(BinaryReader reader)
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                short packetId = reader.ReadInt16();
                reader.ReadByte();
                int length = reader.ReadInt32();
                byte[] payload = reader.ReadBytes(length);

                handlePacket((PacketType)packetId, payload);
            }
        }

        private void handlePacket(PacketType type, byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                switch (type)
                {
                    case PacketType.Bancho_LoginReply:
                        int userId = reader.ReadInt32();
                        if (userId > 0) OnLoginSuccess?.Invoke(userId);
                        break;

                    case PacketType.Bancho_SpectateFrames:
                        var bundle = new bReplayFrameBundle(reader);
                        OnReplayFramesReceived?.Invoke(bundle);
                        break;

                    case PacketType.Bancho_MatchNew:
                        OnMatchCreated?.Invoke(new MultiplayerMatch(reader));
                        break;

                    case PacketType.Bancho_MatchUpdate:
                        OnMatchUpdated?.Invoke(new MultiplayerMatch(reader));
                        break;

                    case PacketType.Bancho_MatchDisband:
                        OnMatchDisbanded?.Invoke(reader.ReadInt32());
                        break;

                    case PacketType.Bancho_HandleOsuUpdate:
                        handleUserStatus(reader);
                        break;
                }
            }
        }

        private void handleUserStatus(BinaryReader reader)
        {
            int userId = reader.ReadInt32();
            reader.ReadByte(); // Action
            reader.ReadBString(); // StatusText
            string beatmapChecksum = reader.ReadBString();
            reader.ReadInt32(); // Mods
            byte playMode = reader.ReadByte();

            OnUserStatusChanged?.Invoke(userId, beatmapChecksum, playMode);
        }

        public void StartSpectating(int userId)
        {
            sendPacket(PacketType.Osu_StartSpectating, userId).FireAndForget();
        }

        private async Task sendEmptyPacket(PacketType type)
        {
            if (token == null) return;

            var request = new OsuWebRequest("https://c.ppy.sh");
            request.Method = HttpMethod.Post;
            request.AddHeader("osu-token", token);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((short)type);
                writer.Write((byte)0);
                writer.Write(0);

                request.AddRaw(ms.ToArray());
            }

            await request.PerformAsync(cts.Token).ConfigureAwait(false);
        }

        private async Task sendPacket(PacketType type, int value)
        {
            if (token == null) return;

            var request = new OsuWebRequest("https://c.ppy.sh");
            request.Method = HttpMethod.Post;
            request.AddHeader("osu-token", token);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((short)type);
                writer.Write((byte)0);
                writer.Write(4);
                writer.Write(value);

                request.AddRaw(ms.ToArray());
            }

            await request.PerformAsync(cts.Token).ConfigureAwait(false);
        }

        protected override void Dispose(bool isDisposing)
        {
            cts.Cancel();
            cts.Dispose();
            base.Dispose(isDisposing);
        }
    }

    public enum PacketType : short
    {
        Bancho_LoginReply = 5,
        Bancho_SpectateFrames = 15,
        Osu_StartSpectating = 16,
        Bancho_MatchUpdate = 26,
        Bancho_MatchNew = 27,
        Bancho_MatchDisband = 28,
        Osu_LobbyPart = 29,
        Osu_LobbyJoin = 30,
        Bancho_HandleOsuUpdate = 95,
        Osu_SpecialJoinMatchChannel = 108,
    }
}
