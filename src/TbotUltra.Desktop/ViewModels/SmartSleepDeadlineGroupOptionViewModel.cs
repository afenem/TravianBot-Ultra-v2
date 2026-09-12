using TbotUltra.Desktop.Common;

namespace TbotUltra.Desktop.ViewModels;

public sealed class SmartSleepDeadlineGroupOptionViewModel(
    string groupKey,
    string title) : BaseViewModel
{
    private bool _isSelected = true;

    public string GroupKey { get; } = groupKey;
    public string Title { get; } = title;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
