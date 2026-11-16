// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
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

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        private readonly FillFlowContainer invitationList = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
        };

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
                    Children = new Drawable[]
                    {
                        new SettingsCheckbox
                        {
                            RelativeSizeAxes = Axes.X,
                            LabelText = "掉线后自动重新入房",
                            Current = { BindTarget = lounge.AutoReconnect }
                        },
                        new ControlPanel.Spacer(),
                        new TournamentSpriteText { Text = "收到的房间邀请" },
                        invitationList,
                        new RoundedButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 30,
                            Text = "清空邀请",
                            Action = () => invitationList.Clear()
                        }
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            client.InvitationReceived += onInvitationReceived;
            screenStack.Push(lounge);
        }

        private void onInvitationReceived(APIUser invitedBy, Room room, string password)
        {
            // Repeated invitations update the existing entry, including its password.
            foreach (var existing in invitationList.Children.Where(c => c.Name == room.RoomID.ToString()).ToArray())
                invitationList.Remove(existing, true);

            var entry = new FillFlowContainer
            {
                Name = room.RoomID.ToString(),
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Margin = new MarginPadding { Bottom = 10 },
            };
            entry.Add(new TournamentSpriteText
            {
                Text = $"{invitedBy.Username} 邀请你加入",
            });
            entry.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 40,
                Text = room.Name,
                Action = () =>
                {
                    if (!lounge.IsLoaded || !lounge.IsCurrentScreen() || !client.IsConnected.Value || client.Room != null)
                        return;

                    lounge.Join(room, password, _ => invitationList.Remove(entry, true));
                }
            });
            invitationList.Add(entry);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (client.IsNotNull())
                client.InvitationReceived -= onInvitationReceived;

            base.Dispose(isDisposing);
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
