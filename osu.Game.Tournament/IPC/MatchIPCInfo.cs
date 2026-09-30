// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.Chat;
using osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    public partial class MatchIPCInfo : CompositeComponent
    {
        [Resolved]
        private IBindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        public Bindable<TournamentBeatmap?> Beatmap { get; } = new Bindable<TournamentBeatmap?>();
        public Bindable<LegacyMods> Mods { get; } = new Bindable<LegacyMods>();
        public Bindable<TourneyState> State { get; } = new Bindable<TourneyState>();
        public Bindable<Channel?> ChatChannel { get; } = new Bindable<Channel?>();
        public BindableLong Score1 { get; } = new BindableLong();
        public BindableLong Score2 { get; } = new BindableLong();

        public BindableInt Team1Combo { get; } = new BindableInt();
        public BindableInt Team2Combo { get; } = new BindableInt();

        public BindableBool HasActiveMatch { get; } = new BindableBool();

        protected readonly BindableList<MatchRoomPlayerInfo> RoomPlayersInternal = new BindableList<MatchRoomPlayerInfo>();

        public IBindableList<MatchRoomPlayerInfo> RoomPlayers => RoomPlayersInternal;

        public MatchIPCInfo()
        {
            State.BindValueChanged(s => Logger.Log($"Tourney State turn {s.OldValue} to {s.NewValue}"));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            State.BindValueChanged(_ => updateTrackLooping());
            workingBeatmap.BindValueChanged(onWorkingBeatmapChanged, true);
        }

        protected override void Dispose(bool isDisposing)
        {
            workingBeatmap.ValueChanged -= onWorkingBeatmapChanged;

            base.Dispose(isDisposing);
        }

        private void onWorkingBeatmapChanged(ValueChangedEvent<WorkingBeatmap> _) => updateTrackLooping();

        private void updateTrackLooping() => workingBeatmap.Value.PrepareTrackForPreview(State.Value != TourneyState.Idle);

        protected void SetRoomPlayers(IEnumerable<MatchRoomPlayerInfo> players)
        {
            var playerArray = players.ToArray();

            if (RoomPlayersInternal.SequenceEqual(playerArray))
                return;

            RoomPlayersInternal.ReplaceRange(0, RoomPlayersInternal.Count, playerArray);
        }

        public virtual void RefreshChatChannel()
        {
        }

        public virtual bool PostChatMessage(string message) => false;

        internal virtual void SetLiveLeaderboardProvider(TournamentLiveLeaderboardProvider? provider)
        {
        }

        internal virtual void ForceReSpectate()
        {
        }
    }
}
