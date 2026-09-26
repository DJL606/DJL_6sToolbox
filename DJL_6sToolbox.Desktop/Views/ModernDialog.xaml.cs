using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using System.Windows;
using System.Windows.Media;

namespace DJL_6sToolbox.Desktop.Views;

public partial class ModernDialog : Window
{
    private ModernDialog(string title, string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        (IconText.Text, IconText.Foreground) = icon switch
        {
            MessageBoxImage.Error => ("✕", new SolidColorBrush(Color.FromRgb(244, 67, 54))),
            MessageBoxImage.Warning => ("⚠", new SolidColorBrush(Color.FromRgb(255, 176, 32))),
            MessageBoxImage.Question => ("？", new SolidColorBrush(Color.FromRgb(35, 173, 229))),
            _ => ("ℹ", new SolidColorBrush(Color.FromRgb(35, 173, 229)))
        };

        YesButton.Visibility = buttons is MessageBoxButton.OK or MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel
            ? Visibility.Visible : Visibility.Collapsed;
        NoButton.Visibility = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
            ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel
            ? Visibility.Visible : Visibility.Collapsed;
        YesButton.Content = buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel ? "确定" : "是";
    }

    public static MessageBoxResult Show(Window? owner, string title, string message,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
    {
        var dialog = new ModernDialog(title, message, buttons, icon)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog._result;
    }

    public static MessageBoxResult Show(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
    {
        return Show(Application.Current?.MainWindow, title, message, buttons, icon);
    }

    private MessageBoxResult _result = MessageBoxResult.None;

    private void Yes_OnClick(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Yes;
        DialogResult = true;
    }

    private void No_OnClick(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.No;
        DialogResult = false;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        DialogResult = false;
    }
}
