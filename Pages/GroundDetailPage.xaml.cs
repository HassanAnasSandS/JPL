using System.ComponentModel;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class GroundDetailPage : ContentPage
{
    private readonly GroundDetailViewModel _vm;
    public GroundDetailPage(GroundDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(_vm.LoadAsync);
    }

    protected override void OnDisappearing()
    {
        if (_vm.IsViewerOpen)
            _vm.CloseViewerCommand.Execute(null);
        Shell.SetNavBarIsVisible(this, true);
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_vm.IsViewerOpen)
        {
            _vm.CloseViewerCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    void OnZoomInClicked(object? sender, EventArgs e) => ViewerImage.ZoomIn();

    void OnZoomOutClicked(object? sender, EventArgs e) => ViewerImage.ZoomOut();

    void OnZoomResetClicked(object? sender, EventArgs e) => ViewerImage.ResetZoom();

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroundDetailViewModel.IsViewerOpen))
        {
            Shell.SetNavBarIsVisible(this, !_vm.IsViewerOpen);
            if (_vm.IsViewerOpen)
                ViewerImage.ResetZoom();
            return;
        }

        if (e.PropertyName == nameof(GroundDetailViewModel.CurrentImageUrl) && _vm.IsViewerOpen)
            ViewerImage.ResetZoom();
    }
}
