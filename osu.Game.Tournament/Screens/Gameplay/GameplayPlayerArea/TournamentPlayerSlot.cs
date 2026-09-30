// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.IO.Stores;
using osu.Framework.Utils;
using osu.Game.Screens;
using osu.Game.Tournament.IO;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentPlayerSlot : CompositeDrawable
    {
        public TeamColour TeamColour { get; }
        public int Index { get; }
        public int? UserId { get; private set; }

        public bool HasGameplay => gameplayPresentation != null;
        public bool PlayerLoaded => gameplayPresentation?.PlayerLoaded == true;
        public TournamentPlayerPresentation? GameplayPresentation => gameplayPresentation;

        private readonly OsuScreenStack stack;
        private readonly IdlePlayerScreen idleScreen;
        private TournamentPlayerPresentation? gameplayPresentation;
        private readonly BindableInt playerCombo = new BindableInt();
        private readonly List<ISample> samples = new List<ISample>();
        private IBindable<int>? comboSource;
        private bool suppressComboSound;

        [Resolved]
        private AudioManager audioManager { get; set; } = null!;

        [Resolved]
        private TournamentStorage storage { get; set; } = null!;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        public TournamentPlayerSlot(TeamColour colour, int index)
        {
            TeamColour = colour;
            Index = index;
            RelativeSizeAxes = Axes.Both;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Child = stack = new OsuScreenStack
                {
                    RelativeSizeAxes = Axes.Both,
                },
            };

            stack.Push(idleScreen = new IdlePlayerScreen(index, colour));
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            playerCombo.BindValueChanged(comboChanged);
            initSamples();
        }

        public void ApplySlotInfo(TournamentPlayerSlotInfo info)
        {
            if (UserId != info.User?.UserId)
            {
                ResetToIdle();
                ResetWindowTransform();
            }

            UserId = info.User?.UserId;
        }

        public void StartGameplay(TournamentPlayerPresentation presentation)
        {
            if (HasGameplay)
                return;

            if (UserId == null)
                return;

            gameplayPresentation = presentation;
            stack.Push(presentation);
            bindCombo(presentation.Combo);
        }

        public void ResetToIdle()
        {
            bindCombo(null);

            if (stack.CurrentScreen != idleScreen)
                stack.Exit();

            gameplayPresentation = null;
        }

        public void MarkFailedOrQuit()
        {
            gameplayPresentation?.MarkFailedOrQuit();
        }

        public void ForceToResult()
        {
            gameplayPresentation?.ForceToResult();
        }

        public void FlyingLaunch(bool clockwise)
        {
            ClearTransforms();

            this.ScaleTo(0.9f, 1200, Easing.InQuint)
                .RotateTo(180f * (clockwise ? 1 : -1), 1200, Easing.InQuint)
                .MoveToOffset(new Vector2(0, -50), 1200, Easing.InQuint);

            this.Delay(1200f)
                .RotateTo(720f * (clockwise ? 1 : -1), 5000, Easing.OutQuint)
                .MoveToOffset(new Vector2(0, -DrawHeight * 4), 5000, Easing.OutQuint);
        }

        public void ResetWindowTransform()
        {
            ClearTransforms();
            this.ScaleTo(1, 5000, Easing.InOutSine);
            this.MoveTo(Vector2.Zero, 5000, Easing.InOutSine);
            this.RotateTo(0, 5000, Easing.InOutSine);
        }

        private void bindCombo(IBindable<int>? source)
        {
            suppressComboSound = true;

            if (comboSource != null)
                playerCombo.UnbindFrom(comboSource);

            comboSource = source;

            if (comboSource == null)
                playerCombo.Value = 0;
            else
                ((IBindable<int>)playerCombo).BindTo(comboSource);

            suppressComboSound = false;
        }

        private void comboChanged(ValueChangedEvent<int> combo)
        {
            if (suppressComboSound || ipc.State.Value != TourneyState.Playing)
                return;

            if (combo.NewValue >= combo.OldValue || combo.OldValue < 20 || samples.Count == 0)
                return;

            samples[RNG.Next(0, samples.Count)].Play();
        }

        private void initSamples()
        {
            var sampleStore = audioManager.GetSampleStore(new NamespacedResourceStore<byte[]>(new StorageBackedResourceStore(storage), "Lmao"));
            string samplePath = storage.GetFullPath("Lmao");

            if (!Directory.Exists(samplePath))
                return;

            foreach (string file in Directory.GetFiles(samplePath))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var sample = sampleStore.Get(name);

                if (sample != null)
                    samples.Add(sample);
            }

            Scheduler.Add(() => samples.RemoveAll(sample => sample.Length == 0));
        }

        protected override void Dispose(bool isDisposing)
        {
            bindCombo(null);
            base.Dispose(isDisposing);
        }
    }
}
