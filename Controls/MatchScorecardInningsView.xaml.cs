using System.Windows.Input;
using System.Collections;
using Microsoft.Maui.Controls;

namespace JPL.Controls;

public partial class MatchScorecardInningsView : ContentView
{
    public static readonly BindableProperty InningsProperty = BindableProperty.Create(
        nameof(Innings),
        typeof(IEnumerable),
        typeof(MatchScorecardInningsView));

    public static readonly BindableProperty PlayerTappedCommandProperty = BindableProperty.Create(
        nameof(PlayerTappedCommand),
        typeof(ICommand),
        typeof(MatchScorecardInningsView));

    public IEnumerable? Innings
    {
        get => (IEnumerable?)GetValue(InningsProperty);
        set => SetValue(InningsProperty, value);
    }

    public ICommand? PlayerTappedCommand
    {
        get => (ICommand?)GetValue(PlayerTappedCommandProperty);
        set => SetValue(PlayerTappedCommandProperty, value);
    }

    public MatchScorecardInningsView()
    {
        InitializeComponent();
    }
}
