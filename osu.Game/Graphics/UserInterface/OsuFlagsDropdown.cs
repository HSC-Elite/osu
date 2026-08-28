using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Input;

namespace osu.Game.Graphics.UserInterface
{
    public partial class OsuFlagsDropdown<T> : OsuDropdown<T>
        where T : struct, Enum
    {
        private static T[] individualFlags => Enum.GetValues<T>()
                                                  .Where(isIndividualFlag)
                                                  .Distinct()
                                                  .ToArray();

        private T[] configuredItems = Array.Empty<T>();
        private T[] displayedItems = Array.Empty<T>();

        private Bindable<T>? boundCurrent;

        /// <summary>
        /// Text permanently displayed in the dropdown header.
        /// This does not reflect the currently selected flags.
        /// </summary>
        public LocalisableString HeaderText
        {
            get => ((FlagsDropdownHeader)Header).PromptText;
            set => ((FlagsDropdownHeader)Header).PromptText = value;
        }

        /// <summary>
        /// Flags which should normally be displayed by this dropdown.
        ///
        /// Any declared individual flag which is currently active but absent
        /// from this collection will be displayed temporarily until disabled.
        /// </summary>
        public new IEnumerable<T> Items
        {
            get => configuredItems;
            set
            {
                T[] newItems = (value ?? Enumerable.Empty<T>())
                               .Distinct()
                               .ToArray();

                foreach (T item in newItems)
                {
                    if (!Enum.IsDefined(item))
                        throw new ArgumentException(
                            $"{item} is not a declared value of {typeof(T).Name}.",
                            nameof(value));

                    if (!isIndividualFlag(item))
                        throw new ArgumentException(
                            $"{item} is not an individual non-zero flag.",
                            nameof(value));
                }

                configuredItems = newItems;

                refreshDisplayedItems();
            }
        }

        public OsuFlagsDropdown()
        {
            if (!Attribute.IsDefined(typeof(T), typeof(FlagsAttribute)))
            {
                throw new ArgumentException(
                    $"{typeof(T).Name} must be decorated with [Flags].");
            }

            HeaderText = "Select options...";

            // By default expose every declared individual flag.
            Items = individualFlags;
        }

        protected override DropdownHeader CreateHeader()
            => new FlagsDropdownHeader();

        protected override DropdownMenu CreateMenu()
            => new FlagsDropdownMenu(this);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            boundCurrent = Current;
            boundCurrent.ValueChanged += currentChanged;

            // Important for the case where Current was assigned through an
            // object initialiser before this drawable was loaded.
            refreshDisplayedItems();
        }

        private void currentChanged(ValueChangedEvent<T> e)
        {
            // Do this on the scheduler because this value change may originate
            // from a menu item which is currently processing a click.
            Scheduler.AddOnce(refreshDisplayedItems);
        }

        private void refreshDisplayedItems()
        {
            T[] desiredItems = configuredItems
                               .Concat(getActiveHiddenFlags())
                               .Distinct()
                               .ToArray();

            if (displayedItems.SequenceEqual(desiredItems))
                return;

            displayedItems = desiredItems;

            // Do not use base.Items here.
            //
            // Dropdown<T>.Items performs single-selection validation and may
            // replace a composite Current value such as A | B with one item.
            //
            // ClearItems/AddDropdownItem do not perform that validation.
            base.ClearItems();

            foreach (T item in displayedItems)
                base.AddDropdownItem(item);
        }

        private IEnumerable<T> getActiveHiddenFlags()
        {
            ulong current = toBits(Current.Value);

            foreach (T flag in individualFlags)
            {
                if (configuredItems.Contains(flag))
                    continue;

                ulong bit = toBits(flag);

                if ((current & bit) != 0)
                    yield return flag;
            }
        }

        private void toggle(T flag)
        {
            if (Current.Disabled)
                return;

            ulong current = toBits(Current.Value);
            ulong bit = toBits(flag);

            // Items are guaranteed to be individual flags, so XOR is exactly
            // equivalent to toggling that bit while preserving every other bit.
            Current.Value = fromBits(current ^ bit);
        }

