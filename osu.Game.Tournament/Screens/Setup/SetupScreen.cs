// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Setup
{
    public partial class SetupScreen : TournamentScreen
    {
        private FillFlowContainer fillFlow = null!;

        private LoginOverlay? loginOverlay;
        private ResolutionSelector resolution = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        private Bindable<int> stageSize = null!;

        [BackgroundDependencyLoader]
        private void load(TournamentConfigManager config)
        {
            stageSize = config.GetBindable<int>(StorageConfig.StageHeight);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourProvider.Background5,
                },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = fillFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(10),
                        Spacing = new Vector2(10),
                    },
                },
            };

            api.LocalUser.BindValueChanged(_ => Schedule(reload));
            reload();
        }

        private void reload()
        {
            fillFlow.Children = new Drawable[]
            {
                new UpdateCheckAction(),
                new ActionableInfo
                {
                    Label = "Current user",
                    ButtonText = "Change sign-in",
                    Action = () =>
                    {
                        api.Logout();

                        if (loginOverlay == null)
                        {
                            AddInternal(loginOverlay = new LoginOverlay
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                            });
                        }

                        loginOverlay.State.Value = Visibility.Visible;
                    },
                    Value = api.LocalUser.Value.Username,
                    Failing = api.IsLoggedIn != true,
                    Description = "In order to access the API and display metadata, signing in is required."
                },
                new LabelledDropdown<RulesetInfo?>(padded: true)
                {
                    Label = "Ruleset",
                    Description = "Decides what stats are displayed and which ranks are retrieved for players. This requires a restart to reload data for an existing bracket.",
                    Items = rulesets.AvailableRulesets,
                    Current = LadderInfo.Ruleset,
                    DropdownWidth = 0.5f,
                },
                new LabelledEnumDropdown<TournamentScoringMode>(padded: true)
                {
                    Label = "Scoring mode",
                    Description = "Chooses whether live match scores use the legacy score or the calculated performance value.",
                    Current = LadderInfo.ScoringMode,
                    DropdownWidth = 0.5f,
                },
                new TournamentSwitcher
                {
                    Label = "Current tournament",
                    Description = "Changes the background videos and bracket to match the selected tournament. This requires a restart to apply changes.",
                },
                new RoundBeatmapDownloadAction(),
                resolution = new ResolutionSelector
                {
                    Label = "Stream area resolution",
                    ButtonText = "Set height",
                    Action = height =>
                    {
                        stageSize.Value = height;
                    }
                },
                new LabelledSwitchButton
                {
                    Label = "Auto advance screens",
                    Description = "Screens will progress automatically from gameplay -> results -> map pool",
                    Current = LadderInfo.AutoProgressScreens,
                },
                new LabelledSwitchButton
                {
                    Label = "Display team seeds",
                    Description = "Team seeds will display alongside each team at the top in gameplay/map pool screens.",
                    Current = LadderInfo.DisplayTeamSeeds,
                },
                new LabelledSwitchButton
                {
                    Label = "Invert score colour",
                    Current = LadderInfo.InvertScoreColour,
                },
                new LabelledSwitchButton
                {
                    Label = "Use alternate chat source",
                    Current = LadderInfo.UseAlternateChatSource,
                },
                new LabelledSwitchButton
                {
                    Label = "启用回合预览",
                    Current = LadderInfo.EnableRoundPreview,
                },
                new LabelledSliderBar<float>
                {
                    Label = "Idle player username font size",
                    Current = LadderInfo.IdleScreenUsernameFontSize,
                }
            };
        }

        protected override void Update()
        {
            base.Update();

            resolution.Value = $"{ScreenSpaceDrawQuad.Width:N0}x{ScreenSpaceDrawQuad.Height:N0}";
        }
    }
}
