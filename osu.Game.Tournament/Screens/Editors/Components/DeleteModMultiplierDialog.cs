// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Tournament.Screens.Editors.Components
{
    public partial class DeleteModMultiplierDialog : DeletionDialog
    {
        public DeleteModMultiplierDialog(Action action)
        {
            HeaderText = @"Delete mod multiplier?";
            DangerousAction = action;
        }
    }
}
