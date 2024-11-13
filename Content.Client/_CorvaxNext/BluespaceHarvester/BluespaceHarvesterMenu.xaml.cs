using Content.Client.UserInterface.Controls;
using Content.Shared.BluespaceHarvester;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;

namespace Content.Client._CorvaxNext.BluespaceHarvester;

[GenerateTypedNameReferences]
public sealed partial class BluespaceHarvesterMenu : FancyWindow
{
    private readonly BluespaceHarvesterBoundUserInterface _owner;

    public BluespaceHarvesterMenu(BluespaceHarvesterBoundUserInterface owner)
    {
        RobustXamlLoader.Load(this);

        _owner = owner;

        InputLevelBar.OnTextEntered += (args) =>
        {
            if (!int.TryParse(args.Text, out var level) || level < 0 || level > 20)
            {
                InputLevelBar.Text = "0";
                return;
            }
            _owner.SendTargetLevel(level);
        };

        // EntityView.SetEntity(_owner.Owner);
    }

    public void UpdateState(BluespaceHarvesterBoundUserInterfaceState state)
    {
        TargetLevel.Text = $"{state.TargetLevel}";
        CurrentLevel.Text = $"{state.CurrentLevel}";
        DesiredBar.Value = ((float)state.CurrentLevel) / ((float)state.MaxLevel);

        PowerUsageLabel.Text = Loc.GetString("power-monitoring-window-value", ("value", state.PowerUsage));
        PowerUsageNextLabel.Text = Loc.GetString("power-monitoring-window-value", ("value", state.PowerUsageNext));
        PowerSuppliertLabel.Text = Loc.GetString("power-monitoring-window-value", ("value", state.PowerSuppliert));

        AvailablePointsLabel.Text = $"{state.Points}";
        TotalPontsLabel.Text = $"{state.TotalPoints}";
        GenerationPointsLabel.Text = $"{state.PointsGen}";

        Categories.RemoveAllChildren();
        foreach (var category in state.Categories)
        {
            var child = new BluespaceHarvesterCategory(category, state.Points >= category.Cost);

            child.CategoryButton.OnButtonDown += (args) =>
            {
                _owner.SendBuy(category.Type);
            };

            Categories.AddChild(child);
        }
    }
}
