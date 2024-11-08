﻿using System.Linq;
using Content.Client.UserInterface.Screens;
using Content.Shared._CorvaxNext.NextVars;
using Content.Shared.CCVar;
using Content.Shared.HUD;
using Robust.Client.AutoGenerated;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.XAML;
using Robust.Shared;
using Robust.Shared.Prototypes;

namespace Content.Client.Options.UI.Tabs;

[GenerateTypedNameReferences]
public sealed partial class MiscTab : Control
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public MiscTab()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        var themes = _prototypeManager.EnumeratePrototypes<HudThemePrototype>().ToList();
        themes.Sort();
        var themeEntries = new List<OptionDropDownCVar<string>.ValueOption>();
        foreach (var gear in themes)
        {
            themeEntries.Add(new OptionDropDownCVar<string>.ValueOption(gear.ID, Loc.GetString(gear.Name)));
        }

        var layoutEntries = new List<OptionDropDownCVar<string>.ValueOption>();
        foreach (var layout in Enum.GetValues(typeof(ScreenType)))
        {
            layoutEntries.Add(new OptionDropDownCVar<string>.ValueOption(layout.ToString()!, Loc.GetString($"ui-options-hud-layout-{layout.ToString()!.ToLower()}")));
        }

        // Channel can be null in replays so.
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        ShowOocPatronColor.Visible = _playerManager.LocalSession?.Channel?.UserData.PatronTier is { };

        Control.AddOptionDropDown(CVars.InterfaceTheme, DropDownHudTheme, themeEntries);
        Control.AddOptionDropDown(CCVars.UILayout, DropDownHudLayout, layoutEntries);

        Control.AddOptionCheckBox(CVars.DiscordEnabled, DiscordRich);
        Control.AddOptionCheckBox(CCVars.ShowOocPatronColor, ShowOocPatronColor);
        Control.AddOptionCheckBox(CCVars.LoocAboveHeadShow, ShowLoocAboveHeadCheckBox);
        Control.AddOptionCheckBox(CCVars.HudHeldItemShow, ShowHeldItemCheckBox);
        Control.AddOptionCheckBox(CCVars.CombatModeIndicatorsPointShow, ShowCombatModeIndicatorsCheckBox);
        Control.AddOptionCheckBox(CCVars.OpaqueStorageWindow, OpaqueStorageWindowCheckBox);
        Control.AddOptionCheckBox(CCVars.ChatEnableFancyBubbles, FancySpeechBubblesCheckBox);
        Control.AddOptionCheckBox(CCVars.ChatFancyNameBackground, FancyNameBackgroundsCheckBox);
        Control.AddOptionCheckBox(CCVars.StaticStorageUI, StaticStorageUI);
        Control.AddOptionCheckBox(NextVars.OfferModeIndicatorsPointShow, ShowOfferModeIndicatorsCheckBox); // Corvax-Next-Offer
        Control.Initialize();
    }
}
