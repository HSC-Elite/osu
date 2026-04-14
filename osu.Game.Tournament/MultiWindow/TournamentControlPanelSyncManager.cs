// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.TypeExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Skinning;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.MultiWindow
{
    [Cached]
    public partial class TournamentControlPanelSyncManager : Component
    {
        private readonly Dictionary<string, Action<string, string?>> operationHandlers = new Dictionary<string, Action<string, string?>>();
        private readonly List<Action> unbindActions = new List<Action>();

        private int keyCounter;

        [Resolved]
        private TournamentSceneManager sceneManager { get; set; } = null!;

        [Resolved]
        private TournamentStageState stageState { get; set; } = null!;

        [Resolved]
        private TournamentCompanionManager? companionManager { get; set; }

        [Resolved]
        private TournamentGameBase tournamentGame { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            sceneManager.CurrentScreen.BindValueChanged(_ => queueRebuild(), true);

            if (companionManager != null)
                companionManager.ActionMessageReceived += handleAction;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            foreach (var unbind in unbindActions)
                unbind();

            if (companionManager != null)
                companionManager.ActionMessageReceived -= handleAction;
        }

        private void handleAction(TournamentWindowActionMessage message)
        {
            if (message.ActionType != TournamentWindowActionType.ControlOperation || tournamentGame.WindowRole != TournamentWindowRole.Primary)
                return;

            if (message.ControlKey == null || message.ControlOperation == null)
                return;

            if (operationHandlers.TryGetValue(message.ControlKey, out var handler))
                handler(message.ControlOperation, message.JsonValue);
        }

        private void queueRebuild()
        {
            if (tournamentGame.WindowRole != TournamentWindowRole.Primary)
                return;

            Scheduler.AddOnce(rebuildLayout);
        }

        private void rebuildLayout()
        {
            foreach (var unbind in unbindActions)
                unbind();

            unbindActions.Clear();
            operationHandlers.Clear();
            keyCounter = 0;

            var currentScreen = sceneManager.ActiveScreen;
            var controlPanel = currentScreen?.ChildrenOfType<ControlPanel>().FirstOrDefault();

            if (controlPanel == null)
            {
                stageState.ControlPanelLayoutJson.Value = string.Empty;
                return;
            }

            var serialised = new List<SerialisedDrawableInfo>();

            foreach (var item in controlPanel.PanelItems)
            {
                var proxy = createProxy(item);

                if (proxy != null)
                    serialised.Add(proxy.CreateSerialisedInfo());
            }

            string json = JsonConvert.SerializeObject(serialised);

            if (stageState.ControlPanelLayoutJson.Value != json)
                stageState.ControlPanelLayoutJson.Value = json;
        }

        private Drawable? createProxy(Drawable source)
        {
            switch (source)
            {
                case ControlPanel.Spacer spacer:
                    return new TournamentSynchronisedSpacer
                    {
                        HeightBindable = { Value = spacer.Height },
                    };

                case TextFlowContainer:
                    return null;

                case SpriteText spriteText when source is not IHasCurrentValue<string>:
                    return createTextDisplay(spriteText.Text.ToString(), spriteText.Font.Size);
            }

            if (source is TournamentSpriteText tournamentText)
            {
                return createBoundTextDisplay(tournamentText.Current, tournamentText.Font.Size);
            }

            if (source is ClickableContainer clickable && source is not IHasCurrentValue<bool>)
                return createButtonProxy(source, clickable);

            if (tryGetCurrent(source, out IBindable? current, out Type valueType))
            {
                if (isSettingsDropdown(source) || hasProperty(source, "Items"))
                    return createDropdownProxy(source, current, valueType);

                if (valueType == typeof(int?))
                    return createNullableIntTextBoxProxy(source, current);

                if (isNumericType(valueType))
                    return createSliderProxy(source, current, valueType);

                if (valueType == typeof(bool))
                    return createCheckboxProxy(source, current);

                if (valueType == typeof(string) && source.GetType().Name.Contains("TextBox", StringComparison.OrdinalIgnoreCase))
                    return createTextBoxProxy(source, current);

                if (valueType == typeof(string))
                    return createBoundTextDisplay((IBindable<string>)current, 14);
            }

            return null;
        }

        private Drawable createButtonProxy(Drawable source, ClickableContainer clickable)
        {
            string key = nextKey("button");

            operationHandlers[key] = (operation, _) =>
            {
                if (operation == "invoke" && clickable.Enabled.Value)
                    clickable.Action?.Invoke();
            };

            Action<ValueChangedEvent<bool>> handler = v => broadcastControlState(key, "enabled", v.NewValue);
            clickable.Enabled.ValueChanged += handler;
            unbindActions.Add(() => clickable.Enabled.ValueChanged -= handler);

            return new TournamentSynchronisedButton
            {
                KeyBindable = { Value = key },
                EnabledBindable = { Value = clickable.Enabled.Value },
                TextBindable = { Value = getLabel(source) ?? getText(source) ?? source.GetType().ReadableName() },
            };
        }

        private Drawable createCheckboxProxy(Drawable source, IBindable current)
        {
            string key = nextKey("bool");
            var bindable = (Bindable<bool>)current;

            operationHandlers[key] = (operation, jsonValue) =>
            {
                if (operation == "set" && jsonValue != null)
                    bindable.Value = JsonConvert.DeserializeObject<bool>(jsonValue);
            };

            subscribeCurrent(key, bindable, "current");
            subscribeDisabled(key, bindable);

            return new TournamentSynchronisedCheckbox
            {
                KeyBindable = { Value = key },
                LabelBindable = { Value = getLabel(source) ?? key },
                EnabledBindable = { Value = !bindable.Disabled },
                CurrentBindable = { Value = bindable.Value },
            };
        }

        private Drawable createTextBoxProxy(Drawable source, IBindable current)
        {
            string key = nextKey("text");
            var bindable = (Bindable<string>)current;

            operationHandlers[key] = (operation, jsonValue) =>
            {
                if (operation == "set" && jsonValue != null)
                    bindable.Value = JsonConvert.DeserializeObject<string>(jsonValue) ?? string.Empty;
            };

            subscribeCurrent(key, bindable, "current");
            subscribeDisabled(key, bindable);

            return new TournamentSynchronisedTextBox
            {
                KeyBindable = { Value = key },
                LabelBindable = { Value = getLabel(source) ?? key },
                EnabledBindable = { Value = !bindable.Disabled },
                CurrentBindable = { Value = bindable.Value },
            };
        }

        private Drawable createNullableIntTextBoxProxy(Drawable source, IBindable current)
        {
            string key = nextKey("number");
            var bindable = (Bindable<int?>)current;

            operationHandlers[key] = (operation, jsonValue) =>
            {
                if (operation == "set" && jsonValue != null)
                    bindable.Value = JsonConvert.DeserializeObject<int?>(jsonValue);
            };

            subscribeCurrent(key, bindable, "current");
            subscribeDisabled(key, bindable);

            return new TournamentSynchronisedNullableIntTextBox
            {
                KeyBindable = { Value = key },
                LabelBindable = { Value = getLabel(source) ?? key },
                EnabledBindable = { Value = !bindable.Disabled },
                CurrentBindable = { Value = bindable.Value },
            };
        }

        private Drawable createDropdownProxy(Drawable source, IBindable current, Type valueType)
        {
            string key = nextKey("dropdown");
            object[] items = getItems(source, valueType);

            return (Drawable)typeof(TournamentControlPanelSyncManager)
                             .GetMethod(nameof(createTypedDropdownProxy), BindingFlags.NonPublic | BindingFlags.Instance)!
                             .MakeGenericMethod(valueType)
                             .Invoke(this, new object[] { source, current, key, items })!;
        }

        private Drawable createTypedDropdownProxy<T>(Drawable source, IBindable current, string key, object[] items)
        {
            var bindable = (Bindable<T>)current;

            operationHandlers[key] = (operation, jsonValue) =>
            {
                if (operation == "set" && jsonValue != null)
                    bindable.Value = JsonConvert.DeserializeObject<T>(jsonValue)!;
            };

            subscribeCurrent(key, bindable, "current");
            subscribeDisabled(key, bindable);

            return new TournamentSynchronisedDropdown<T>
            {
                KeyBindable = { Value = key },
                LabelBindable = { Value = getLabel(source) ?? key },
                EnabledBindable = { Value = !bindable.Disabled },
                CurrentBindable = { Value = bindable.Value },
                ItemsJsonBindable = { Value = JsonConvert.SerializeObject(items.Cast<T>().ToArray()) },
            };
        }

        private Drawable createSliderProxy(Drawable source, IBindable current, Type valueType)
        {
            string key = nextKey("slider");

            return (Drawable)typeof(TournamentControlPanelSyncManager)
                             .GetMethod(nameof(createTypedSliderProxy), BindingFlags.NonPublic | BindingFlags.Instance)!
                             .MakeGenericMethod(valueType)
                             .Invoke(this, new object[] { source, current, key })!;
        }

        private Drawable createTypedSliderProxy<T>(Drawable source, IBindable current, string key)
            where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
        {
            var bindable = (BindableNumber<T>)current;

            operationHandlers[key] = (operation, jsonValue) =>
            {
                if (operation == "set" && jsonValue != null)
                    bindable.Value = JsonConvert.DeserializeObject<T>(jsonValue);
            };

            TournamentSynchronisedSlider<T> slider = typeof(T) == typeof(int)
                ? (TournamentSynchronisedSlider<T>)(object)new TournamentSynchronisedIntSlider()
                : (TournamentSynchronisedSlider<T>)(object)new TournamentSynchronisedDoubleSlider();

            subscribeCurrent(key, bindable, "current");
            subscribeDisabled(key, bindable);

            slider.KeyBindable.Value = key;
            slider.LabelBindable.Value = getLabel(source) ?? key;
            slider.EnabledBindable.Value = !bindable.Disabled;
            slider.CurrentBindable.Value = bindable.Value;
            slider.MinimumBindable.Value = bindable.MinValue;
            slider.MaximumBindable.Value = bindable.MaxValue;
            return slider;
        }

        private Drawable createTextDisplay(string text, float fontSize, bool emphasis = false)
            => new TournamentSynchronisedTextDisplay
            {
                TextBindable = { Value = text },
                FontSizeBindable = { Value = fontSize },
                EmphasisBindable = { Value = emphasis },
            };

        private Drawable createBoundTextDisplay(IBindable<string> current, float fontSize, bool emphasis = false)
        {
            string key = nextKey("display");
            subscribeCurrent(key, current, "text");

            return new TournamentSynchronisedTextDisplay
            {
                KeyBindable = { Value = key },
                TextBindable = { Value = current.Value ?? string.Empty },
                FontSizeBindable = { Value = fontSize },
                EmphasisBindable = { Value = emphasis },
            };
        }

        private void subscribeCurrent<T>(string key, IBindable<T> bindable, string property)
        {
            Action<ValueChangedEvent<T>> handler = v => broadcastControlState(key, property, v.NewValue);
            bindable.ValueChanged += handler;
            unbindActions.Add(() => bindable.ValueChanged -= handler);
        }

        private void subscribeDisabled(string key, IBindable current)
        {
            Action<bool> handler = v => broadcastControlState(key, "enabled", !v);
            current.DisabledChanged += handler;
            unbindActions.Add(() => current.DisabledChanged -= handler);
        }

        private void broadcastControlState(string key, string property, object? value)
        {
            companionManager?.BroadcastControlState(key, property, value);
        }

        private static bool tryGetCurrent(Drawable source, [NotNullWhen(true)] out IBindable? current, out Type valueType)
        {
            var currentInterface = source.GetType().GetInterfaces()
                                         .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasCurrentValue<>));

            if (currentInterface == null)
            {
                current = null;
                valueType = typeof(void);
                return false;
            }

            valueType = currentInterface.GetGenericArguments()[0];
            current = (IBindable?)source.GetType().GetProperty(nameof(IHasCurrentValue<int>.Current))?.GetValue(source);
            return current != null;
        }

        private static string? getLabel(Drawable source) => source.GetType().GetProperty("LabelText")?.GetValue(source)?.ToString();

        private static string? getText(Drawable source) => source.GetType().GetProperty("Text")?.GetValue(source)?.ToString();

        private static bool hasProperty(Drawable source, string name) => source.GetType().GetProperty(name) != null;

        private static bool isSettingsDropdown(Drawable source)
        {
            for (Type? type = source.GetType(); type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SettingsDropdown<>))
                    return true;
            }

            return false;
        }

        private static object[] getItems(Drawable source, Type valueType)
        {
            object? items = source.GetType().GetProperty("Items")?.GetValue(source);

            if (items is System.Collections.IEnumerable enumerable)
                return enumerable.Cast<object>().ToArray();

            Type enumType = Nullable.GetUnderlyingType(valueType) ?? valueType;

            if (enumType.IsEnum)
                return Enum.GetValues(enumType).Cast<object>().ToArray();

            return Array.Empty<object>();
        }

        private static bool isNumericType(Type valueType)
        {
            Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
            return type == typeof(int) || type == typeof(double);
        }

        private string nextKey(string prefix)
            => $"{sceneManager.CurrentScreen.Value?.Name.ToLowerInvariant() ?? "control"}:{prefix}:{keyCounter++}";
    }
}