        private bool isActive(T flag)
        {
            ulong current = toBits(Current.Value);
            ulong bit = toBits(flag);

            return (current & bit) != 0;
        }

        private static bool isIndividualFlag(T value)
        {
            ulong bits = toBits(value);

            // Non-zero power of two.
            return bits != 0 && (bits & (bits - 1)) == 0;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (boundCurrent != null)
                boundCurrent.ValueChanged -= currentChanged;

            base.Dispose(isDisposing);
        }

        private partial class FlagsDropdownHeader : OsuDropdownHeader,
                                                    IKeyBindingHandler<PlatformAction>
        {
            private LocalisableString promptText;

            public LocalisableString PromptText
            {
                get => promptText;
                set
                {
                    promptText = value;
                    Text.Text = value;
                }
            }

            /// <summary>
            /// Dropdown<T> continuously writes its single selected value into
            /// Label. Ignore that behaviour and always display PromptText.
            /// </summary>
            protected override LocalisableString Label
            {
                get => promptText;
                set => Text.Text = promptText;
            }

            /// <summary>
            /// Prevent Dropdown<T>'s normal up/down behaviour from replacing
            /// the whole flags value with one item.
            /// </summary>
            protected override bool OnKeyDown(KeyDownEvent e)
            {
                switch (e.Key)
                {
                    case Key.Up:
                    case Key.Down:
                        return false;

                    default:
                        return base.OnKeyDown(e);
                }
            }

            /// <summary>
            /// DropdownHeader normally maps list-start/list-end actions to
            /// changing the singular selected item. That concept doesn't apply
            /// to a flags dropdown.
            /// </summary>
            bool IKeyBindingHandler<PlatformAction>.OnPressed(
                KeyBindingPressEvent<PlatformAction> e)
            {
                return false;
            }

            void IKeyBindingHandler<PlatformAction>.OnReleased(
                KeyBindingReleaseEvent<PlatformAction> e)
            {
            }
        }

        private partial class FlagsDropdownMenu : OsuDropdownMenu
        {
            private readonly OsuFlagsDropdown<T> dropdown;

            public FlagsDropdownMenu(OsuFlagsDropdown<T> dropdown)
            {
                this.dropdown = dropdown;
            }

            protected override DrawableDropdownMenuItem CreateDrawableDropdownMenuItem(
                MenuItem item)
            {
                var typedItem = (DropdownMenuItem<T>)item;

                // Replace Dropdown<T>'s action:
                //
                //     Current.Value = value;
                //     Menu.Close();
                //
                // with flag toggling behaviour.
                typedItem.Action.Value = () => dropdown.toggle(typedItem.Value);

                return new DrawableFlagDropdownMenuItem(typedItem, dropdown)
                {
                    BackgroundColourHover = HoverColour,
                    BackgroundColourSelected = SelectionColour,
                };
            }
        }

