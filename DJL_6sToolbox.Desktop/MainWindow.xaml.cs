using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DJL_6sToolbox.Desktop.Services;
using DJL_6sToolbox.Desktop.ViewModels;

namespace DJL_6sToolbox.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _ = CaptchaWebView.EnsureCoreWebView2Async();
        AcrylicHelper.EnableAcrylic(this);
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Initialize();
        UpdatePageVisibility();
    }

    public MainViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedNav))
        {
            UpdatePageVisibility();
        }
    }

    private void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo)
        {
            return;
        }

        var over = e.OriginalSource as DependencyObject;
        if (combo.IsDropDownOpen && over != null && !IsDescendantOf(combo, over))
        {
            return;
        }

        e.Handled = true;
        var scrollViewer = FindParentScrollViewer(combo);
        scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
    }
    private void OnComboBoxDropDownOpened(object sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo)
        {
            return;
        }

        if (combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup popup)
        {
            popup.PlacementTarget = combo;
            popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }
    }


    private void MainScroll_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        foreach (var combo in FindVisualChildren<System.Windows.Controls.ComboBox>(this))
        {
            combo.IsDropDownOpen = false;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var sub in FindVisualChildren<T>(child))
            {
                yield return sub;
            }
        }
    }

    private static bool IsDescendantOf(DependencyObject root, DependencyObject? child)
    {
        while (child != null)
        {
            if (ReferenceEquals(child, root))
            {
                return true;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return false;
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }


    private void UpdatePageVisibility()
    {
        var title = ViewModel.SelectedNav?.Title;

        DashboardPage.Visibility = title == "总览" ? Visibility.Visible : Visibility.Collapsed;
        AutoLikePage.Visibility = title == "自动点赞" ? Visibility.Visible : Visibility.Collapsed;
        SearchPage.Visibility = title == "原版功能" ? Visibility.Visible : Visibility.Collapsed;
        CookiePage.Visibility = title == "Cookie" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = title == "关于更新" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
    }
}
