// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Components;
using osu.Game.Screens;
using osu.Game.Screens.Footer;

namespace osu.Game.Tournament.Screens.RoomSelect
{
    public partial class RoomSelectScreen : TournamentScreen
    {
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        private readonly RoomSelectMultiplayerLoungeSubScreen lounge = new RoomSelectMultiplayerLoungeSubScreen();
        private readonly OsuScreenStack screenStack;
        private readonly ScreenFooter.BackReceptor backReceptor;
        private bool inputEnabled;

        public override bool PropagateNonPositionalInputSubTree => inputEnabled && base.PropagateNonPositionalInputSubTree;

        public override bool PropagatePositionalInputSubTree => inputEnabled && base.PropagatePositionalInputSubTree;

        public RoomSelectScreen()
        {
            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;

            InternalChildren = new Drawable[]
            {
                backReceptor = new ScreenFooter.BackReceptor(),
                screenStack = new OsuScreenStack
                {
                    RelativeSizeAxes = Axes.Both
                },
                new PopoverContainer
                {
                    Depth = -1,
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStackFooter(screenStack, backReceptor)
                    {
                        BackButtonPressed = handleBackButton
                    }
                },
                new ControlPanel
                {
                    Child = new SettingsCheckbox
                    {
                        RelativeSizeAxes = Axes.X,
                        LabelText = "掉线后自动重新入房",
                        Current = { BindTarget = lounge.AutoReconnect }
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            screenStack.Push(lounge);
        }

        public override void Show()
        {
            inputEnabled = true;
            base.Show();
        }

        public override void Hide()
        {
            inputEnabled = false;

            // AlwaysPresent keeps reconnection processing alive, but also keeps a hidden
            // search box valid for focus. Release it explicitly when hiding.
            if (GetContainingInputManager()?.FocusedDrawable.FindClosestParent<RoomSelectScreen>() == this)
                GetContainingFocusManager()?.ChangeFocus(null);

            base.Hide();
        }

        private void handleBackButton()
        {
            if (!(screenStack.CurrentScreen is IOsuScreen currentScreen)) return;

            if (screenStack.CurrentScreen is RoomSelectMultiplayerLoungeSubScreen) return;

            if (!((Drawable)currentScreen).IsLoaded || (currentScreen.AllowUserExit && !currentScreen.OnBackButton())) screenStack.Exit();
        }
    }
}
