// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Game.Online.Spectator;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.StableClient
{
    /// <summary>
    /// Adapts room-owned stable spectator connections to the shared tournament playback pipeline.
    /// Watch registrations control delivery; the room retains ownership of the connections.
    /// </summary>
    public class StableSpectatorDataSource : ISpectatorDataSource
    {
        private readonly Dictionary<int, int> registrations = new Dictionary<int, int>();
        private readonly Dictionary<int, SpectatorState> states = new Dictionary<int, SpectatorState>();
        private readonly BindableDictionary<int, SpectatorState> watchedStates = new BindableDictionary<int, SpectatorState>();

        public IBindableDictionary<int, SpectatorState> WatchedUserStates => watchedStates;
        public event Action<int, SpectatorState>? OnUserBeganPlaying;
        public event Action<int, FrameDataBundle>? OnNewFrames;

        public void WatchUser(int userId)
        {
            registrations[userId] = registrations.GetValueOrDefault(userId) + 1;
            if (registrations[userId] == 1 && states.TryGetValue(userId, out var state))
                watchedStates[userId] = state;
        }

        public void StopWatchingUser(int userId)
        {
            if (!registrations.TryGetValue(userId, out int count))
                return;
            if (count > 1)
                registrations[userId] = count - 1;
            else
            {
                registrations.Remove(userId);
                watchedStates.Remove(userId);
            }
        }

        internal void ReceiveFrames(int userId, SpectatorState state, FrameDataBundle bundle)
        {
            bool beganPlaying = !states.TryGetValue(userId, out var previous)
                                || previous.BeatmapID != state.BeatmapID
                                || previous.RulesetID != state.RulesetID;
            if (beganPlaying)
                states[userId] = state;

            if (!registrations.ContainsKey(userId))
                return;

            if (beganPlaying)
            {
                watchedStates[userId] = state;
                OnUserBeganPlaying?.Invoke(userId, state);
            }
            OnNewFrames?.Invoke(userId, bundle);
        }

        internal void ReceiveAction(int userId, ReplayAction action)
        {
            if (action == ReplayAction.NewSong)
            {
                RemoveUser(userId);
                return;
            }

            if (!states.TryGetValue(userId, out var previous))
                return;

            SpectatedUserState? next = action switch
            {
                ReplayAction.Completion => SpectatedUserState.Passed,
                // Stable multiplayer players can continue sending frames after failing.
                ReplayAction.Fail => null,
                ReplayAction.SongSelect or ReplayAction.WatchingOther => SpectatedUserState.Quit,
                _ => null,
            };
            if (next == null)
                return;

            var state = new SpectatorState
            {
                BeatmapID = previous.BeatmapID,
                RulesetID = previous.RulesetID,
                Mods = previous.Mods,
                MaximumStatistics = previous.MaximumStatistics,
                State = next.Value,
            };
            states[userId] = state;
            if (registrations.ContainsKey(userId))
                watchedStates[userId] = state;
        }

        internal void RemoveUser(int userId)
        {
            states.Remove(userId);
            watchedStates.Remove(userId);
        }

        internal void Reset()
        {
            states.Clear();
            watchedStates.Clear();
        }
    }
}
