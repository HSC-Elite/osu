// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Screens.OnlinePlay.Multiplayer;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Screens.RoomSelect
{
    public partial class RoomSelectMultiplayerLoungeSubScreen : MultiplayerLoungeSubScreen
    {
        [Resolved]
        private LazerRoomMatchInfo lazerInfo { get; set; } = null!;

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OngoingOperationTracker operationTracker { get; set; } = null!;

        public readonly BindableBool AutoReconnect = new BindableBool(true);

        private long? reconnectRoomId;
        private string? reconnectPassword;
        private int reconnectUserId;
        private bool reconnectPending;
        private bool rejoining;
        private GetRoomRequest? refreshRequest;
        private IDisposable? refreshOperation;
        private Room? refreshedRoom;
        private bool disposed;

        public RoomSelectMultiplayerLoungeSubScreen()
        {
            // Continue processing connection changes while the room screen is on top.
            AlwaysPresent = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            client.IsConnected.BindValueChanged(onConnectionChanged);
            client.UserKicked += onUserKicked;
            lazerInfo.CurrentRoom.BindValueChanged(onCurrentRoomChanged);
            lazerInfo.RoomLeft += clearReconnectTarget;
            AutoReconnect.BindValueChanged(enabled =>
            {
                if (!enabled.NewValue)
                    cancelReconnect();
            });
        }

        private void onConnectionChanged(ValueChangedEvent<bool> connected)
        {
            if (connected.NewValue)
                return;

            Schedule(() =>
            {
                if (!disposed && AutoReconnect.Value && reconnectRoomId != null)
                {
                    cancelRefresh();
                    reconnectPending = true;
                }
            });
        }

        private void onCurrentRoomChanged(ValueChangedEvent<Room?> room)
        {
            // An online room exit is intentional unless a connection loss is being recovered.
            if (room.NewValue == null && client.IsConnected.Value && !reconnectPending && !rejoining)
                clearReconnectTarget();
        }

        private void onUserKicked(MultiplayerRoomUser user)
        {
            if (user.UserID == api.LocalUser.Value.Id)
                clearReconnectTarget();
        }

        protected override void Update()
        {
            base.Update();

            if (!reconnectPending || rejoining || refreshRequest != null || !AutoReconnect.Value
                || !this.IsCurrentScreen() || !client.IsConnected.Value
                || client.Room != null || lazerInfo.CurrentRoom.Value != null || operationTracker.InProgress.Value)
                return;

            if (reconnectRoomId == null || reconnectUserId != api.LocalUser.Value.Id)
            {
                clearReconnectTarget();
                return;
            }

            if (refreshedRoom is Room room)
            {
                refreshedRoom = null;
                reconnectPending = false;
                rejoining = true;
                Join(room, reconnectPassword, _ => rejoining = false, (message, _) =>
                {
                    rejoining = false;
                    if (!client.IsConnected.Value && AutoReconnect.Value && reconnectRoomId != null)
                        reconnectPending = true;
                    else
                        clearReconnectTarget();

                    Logger.Log(message, level: LogLevel.Important);
                });
                return;
            }

            var request = refreshRequest = new GetRoomRequest(reconnectRoomId.Value);
            refreshOperation = operationTracker.BeginOperation();
            request.Success += room => Schedule(() =>
            {
                if (refreshRequest != request)
                    return;

                finishRefresh();
                if (client.IsConnected.Value && AutoReconnect.Value)
                    refreshedRoom = room;
            });
            request.Failure += exception => Schedule(() =>
            {
                if (refreshRequest != request)
                    return;

                finishRefresh();
                if (client.IsConnected.Value)
                    clearReconnectTarget();

                Logger.Log($"Failed to refresh multiplayer room for reconnection: {exception.Message}", level: LogLevel.Important);
            });
            api.Queue(request);
        }

        protected override void JoinInternal(Room room, string? password, Action<Room> onSuccess, Action<string, Exception?> onFailure)
        {
            // Join() itself is scheduled; recovery may have been cancelled before it runs.
            if (rejoining && (!AutoReconnect.Value || reconnectRoomId != room.RoomID || !client.IsConnected.Value))
            {
                onFailure("Automatic room rejoin was cancelled.", null);
                return;
            }

            if (!rejoining)
                clearReconnectTarget();

            lazerInfo.Join(room, password, joined =>
            {
                reconnectRoomId = joined.RoomID;
                reconnectPassword = password ?? joined.Password;
                reconnectUserId = api.LocalUser.Value.Id;
                onSuccess(joined);
            }, onFailure);
        }

        private void finishRefresh()
        {
            refreshRequest = null;
            refreshOperation?.Dispose();
            refreshOperation = null;
        }

        private void cancelRefresh()
        {
            refreshRequest?.Cancel();
            finishRefresh();
            refreshedRoom = null;
        }

        private void cancelReconnect()
        {
            reconnectPending = false;
            cancelRefresh();
            if (rejoining)
                lazerInfo.Left();
        }

        private void clearReconnectTarget()
        {
            reconnectRoomId = null;
            reconnectPassword = null;
            reconnectPending = false;
            cancelRefresh();
        }

        protected override void Dispose(bool isDisposing)
        {
            disposed = true;
            cancelRefresh();
            if (rejoining && lazerInfo.IsNotNull())
                lazerInfo.Left();

            if (client.IsNotNull())
            {
                client.IsConnected.ValueChanged -= onConnectionChanged;
                client.UserKicked -= onUserKicked;
            }

            if (lazerInfo.IsNotNull())
            {
                lazerInfo.CurrentRoom.ValueChanged -= onCurrentRoomChanged;
                lazerInfo.RoomLeft -= clearReconnectTarget;
            }

            base.Dispose(isDisposing);
        }
    }
}
