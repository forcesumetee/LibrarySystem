using CommunityToolkit.Mvvm.ComponentModel;

namespace LibraryKiosk.ViewModels;

/// <summary>One category chip. Selection is driven explicitly by the VM (not by a
/// ListBox), so rebuilding the category list never disturbs the active filter.</summary>
public partial class CategoryChipViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    public CategoryChipViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }
}
