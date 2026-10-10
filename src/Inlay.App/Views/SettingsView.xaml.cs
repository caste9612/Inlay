using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Inlay.App.Services;
using Inlay.App.ViewModels;

namespace Inlay.App.Views;

/// <summary>The settings: the pages on the left, the chosen one on the right. The first time, only the archives.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    /// <summary>Where the keyboard starts: in the archives when they are shown, otherwise on the list of pages.</summary>
    public void FocusStart()
    {
        if (ViewModel.Section == SettingsSection.Archives)
            ArchivesSection.FocusStart();
        else
            SectionList.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();

    /// <summary>The releases on GitHub, with what each version brings, in the browser.</summary>
    private void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellLauncher.Open(UpdatesViewModel.ReleasesPage);
        }
        catch (Win32Exception exception)
        {
            Log.Write($"Could not open {UpdatesViewModel.ReleasesPage}: {exception.Message}");
        }
    }

    /// <summary>A combination was pressed: it is tried at once, and the field shows the one in use afterwards.</summary>
    private void HotkeyField_ShortcutCaptured(object? sender, string shortcut)
    {
        if (ViewModel.UseHotkey(shortcut) is not null)
            HotkeyField.Text = ViewModel.Hotkey;
    }
}

/// <summary>Shows a page only when it is the chosen section: ConverterParameter is the name of the section.</summary>
public sealed class SectionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SettingsSection section && parameter is string name && section.ToString() == name
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
