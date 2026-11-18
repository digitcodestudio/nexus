using Microsoft.UI.Xaml;
using Nexus.Core.Contracts;

namespace Nexus.UI.Services;

public sealed class ThemeService(INexusRepository repository)
{
    private const string ThemeSettingKey = "ui.theme";
    private FrameworkElement? _rootElement;

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Dark;

    public void AttachRoot(FrameworkElement root)
    {
        _rootElement = root;
        _rootElement.RequestedTheme = CurrentTheme;
    }

    public async Task LoadSavedThemeAsync()
    {
        var saved = await repository.GetSettingAsync(ThemeSettingKey);
        if (Enum.TryParse<ElementTheme>(saved, out var parsed) && parsed != ElementTheme.Default)
        {
            CurrentTheme = parsed;
        }
        else
        {
            CurrentTheme = ElementTheme.Dark;
        }

        if (_rootElement is not null)
        {
            _rootElement.RequestedTheme = CurrentTheme;
        }
    }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        CurrentTheme = theme;
        if (_rootElement is not null)
        {
            _rootElement.RequestedTheme = theme;
        }

        await repository.SetSettingAsync(ThemeSettingKey, theme.ToString());
    }
}

