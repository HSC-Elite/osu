// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Screens;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.StableClient.IPC;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableTournamentIdleScreen : OsuScreen
    {
        [Resolved]
        private StableMatchIPCInfo stableIpc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladderInfo { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> globalWorkingBeatmap { get; set; } = null!;

        [Resolved]
        private MusicController musicController { get; set; } = null!;

        private StableTournamentGrid grid = null!;
        private bool isActive;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            ladderInfo.PlayersPerTeam.BindValueChanged(_ => rebuildLayout(), true);
            globalWorkingBeatmap.BindValueChanged(beatmap =>
            {
                if (isActive)
                    Schedule(restartIdleTrack);
            }, true);
        }

        private void rebuildLayout()
        {
            int playersPerTeam = ladderInfo.PlayersPerTeam.Value;
            grid = new StableTournamentGrid(playersPerTeam);

            for (int i = 0; i < grid.SlotCount; i++)
            {
                var stack = new OsuScreenStack
                {
                    RelativeSizeAxes = Axes.Both,
                };

                stack.Push(new StableTournamentIdlePlayer(i, playersPerTeam));
                grid.GetSlot(i).Add(stack);
            }

            InternalChild = grid;
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            isActive = true;
            restartIdleTrack();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            isActive = false;
            stopIdleTrack();
            return base.OnExiting(e);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);
            isActive = false;
            stopIdleTrack();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            isActive = true;
            restartIdleTrack();
        }

        private void restartIdleTrack()
        {
            var beatmap = globalWorkingBeatmap.Value;

            if (beatmap == null)
                return;

            musicController.Stop();
            musicController.AllowTrackControl.Value = true;

            if (!ReferenceEquals(beatmap, Beatmap.Value))
                Beatmap.Value = beatmap;

            if (!beatmap.TrackLoaded)
                beatmap.LoadTrack();

            beatmap.Track.RestartPoint = 0;

            Schedule(() =>
            {
                musicController.CurrentTrack.Looping = true;
                musicController.Play(restart: true);
            });
        }

        private void stopIdleTrack()
        {
            musicController.Stop();
        }
    }
}
