// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;

namespace osu.Game.Online.Spectator
{
    /// <summary>
    /// Supplies state and replay frames for users currently being spectated.
    /// </summary>
    public interface ISpectatorDataSource
    {
        /// <summary>
        /// Latest known gameplay state for each watched user.
        /// </summary>
        IBindableDictionary<int, SpectatorState> WatchedUserStates { get; }

        /// <summary>
        /// Raised when a watched user begins a gameplay session.
        /// </summary>
        event Action<int, SpectatorState>? OnUserBeganPlaying;

        /// <summary>
        /// Raised when replay frames arrive for a watched user.
        /// </summary>
        event Action<int, FrameDataBundle>? OnNewFrames;

        /// <summary>
        /// Adds one watch registration. Each call must be balanced by <see cref="StopWatchingUser"/>.
        /// </summary>
        void WatchUser(int userId);

        /// <summary>
        /// Removes one watch registration.
        /// </summary>
        void StopWatchingUser(int userId);
    }
}
