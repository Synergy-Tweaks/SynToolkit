using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinUIEx;
using DrawingIcon = System.Drawing.Icon;

namespace SynToolkit.Services
{
    /// <summary>
    /// Owns SynToolkit's notification-area icon. The icon only remains visible
    /// while the user has explicitly enabled close-to-tray behavior.
    /// </summary>
    public sealed class SystemTrayService : IDisposable
    {
        private readonly Window _window;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action _exitApplication;
        private readonly Action _openAudioMixer;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly DrawingIcon _icon;
        private bool _disposed;

        public SystemTrayService(Window window, Action exitApplication, Action openAudioMixer)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _dispatcherQueue = window.DispatcherQueue;
            _exitApplication = exitApplication ?? throw new ArgumentNullException(nameof(exitApplication));
            _openAudioMixer = openAudioMixer ?? throw new ArgumentNullException(nameof(openAudioMixer));
            _icon = LoadIcon();

            _contextMenu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };

            _contextMenu.Items.Add("Open SynToolkit", null, (_, _) => RestoreWindow());
            _contextMenu.Items.Add("Open Audio Mixer", null, (_, _) => OpenAudioMixer());
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("Exit SynToolkit", null, (_, _) => RequestExit());

            _notifyIcon = new NotifyIcon
            {
                Text = "SynToolkit",
                Icon = _icon,
                ContextMenuStrip = _contextMenu,
                Visible = false
            };

            _notifyIcon.MouseClick += (_, eventArgs) =>
            {
                if (eventArgs.Button == MouseButtons.Left)
                {
                    RestoreWindow();
                }
            };
            _notifyIcon.MouseDoubleClick += (_, eventArgs) =>
            {
                if (eventArgs.Button == MouseButtons.Left)
                {
                    OpenAudioMixer();
                }
            };
        }

        public bool IsEnabled => !_disposed && _notifyIcon.Visible;

        public void SetEnabled(bool enabled)
        {
            if (_disposed)
            {
                return;
            }

            _notifyIcon.Visible = enabled;
        }

        public void RestoreWindow()
        {
            if (_disposed)
            {
                return;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                _window.Show();
                _window.Activate();
            });
        }

        public void OpenAudioMixer()
        {
            if (_disposed)
            {
                return;
            }

            _dispatcherQueue.TryEnqueue(() => _openAudioMixer());
        }

        private void RequestExit()
        {
            if (_disposed)
            {
                return;
            }

            _dispatcherQueue.TryEnqueue(() => _exitApplication());
        }

        private static DrawingIcon LoadIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "logo", "SynToolkit.ico");
            if (File.Exists(iconPath))
            {
                return new DrawingIcon(iconPath);
            }

            string executablePath = Environment.ProcessPath;
            DrawingIcon associatedIcon = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : DrawingIcon.ExtractAssociatedIcon(executablePath);

            return associatedIcon ?? new DrawingIcon(SystemIcons.Application, SystemIcons.Application.Size);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _icon.Dispose();
        }
    }
}
