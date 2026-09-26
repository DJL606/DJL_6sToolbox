using System.IO;
using System.Reflection;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using System.Windows.Threading;
using DJL_6sToolbox.Desktop.Services;
using DJL_6sToolbox.Desktop.Views;

namespace DJL_6sToolbox.Desktop;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();
        _singleInstanceMutex = new Mutex(true, @"Local\DJL_6sToolbox.SingleInstance", out var isNew);
        _ownsMutex = isNew;
        if (!isNew)
        {
            ModernDialog.Show("DJL_6's 工具箱 已经在运行中。", "DJL_6's 工具箱", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _mainWindow = new MainWindow();
        _mainWindow.Closing += OnMainClosing;

        var silentStart = e.Args.Contains("--tray") || e.Args.Contains("--silent");
        if (!silentStart)
        {
            _mainWindow.Show();
        }

        _tray = new TrayService(
            () =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            },
            () =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _ = _mainWindow.ViewModel.CheckUpdateCommand.ExecuteAsync();
            },
            () =>
            {
                _mainWindow?.ViewModel.Shutdown();
                _tray?.Dispose();
                Shutdown();
            },
            () => _mainWindow.ViewModel.TrayStatusText,
            () => _mainWindow.ViewModel.StartAutoLike(),
            () => _mainWindow.ViewModel.StopAutoLike());

        _tray.Show();
    }

    private void OnMainClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = _mainWindow?.ViewModel.Settings;
        if (settings is { MinimizeToTray: true })
        {
            e.Cancel = true;
            _mainWindow?.Hide();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ModernDialog.Show($"发生未处理异常：{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "DJL_6's 工具箱 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
