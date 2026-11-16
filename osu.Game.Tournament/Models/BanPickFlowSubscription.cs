// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using osu.Framework.Bindables;

namespace osu.Game.Tournament.Models
{
    /// <summary>Observes nested flow edits, including changes made while the broadcast is visible.</summary>
    public sealed class BanPickFlowSubscription : IDisposable
    {
        private readonly TournamentRound round;
        private readonly Action changed;
        private readonly List<Action> unbind = new List<Action>();

        public BanPickFlowSubscription(TournamentRound round, Action changed)
        {
            this.round = round;
            this.changed = changed;
            bind();
        }

        private void bind()
        {
            Dispose();
            observe(round.BanPickFlowGroups);
            observe(round.Beatmaps);
            observe(round.BestOf);
            observe(round.SeparateTiebreaker);
            observe(round.BanPickFlowBestOf);
            foreach (var group in round.BanPickFlowGroups)
            {
                observe(group.Name);
                observe(group.RepeatCount);
                observe(group.Steps);
                foreach (var step in group.Steps)
                {
                    observe(step.CurrentAction);
                    observe(step.TeamRule);
                    observe(step.SwapFromLastColor);
                }
            }
        }

        private void observe<T>(Bindable<T> value)
        {
            void onChanged(ValueChangedEvent<T> _) => changed();
            value.ValueChanged += onChanged;
            unbind.Add(() => value.ValueChanged -= onChanged);
        }

        private void observe<T>(BindableList<T> list)
        {
            void onChanged(object? sender, NotifyCollectionChangedEventArgs args)
            {
                bind();
                changed();
            }
            list.CollectionChanged += onChanged;
            unbind.Add(() => list.CollectionChanged -= onChanged);
        }

        public void Dispose()
        {
            foreach (var action in unbind)
                action();
            unbind.Clear();
        }
    }
}
