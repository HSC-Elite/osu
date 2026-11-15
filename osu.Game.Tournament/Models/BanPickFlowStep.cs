// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using osu.Framework.Bindables;

namespace osu.Game.Tournament.Models
{
    public class BanPickFlowStep
    {
        public readonly Bindable<ChoiceType> CurrentAction = new Bindable<ChoiceType>();
        public readonly Bindable<BanPickTeamRule> TeamRule = new Bindable<BanPickTeamRule>();

        // Retained for reading existing tournament files. New steps use TeamRule.
        public readonly BindableBool SwapFromLastColor = new BindableBool();

        public BanPickFlowStep DeepClone() => new BanPickFlowStep
        {
            CurrentAction = { Value = CurrentAction.Value },
            TeamRule = { Value = TeamRule.Value },
            SwapFromLastColor = { Value = SwapFromLastColor.Value }
        };
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum BanPickTeamRule
    {
        [Description("旧配置（按交换队伍设置）")]
        Legacy,
        [Description("先手队")]
        First,
        [Description("后手队")]
        Second,
        [Description("上一步的队伍")]
        Previous,
        [Description("上一步的另一队")]
        Opponent,
        [Description("红队")]
        Red,
        [Description("蓝队")]
        Blue
    }
}
