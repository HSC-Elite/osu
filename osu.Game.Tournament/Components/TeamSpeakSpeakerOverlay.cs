// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Tournament.Audio;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    internal partial class TeamSpeakSpeakerOverlay : FillFlowContainer
    {
        private readonly Bindable<IReadOnlyList<TeamSpeakSpeaker>> speakers = new Bindable<IReadOnlyList<TeamSpeakSpeaker>>();

        public TeamSpeakSpeakerOverlay()
        {
            AutoSizeAxes = Axes.Both;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 5);
        }

        [BackgroundDependencyLoader]
        private void load(TeamSpeakSession session)
        {
            speakers.BindTo(session.Speakers);
            speakers.BindValueChanged(change =>
            {
                Clear();
                foreach (var speaker in change.NewValue)
                    Add(new SpeakerRow(speaker));
            }, true);
        }

        protected override void Update()
        {
            base.Update();

            foreach (var child in Children)
                ((SpeakerRow)child).UpdateSpeaker();
        }

        private partial class SpeakerRow : CompositeDrawable
        {
            private readonly TeamSpeakSpeaker speaker;
            private readonly Sprite avatar;
            private readonly OsuSpriteText fallback;

            public SpeakerRow(TeamSpeakSpeaker speaker)
            {
                this.speaker = speaker;
                Size = new Vector2(230, 36);
                Masking = true;
                CornerRadius = 6;
                InternalChildren = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(19, 25, 31, 220) },
                    new Box { RelativeSizeAxes = Axes.Y, Width = 3, Colour = Color4.MediumSeaGreen },
                    new CircularContainer
                    {
                        Masking = true,
                        Size = new Vector2(26),
                        X = 8,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.33f) },
                            fallback = new OsuSpriteText
                            {
                                Text = "?",
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Font = OsuFont.Default.With(size: 16)
                            },
                            avatar = new Sprite { RelativeSizeAxes = Axes.Both, FillMode = FillMode.Fill }
                        },
                    },
                    new TruncatingSpriteText
                    {
                        Text = speaker.Name,
                        X = 41,
                        Width = 182,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.Default.With(size: 18)
                    },
                };
            }

            public void UpdateSpeaker()
            {
                Alpha = speaker.IsSpeaking ? 1 : 0.5f;
                avatar.Texture = speaker.Avatar;
                fallback.Alpha = speaker.Avatar == null ? 1 : 0;
            }
        }
    }
}