        private partial class DrawableFlagDropdownMenuItem
            : OsuDropdownMenu.DrawableOsuDropdownMenuItem
        {
            private readonly OsuFlagsDropdown<T> dropdown;

            private SpriteIcon stateIcon = null!;
            private Bindable<T>? boundCurrent;

            private T Value => ((DropdownMenuItem<T>)Item).Value;

            /// <summary>
            /// Same behaviour as DrawableStatefulMenuItem:
            /// toggling an option leaves the menu open.
            /// </summary>
            public override bool CloseMenuOnClick => false;

            public DrawableFlagDropdownMenuItem(
                DropdownMenuItem<T> item,
                OsuFlagsDropdown<T> dropdown)
                : base(item)
            {
                this.dropdown = dropdown;
            }

            protected override Drawable CreateContent()
            {
                var content = new FlagContent();

                stateIcon = content.StateIcon;

                return content;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                boundCurrent = dropdown.Current;
                boundCurrent.ValueChanged += currentChanged;

                updateState();
            }

            private void currentChanged(ValueChangedEvent<T> e)
            {
                updateState();
            }

            private void updateState()
            {
                stateIcon.Alpha = dropdown.isActive(Value) ? 1 : 0;
            }

            /// <summary>
            /// IsSelected belongs to Dropdown<T>'s singular-selection model.
            /// It must not be used to visualise flag state.
            ///
            /// Only preselection/hover should affect the background.
            /// </summary>
            protected override void UpdateBackgroundColour()
            {
                Background.FadeColour(
                    IsPreSelected
                        ? BackgroundColourHover
                        : BackgroundColour,
                    100,
                    Easing.OutQuint);

                if (IsPreSelected)
                    Background.FadeIn(100, Easing.OutQuint);
                else
                    Background.FadeOut(600, Easing.OutQuint);
            }

            protected override void UpdateForegroundColour()
            {
                Foreground.FadeColour(
                    IsPreSelected
                        ? ForegroundColourHover
                        : ForegroundColour,
                    100,
                    Easing.OutQuint);
            }

            protected override void Dispose(bool isDisposing)
            {
                if (boundCurrent != null)
                    boundCurrent.ValueChanged -= currentChanged;

                base.Dispose(isDisposing);
            }

            /// <summary>
            /// Dropdown equivalent of DrawableStatefulMenuItem's state content.
            /// The left-hand slot is permanently allocated; only the check's
            /// alpha changes.
            /// </summary>
            private partial class FlagContent : CompositeDrawable, IHasText
            {
                public readonly SpriteIcon StateIcon;

                private readonly SpriteText label;

                public LocalisableString Text
                {
                    get => label.Text;
                    set => label.Text = value;
                }

                public FlagContent()
                {
                    RelativeSizeAxes = Axes.X;
                    AutoSizeAxes = Axes.Y;

                    InternalChildren = new Drawable[]
                    {
                        StateIcon = new SpriteIcon
                        {
                            Icon = FontAwesome.Solid.Check,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Position = new Vector2(3, 0),
                            Size = new Vector2(10),
                            Alpha = 0,
                            AlwaysPresent = true,
                        },
                        label = new TruncatingSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Padding = new MarginPadding
                            {
                                Left = 20,
                                Right = 2,
                            },
                        },
                    };
                }
            }
        }

        private static TypeCode underlyingType =>
            Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T)));

        private static ulong toBits(T value)
        {
            return underlyingType switch
            {
                TypeCode.SByte =>
                    unchecked((byte)Convert.ToSByte(value)),

                TypeCode.Byte =>
                    Convert.ToByte(value),

                TypeCode.Int16 =>
                    unchecked((ushort)Convert.ToInt16(value)),

                TypeCode.UInt16 =>
                    Convert.ToUInt16(value),

                TypeCode.Int32 =>
                    unchecked((uint)Convert.ToInt32(value)),

                TypeCode.UInt32 =>
                    Convert.ToUInt32(value),

                TypeCode.Int64 =>
                    unchecked((ulong)Convert.ToInt64(value)),

                TypeCode.UInt64 =>
                    Convert.ToUInt64(value),

                _ => throw new InvalidOperationException(
                    $"Unsupported enum backing type for {typeof(T).Name}."),
            };
        }

        private static T fromBits(ulong value)
        {
            object raw = underlyingType switch
            {
                TypeCode.SByte =>
                    unchecked((sbyte)value),

                TypeCode.Byte =>
                    unchecked((byte)value),

                TypeCode.Int16 =>
                    unchecked((short)value),

                TypeCode.UInt16 =>
                    unchecked((ushort)value),

                TypeCode.Int32 =>
                    unchecked((int)value),

                TypeCode.UInt32 =>
                    unchecked((uint)value),

                TypeCode.Int64 =>
                    unchecked((long)value),

                TypeCode.UInt64 =>
                    value,

                _ => throw new InvalidOperationException(
                    $"Unsupported enum backing type for {typeof(T).Name}."),
            };

            return (T)Enum.ToObject(typeof(T), raw);
        }
    }
}
