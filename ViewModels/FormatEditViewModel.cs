using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class FormatEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly IFormatService _formats;
    private readonly IAuthService _auth;
    private Guid? _id;

    public ObservableCollection<SelectOption<MatchFormat>> TypeOptions { get; } = new();

    [ObservableProperty] private string pageTitle = "New format";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private SelectOption<MatchFormat>? selectedType;
    [ObservableProperty] private int oversLimit = 8;
    [ObservableProperty] private bool noOversLimit;
    [ObservableProperty] private bool oversAreMatchTotal = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string formatHint = string.Empty;

    public bool ShowOversFields => !NoOversLimit;
    public string OversFieldLabel => OversAreMatchTotal ? "Match overs *" : "Overs per innings *";
    public OversMode CurrentOversMode => NoOversLimit
        ? OversMode.Unlimited
        : OversAreMatchTotal
            ? OversMode.MatchTotal
            : OversMode.PerInnings;

    public FormatEditViewModel(IFormatService formats, IAuthService auth)
    {
        _formats = formats;
        _auth = auth;

        TypeOptions.Add(new SelectOption<MatchFormat>
        {
            Label = "Single inning (1 turn each)",
            Value = MatchFormat.SingleInning
        });
        TypeOptions.Add(new SelectOption<MatchFormat>
        {
            Label = "Double innings (2 turns each)",
            Value = MatchFormat.DoubleInnings
        });
        SelectedType = TypeOptions[0];
        UpdateHint();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = "New format";
        Name = string.Empty;
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    partial void OnSelectedTypeChanged(SelectOption<MatchFormat>? value) => UpdateHint();
    partial void OnOversLimitChanged(int value) => UpdateHint();
    partial void OnNoOversLimitChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOversFields));
        OnPropertyChanged(nameof(OversFieldLabel));
        UpdateHint();
    }
    partial void OnOversAreMatchTotalChanged(bool value)
    {
        OnPropertyChanged(nameof(OversFieldLabel));
        UpdateHint();
    }

    public async Task InitializeAsync()
    {
        if (!_auth.CanWrite)
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (_id is not Guid id)
        {
            UpdateHint();
            return;
        }

        try
        {
            IsBusy = true;
            PageTitle = "Edit format";
            var item = await _formats.GetAsync(id).ConfigureAwait(true)
                       ?? throw new InvalidOperationException("Format not found.");
            Name = item.Name;
            SelectedType = TypeOptions.FirstOrDefault(t => t.Value == item.Format) ?? TypeOptions[0];
            OversLimit = item.OversLimit > 0 ? item.OversLimit : 8;
            ApplyOversMode(item.OversMode);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (SelectedType is null)
                throw new InvalidOperationException("Select a match type.");
            OversRules.Validate(CurrentOversMode, OversLimit, bowlerMaxOvers: 0);

            if (_id is Guid id)
                await _formats.UpdateAsync(id, Name, SelectedType.Value, CurrentOversMode, OversLimit)
                    .ConfigureAwait(true);
            else
                await _formats.CreateAsync(Name, SelectedType.Value, CurrentOversMode, OversLimit)
                    .ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void UpdateHint()
    {
        var type = SelectedType?.Value ?? MatchFormat.SingleInning;
        FormatHint = FormatRules.Hint(type, CurrentOversMode, OversLimit);
    }

    private void ApplyOversMode(OversMode mode)
    {
        NoOversLimit = mode == OversMode.Unlimited;
        OversAreMatchTotal = mode != OversMode.PerInnings;
    }
}
