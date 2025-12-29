// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;

namespace osu.Game.Tournament.Models
{
    public class ModMultiplierSetting
    {
        public Bindable<string> ModAcronym { get; set; } = new Bindable<string>();

        public BindableDouble Multiplier { get; set; } = new BindableDouble(1)
        {
            MinValue = 0,
            MaxValue = 10,
            Precision = 0.1,
            Default = 1,
        };
    }
}
