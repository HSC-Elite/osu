// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Configuration
{
    public enum ReleaseStream
    {
        [Description("通用端")]
        General,

        [Description("提现端")]
        MulCoin,

        [Description("Lazer比赛直播端")]
        LazerMatch
    }
}
