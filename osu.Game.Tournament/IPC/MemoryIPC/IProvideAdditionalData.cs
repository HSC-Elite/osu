// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Game.Online.Chat;

namespace osu.Game.Tournament.IPC.MemoryIPC
{
    public interface IProvideAdditionalData
    {
        SlotPlayerStatus[] SlotPlayers { get; }

        Bindable<Channel> TourneyChatChannel { get; }

        BindableInt Team1Combo { get; }

        BindableInt Team2Combo { get; }

        int PlayTime { get; }
    }
}
