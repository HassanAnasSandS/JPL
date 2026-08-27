using JPL.ViewModels;

namespace JPL.Pages;

public partial class ModeChooserPage : ContentPage
{
    public ModeChooserPage(ModeChooserViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
