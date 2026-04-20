// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Numerics;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osuTK.Graphics;
using Vector2 = osuTK.Vector2;

namespace osu.Game.Tournament.MultiWindow
{
    public interface ITournamentSynchronisedStatefulDrawable
    {
        string SynchronisationKey { get; }

        void ApplyState(string property, string? jsonValue);
    }

    public abstract partial class TournamentSynchronisedControlDrawable : CompositeDrawable, osu.Game.Skinning.ISerialisableDrawable
    {
        public bool IsEditable => false;

        public bool UsesFixedAnchor { get; set; } = true;

        [SettingSource("key")]
        public Bindable<string> KeyBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("enabled")]
        public BindableBool EnabledBindable { get; } = new BindableBool(true);

        [Resolved]
        private TournamentCompanionManager? companionManager { get; set; }

        protected void SendOperation(string operation, object? value = null)
        {
            if (!IsLoaded)
                return;

            companionManager?.SendAction(new TournamentWindowActionMessage
            {
                ActionType = TournamentWindowActionType.ControlOperation,
                ControlKey = KeyBindable.Value,
                ControlOperation = operation,
                JsonValue = value == null ? null : JsonConvert.SerializeObject(value),
            });
        }

        protected static T? Deserialise<T>(string? jsonValue)
            => jsonValue == null ? default : JsonConvert.DeserializeObject<T>(jsonValue);
    }

    public partial class TournamentSynchronisedButton : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
    {
        [SettingSource("text")]
        public Bindable<string> TextBindable { get; } = new Bindable<string>(string.Empty);

        public TournamentSynchronisedButton()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var button = new SynchronisedButtonDrawable();

            InternalChild = button;

            TextBindable.BindValueChanged(v => button.Text = v.NewValue, true);
            EnabledBindable.BindValueChanged(v => button.Enabled.Value = v.NewValue, true);
            button.Action = () => SendOperation("invoke");
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "enabled":
                    EnabledBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "text":
                    TextBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;
            }
        }
    }

    public partial class TournamentSynchronisedCheckbox : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
    {
        [SettingSource("label")]
        public Bindable<string> LabelBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("current")]
        public BindableBool CurrentBindable { get; } = new BindableBool();

        private bool suppressChanges;

        public TournamentSynchronisedCheckbox()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var checkbox = new SettingsCheckbox
            {
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
            };

            InternalChild = checkbox;

            LabelBindable.BindValueChanged(v => checkbox.LabelText = v.NewValue, true);
            EnabledBindable.BindValueChanged(v => checkbox.Current.Disabled = !v.NewValue, true);
            CurrentBindable.BindValueChanged(v =>
            {
                suppressChanges = true;
                checkbox.Current.Value = v.NewValue;
                suppressChanges = false;
            }, true);
            checkbox.Current.BindValueChanged(v =>
            {
                if (!suppressChanges)
                    SendOperation("set", v.NewValue);
            });
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "current":
                    CurrentBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "enabled":
                    EnabledBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "label":
                    LabelBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;
            }
        }
    }

    public partial class TournamentSynchronisedTextBox : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
    {
        [SettingSource("label")]
        public Bindable<string> LabelBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("current")]
        public Bindable<string> CurrentBindable { get; } = new Bindable<string>(string.Empty);

        private bool suppressChanges;

        public TournamentSynchronisedTextBox()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            OsuSpriteText label;
            SettingsTextBox textBox;

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    label = new OsuSpriteText
                    {
                        Font = FrameworkFont.Condensed.With(size: 18),
                        Colour = new Color4(220, 220, 220, 255),
                    },
                    textBox = new SettingsTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                }
            };

            LabelBindable.BindValueChanged(v => label.Text = v.NewValue, true);
            EnabledBindable.BindValueChanged(v => textBox.Current.Disabled = !v.NewValue, true);
            CurrentBindable.BindValueChanged(v =>
            {
                suppressChanges = true;
                textBox.Current.Value = v.NewValue;
                suppressChanges = false;
            }, true);
            textBox.Current.BindValueChanged(v =>
            {
                if (!suppressChanges)
                    SendOperation("set", v.NewValue);
            });
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "current":
                    CurrentBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;

                case "enabled":
                    EnabledBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "label":
                    LabelBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;
            }
        }
    }

    public partial class TournamentSynchronisedNullableIntTextBox : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
    {
        [SettingSource("label")]
        public Bindable<string> LabelBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("current")]
        public Bindable<int?> CurrentBindable { get; } = new Bindable<int?>();

        private bool suppressChanges;

        public TournamentSynchronisedNullableIntTextBox()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            OsuSpriteText label;
            BasicTextBox textBox;

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    label = new OsuSpriteText
                    {
                        Font = FrameworkFont.Condensed.With(size: 18),
                        Colour = new Color4(220, 220, 220, 255),
                    },
                    textBox = new NullableIntTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 30,
                    },
                }
            };

            LabelBindable.BindValueChanged(v => label.Text = v.NewValue, true);
            EnabledBindable.BindValueChanged(v => textBox.Current.Disabled = !v.NewValue, true);
            CurrentBindable.BindValueChanged(v =>
            {
                suppressChanges = true;
                textBox.Current.Value = v.NewValue?.ToString() ?? string.Empty;
                suppressChanges = false;
            }, true);
            textBox.OnCommit += (_, _) =>
            {
                if (suppressChanges)
                    return;

                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    SendOperation("set", (int?)null);
                    return;
                }

                if (int.TryParse(textBox.Text, out int value))
                    SendOperation("set", (int?)value);
                else
                    textBox.Current.Value = CurrentBindable.Value?.ToString() ?? string.Empty;
            };
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "current":
                    CurrentBindable.Value = Deserialise<int?>(jsonValue);
                    break;

                case "enabled":
                    EnabledBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "label":
                    LabelBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;
            }
        }

        private partial class NullableIntTextBox : BasicTextBox
        {
            protected override bool CanAddCharacter(char character) => char.IsAsciiDigit(character);
        }
    }

    public abstract partial class TournamentSynchronisedSlider<T> : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
        where T : struct, INumber<T>, System.Numerics.IMinMaxValue<T>
    {
        [SettingSource("label")]
        public Bindable<string> LabelBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("current")]
        public BindableNumber<T> CurrentBindable { get; } = new BindableNumber<T>();

        [SettingSource("minimum")]
        public Bindable<T> MinimumBindable { get; } = new Bindable<T>(T.MinValue);

        [SettingSource("maximum")]
        public Bindable<T> MaximumBindable { get; } = new Bindable<T>(T.MaxValue);

        private bool suppressChanges;

        protected TournamentSynchronisedSlider(Func<T, string> formatter)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            OsuSpriteText label;
            OsuSpriteText valueText;
            var sliderCurrent = new BindableNumber<T>
            {
                MinValue = MinimumBindable.Value,
                MaxValue = MaximumBindable.Value,
            };

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Children = new Drawable[]
                        {
                            label = new OsuSpriteText
                            {
                                Font = FrameworkFont.Condensed.With(size: 18),
                                Colour = new Color4(220, 220, 220, 255),
                            },
                            valueText = new OsuSpriteText
                            {
                                Font = FrameworkFont.Condensed.With(size: 16),
                                Colour = Color4.White,
                            }
                        }
                    },
                    new SettingsSlider<T>
                    {
                        RelativeSizeAxes = Axes.X,
                        Current = sliderCurrent,
                    }
                }
            };

            LabelBindable.BindValueChanged(v => label.Text = v.NewValue, true);
            CurrentBindable.BindValueChanged(v =>
            {
                suppressChanges = true;
                valueText.Text = formatter(v.NewValue);
                sliderCurrent.Value = v.NewValue;
                suppressChanges = false;
            }, true);
            MinimumBindable.BindValueChanged(v => sliderCurrent.MinValue = v.NewValue, true);
            MaximumBindable.BindValueChanged(v => sliderCurrent.MaxValue = v.NewValue, true);
            EnabledBindable.BindValueChanged(v => sliderCurrent.Disabled = !v.NewValue, true);
            sliderCurrent.BindValueChanged(v =>
            {
                if (!suppressChanges)
                    SendOperation("set", v.NewValue);
            });
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "current":
                    CurrentBindable.Value = Deserialise<T>(jsonValue);
                    break;

                case "minimum":
                    MinimumBindable.Value = Deserialise<T>(jsonValue);
                    break;

                case "maximum":
                    MaximumBindable.Value = Deserialise<T>(jsonValue);
                    break;

                case "enabled":
                    EnabledBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "label":
                    LabelBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;
            }
        }
    }

    public partial class TournamentSynchronisedIntSlider : TournamentSynchronisedSlider<int>
    {
        public TournamentSynchronisedIntSlider()
            : base(v => v.ToString())
        {
        }
    }

    public partial class TournamentSynchronisedDoubleSlider : TournamentSynchronisedSlider<double>
    {
        public TournamentSynchronisedDoubleSlider()
            : base(v => $"{v:P0}")
        {
        }
    }

    public partial class TournamentSynchronisedDropdown<T> : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
    {
        [SettingSource("label")]
        public Bindable<string> LabelBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("items_json")]
        public Bindable<string> ItemsJsonBindable { get; } = new Bindable<string>("[]");

        [SettingSource("current")]
        public Bindable<T> CurrentBindable { get; } = new Bindable<T>();

        private bool suppressChanges;

        public TournamentSynchronisedDropdown()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            OsuSpriteText label;
            OsuDropdown<T> dropdown;

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    label = new OsuSpriteText
                    {
                        Font = FrameworkFont.Condensed.With(size: 18),
                        Colour = new Color4(220, 220, 220, 255),
                    },
                    dropdown = new OsuDropdown<T>
                    {
                        RelativeSizeAxes = Axes.X,
                    }
                }
            };

            LabelBindable.BindValueChanged(v => label.Text = v.NewValue, true);
            ItemsJsonBindable.BindValueChanged(v => dropdown.Items = JsonConvert.DeserializeObject<T[]>(v.NewValue) ?? Array.Empty<T>(), true);
            CurrentBindable.BindValueChanged(v =>
            {
                suppressChanges = true;
                dropdown.Current.Value = v.NewValue;
                suppressChanges = false;
            }, true);
            EnabledBindable.BindValueChanged(v => dropdown.Current.Disabled = !v.NewValue, true);
            dropdown.Current.BindValueChanged(v =>
            {
                if (!suppressChanges)
                    SendOperation("set", v.NewValue);
            });
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "current":
                    CurrentBindable.Value = Deserialise<T>(jsonValue)!;
                    break;

                case "enabled":
                    EnabledBindable.Value = Deserialise<bool>(jsonValue);
                    break;

                case "label":
                    LabelBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;

                case "items_json":
                    ItemsJsonBindable.Value = Deserialise<string>(jsonValue) ?? "[]";
                    break;
            }
        }
    }

    public partial class TournamentSynchronisedTextDisplay : TournamentSynchronisedControlDrawable, ITournamentSynchronisedStatefulDrawable
    {
        [SettingSource("text")]
        public Bindable<string> TextBindable { get; } = new Bindable<string>(string.Empty);

        [SettingSource("font_size")]
        public BindableFloat FontSizeBindable { get; } = new BindableFloat(16);

        [SettingSource("emphasis")]
        public BindableBool EmphasisBindable { get; } = new BindableBool();

        public TournamentSynchronisedTextDisplay()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            TournamentSpriteText text = new TournamentSpriteText
            {
                RelativeSizeAxes = Axes.X,
                AllowMultiline = true,
                Colour = Color4.White,
            };

            InternalChild = text;

            TextBindable.BindValueChanged(v => text.Text = v.NewValue, true);
            FontSizeBindable.BindValueChanged(v => text.Font = (EmphasisBindable.Value ? FrameworkFont.Regular : FrameworkFont.Condensed).With(size: v.NewValue), true);
            EmphasisBindable.BindValueChanged(v => text.Font = (v.NewValue ? FrameworkFont.Regular : FrameworkFont.Condensed).With(size: FontSizeBindable.Value), true);
        }

        public string SynchronisationKey => KeyBindable.Value;

        public void ApplyState(string property, string? jsonValue)
        {
            switch (property)
            {
                case "text":
                    TextBindable.Value = Deserialise<string>(jsonValue) ?? string.Empty;
                    break;

                case "font_size":
                    FontSizeBindable.Value = Deserialise<float>(jsonValue);
                    break;

                case "emphasis":
                    EmphasisBindable.Value = Deserialise<bool>(jsonValue);
                    break;
            }
        }
    }

    public partial class TournamentSynchronisedSpacer : TournamentSynchronisedControlDrawable
    {
        [SettingSource("height")]
        public BindableFloat HeightBindable { get; } = new BindableFloat(20);

        public TournamentSynchronisedSpacer()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.None;
            HeightBindable.BindValueChanged(v => Height = v.NewValue, true);
        }
    }

    internal partial class SynchronisedButtonDrawable : TourneyButton
    {
        public Color4 TextColour
        {
            set => SpriteText.Colour = value;
        }

        public SynchronisedButtonDrawable()
        {
            RelativeSizeAxes = Axes.X;
            Height = 34;
            BackgroundColour = new Color4(50, 90, 110, 255);
            TextColour = Color4.White;
        }

        protected override SpriteText CreateText() => new OsuSpriteText
        {
            Depth = -1,
            Origin = Anchor.Centre,
            Anchor = Anchor.Centre,
            Font = FrameworkFont.Condensed,
            Colour = Color4.White,
        };
    }
}
