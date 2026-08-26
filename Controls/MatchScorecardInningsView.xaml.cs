using System.Collections;
using Microsoft.Maui.Controls;

namespace JPL.Controls;

public partial class MatchScorecardInningsView : ContentView
{
    public static readonly BindableProperty InningsProperty = BindableProperty.Create(
        nameof(Innings),
        typeof(IEnumerable),
        typeof(MatchScorecardInningsView));

    public IEnumerable? Innings
    {
        get => (IEnumerable?)GetValue(InningsProperty);
        set => SetValue(InningsProperty, value);
    }

    public MatchScorecardInningsView()
    {
        InitializeComponent();
    }
}
