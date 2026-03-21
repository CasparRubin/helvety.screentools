using System;
using System.Drawing;
using System.IO;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace helvety.screentools.Services
{
    internal sealed class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _taskbarIcon;
        private readonly Icon? _trayIcon;

        public TrayIconService(Action openMainWindow, Action exitApplication)
        {
            var openCommand = new DelegateCommand(_ => openMainWindow());
            var exitCommand = new DelegateCommand(_ => exitApplication());
            var contextMenu = new MenuFlyout();

            var openMenuItem = new MenuFlyoutItem
            {
                Text = "Open Helvety Screen Tools",
                Command = openCommand
            };

            var separator = new MenuFlyoutSeparator();

            var exitMenuItem = new MenuFlyoutItem
            {
                Text = "Exit",
                Command = exitCommand
            };

            var trayIconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "TrayIcon.ico");
            if (File.Exists(trayIconPath))
            {
                _trayIcon = new Icon(trayIconPath);
            }

            contextMenu.Items.Add(openMenuItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(exitMenuItem);

            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "Helvety Screen Tools — capture & Live Draw",
                Icon = _trayIcon,
                ContextFlyout = contextMenu,
                LeftClickCommand = openCommand
            };

            _taskbarIcon.ForceCreate();
        }

        public void Dispose()
        {
            _taskbarIcon.Dispose();
            _trayIcon?.Dispose();
        }

        private sealed class DelegateCommand : ICommand
        {
            private readonly Action<object?> _execute;

            public DelegateCommand(Action<object?> execute)
            {
                _execute = execute;
            }

            public event EventHandler? CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object? parameter)
            {
                return true;
            }

            public void Execute(object? parameter)
            {
                _execute(parameter);
            }
        }
    }
}
