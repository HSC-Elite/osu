// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Bindables;

namespace osu.Game.Tournament.Models
{
    public class BanPickFlowGroup
    {
        public Bindable<string> Name = new Bindable<string>(string.Empty);

        public BindableList<BanPickFlowStep> Steps = new BindableList<BanPickFlowStep>();

        // Legacy repeat count. The editor expands this into individual steps.
        public Bindable<int> RepeatCount = new Bindable<int>(0);

        [JsonIgnore]
        public int TotalStep => Steps.Count * (System.Math.Max(0, RepeatCount.Value) + 1);

        public BanPickFlowGroup DeepClone()
        {
            var clone = new BanPickFlowGroup { Name = { Value = Name.Value } };
            clone.Steps.AddRange(Enumerable.Range(0, TotalStep).Select(i => Steps[i % Steps.Count].DeepClone()));
            return clone;
        }
    }
}
