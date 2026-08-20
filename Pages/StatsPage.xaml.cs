using JPL.ViewModels;
using Microsoft.Maui.Controls;

namespace JPL.Pages;

public partial class StatsPage : ContentPage
{
    private readonly StatsViewModel _vm;
    public StatsPage(StatsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }
}

public sealed class StatsTabTemplateSelector : DataTemplateSelector
{
    public DataTemplate Match { get; set; } = null!;
    public DataTemplate Tournament { get; set; } = null!;
    public DataTemplate Series { get; set; } = null!;
    public DataTemplate Batting { get; set; } = null!;
    public DataTemplate NetsBatting { get; set; } = null!;
    public DataTemplate Bowling { get; set; } = null!;
    public DataTemplate Fielding { get; set; } = null!;
    public DataTemplate Captaincy { get; set; } = null!;
    public DataTemplate Attendance { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        var vm = container.BindingContext as StatsViewModel;
        if (vm is null && container is Element el)
        {
            for (var p = el.Parent; p is not null; p = p.Parent)
            {
                if (p.BindingContext is StatsViewModel found)
                {
                    vm = found;
                    break;
                }
            }
        }

        return (vm?.SelectedCategory ?? 0) switch
        {
            1 => Tournament,
            2 => Series,
            3 => vm?.IsNetsSource == true ? NetsBatting : Batting,
            4 => Bowling,
            5 => Fielding,
            6 => Captaincy,
            7 => Attendance,
            _ => Match
        };
    }
}
