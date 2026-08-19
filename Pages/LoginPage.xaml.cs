using JPL.ViewModels;

namespace JPL.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    void OnLoginScrollSizeChanged(object? sender, EventArgs e)
    {
        // Make host at least viewport height so VerticalOptions=Center works inside ScrollView.
        if (LoginScroll.Height > 0)
            ContentHost.MinimumHeightRequest = LoginScroll.Height;
    }
}
