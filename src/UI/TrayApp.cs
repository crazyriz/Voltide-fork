
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Voltide.Core;

namespace Voltide.UI
{
    public class TrayApp : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly DeviceManager _manager;
        private readonly NotifyIcon _tray;
        private readonly MainForm _form;
        private readonly System.Windows.Forms.Timer _timer;

        // Refresh queue synchronization
        private readonly object _refreshSync = new object();
        private bool _refreshing;
        private bool _refreshQueued;

        // Tray menu
        private volatile bool _showMenuAfterRefresh;
        private TrayMenuForm _menuForm;

        internal static readonly Color MenuMuted =
            Color.FromArgb(135, 135, 135);

        public TrayApp()
        {
            _manager = new DeviceManager();

            _form = new MainForm(_manager);

            _form.FormClosing += delegate(
                object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    _form.Hide();
                }
            };

            _form.RefreshRequested += delegate
            {
                RefreshNow();
            };

            // Force handle creation for cross-thread marshalling.
            IntPtr handle = _form.Handle;

            _tray = new NotifyIcon();
            _tray.Icon = IconFactory.ToIcon(32);
            _tray.Text = "Voltide";
            _tray.Visible = true;

            _tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _form.ShowPanel();
                    return;
                }

                if (e.Button == MouseButtons.Right)
                {
                    ShowMenuAboveRight();
                }
            };

            _form.RefreshSeconds = LoadRefreshSeconds();

            _form.RefreshRateChanged += delegate
            {
                SaveRefreshSeconds(_form.RefreshSeconds);
                ApplyInterval();
            };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = _form.RefreshSeconds * 1000;

            _timer.Tick += delegate
            {
                ApplyInterval();
                RefreshNow();
            };

            _timer.Start();

            // Initial refresh
            RefreshNow();
        }

        // -------------------------------------------------------
        // Refresh interval
        // -------------------------------------------------------

        private void ApplyInterval()
        {
            int seconds = _form.Visible
                ? _form.RefreshSeconds
                : 300;

            if (_timer.Interval != seconds * 1000)
            {
                _timer.Interval = seconds * 1000;
            }
        }

        // -------------------------------------------------------
        // Settings
        // -------------------------------------------------------

        private const string SettingsKey = @"Software\Voltide";

        private static int LoadRefreshSeconds()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        SettingsKey, false))
                {
                    if (key == null)
                        return 90;

                    object raw = key.GetValue("RefreshSeconds");

                    if (raw == null)
                        return 90;

                    int seconds = Convert.ToInt32(raw);

                    return (seconds == 15 ||
                            seconds == 30 ||
                            seconds == 90)
                        ? seconds
                        : 90;
                }
            }
            catch
            {
                return 30;
            }
        }

        private static void SaveRefreshSeconds(int seconds)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                        SettingsKey))
                {
                    if (key != null)
                    {
                        key.SetValue("RefreshSeconds", seconds);
                    }
                }
            }
            catch
            {
            }
        }

        // -------------------------------------------------------
        // Tray menu
        // -------------------------------------------------------

        /// <summary>
        /// Show the current menu immediately.
        /// Refresh device data in the background.
        /// </summary>
        private void ShowMenuAboveRight()
        {
            // Display current snapshot immediately.
            ShowMenuAboveRightCore();

            // Request menu update after all pending refreshes finish.
            _showMenuAfterRefresh = true;

            RefreshNow();
        }

        private void ShowMenuAboveRightCore()
        {
            // Close previous popup.
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                try
                {
                    _menuForm.Close();
                }
                catch
                {
                }

                _menuForm = null;
            }

            MenuPanel panel = BuildMenu();

            if (panel == null)
                return;

            TrayMenuForm form = new TrayMenuForm();

            form.Panel = panel;
            form.ClientSize = panel.Size;

            panel.Dock = DockStyle.Fill;

            panel.QuitRequested += delegate
            {
                Application.Exit();
            };

            panel.AutostartEnabled = Autostart.IsSupported();

            panel.AutostartOn =
                Autostart.IsSupported() &&
                Autostart.IsEnabled();

            MenuPanel captured = panel;

            panel.AutostartToggled += delegate
            {
                Autostart.Set(captured.AutostartOn);
            };

            form.Controls.Add(panel);

            Point p = Cursor.Position;

            Rectangle screen =
                Screen.FromPoint(p).WorkingArea;

            int x = p.X + 4;
            int y = p.Y - panel.Height - 4;

            if (x + panel.Width > screen.Right)
            {
                x = p.X - panel.Width - 4;
            }

            if (y < screen.Top)
            {
                y = p.Y + 4;
            }

            form.Location = new Point(x, y);

            _menuForm = form;

            form.Show();
            form.Activate();

            panel.Focus();
        }

        // -------------------------------------------------------
        // Build menu
        // -------------------------------------------------------

        private MenuPanel BuildMenu()
{
    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
    {
        float scale = g.DpiX / 96f;

        Font nameFont = FontManager.Get(
            FontKind.MontserratMedium, 20f * scale);

        Font valueFont = FontManager.Get(
            FontKind.MontserratMedium, 20f * scale);

        Font cnFont = FontManager.Get(
            FontKind.Chinese, 17f * scale);

        Font markFont = FontManager.Get(
            FontKind.MontserratSemiBold, 9f * scale);

        return new MenuPanel(
            BuildMenuRows(),
            scale,
            nameFont,
            valueFont,
            cnFont,
            markFont);
    }
}

private List<MenuRow> BuildMenuRows()
{
    List<MenuRow> rows = new List<MenuRow>();

    foreach (DeviceSnapshot s in OrderedForMenu())
    {
        MenuRow row = new MenuRow();

        if (!string.IsNullOrEmpty(s.Info.ShortModel))
            row.Name = s.Info.ShortModel;
        else if (!string.IsNullOrEmpty(s.Info.Model))
            row.Name = s.Info.Model;
        else
            row.Name = s.Info.Brand;

        if (HasLevel(s))
        {
            row.Kind = MenuRowKind.Level;
            row.Percent = s.Reading.Percent.Value;
            row.Value = s.Reading.Percent.Value + "%";
        }
        else if (s.Reading != null && s.Reading.Online)
        {
            row.Kind = MenuRowKind.OnlineUnknown;
            row.Value = "已連線";
        }
        else
        {
            row.Kind = MenuRowKind.Asleep;

            int? last = _form.LastPercent(s.Info.Id);

            if (last.HasValue)
            {
                row.Percent = last.Value;
                row.Value = last.Value >= 100
                    ? "Full"
                    : last.Value + "%";

                row.GreyValue = true;
            }
            else
            {
                row.Value = "休眠中";
            }
        }

        rows.Add(row);
    }

    return rows;
}

        private List<DeviceSnapshot> OrderedForMenu()
        {
            List<DeviceSnapshot> live =
                new List<DeviceSnapshot>();

            List<DeviceSnapshot> asleep =
                new List<DeviceSnapshot>();

            foreach (DeviceSnapshot s in _manager.Snapshot())
            {
                if (s.Reading == null)
                {
                    asleep.Add(s);
                    continue;
                }

                if (s.Reading.Online)
                {
                    live.Add(s);
                }
                else
                {
                    asleep.Add(s);
                }
            }

            live.AddRange(asleep);

            return live;
        }

        private static bool HasLevel(DeviceSnapshot s)
        {
            return s != null &&
                   s.Reading != null &&
                   s.Reading.Online &&
                   s.Reading.Percent.HasValue;
        }

        // -------------------------------------------------------
        // Refresh queue
        // -------------------------------------------------------

        /// <summary>
        /// Only one background refresh runs at a time.
        /// Additional requests are queued and processed sequentially.
        /// </summary>
        private void RefreshNow()
        {
            lock (_refreshSync)
            {
                if (_refreshing)
                {
                    _refreshQueued = true;
                    return;
                }

                _refreshing = true;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    // Refresh device data.
                    try
                    {
                        _manager.Refresh();
                    }
                    catch
                    {
                    }

                    // Check whether another refresh was requested.
                    lock (_refreshSync)
                    {
                        if (_refreshQueued)
                        {
                            _refreshQueued = false;
                            continue;
                        }

                        _refreshing = false;
                        break;
                    }
                }

                // Update UI after all queued refreshes have finished.
try
{
    _form.BeginInvoke(new MethodInvoker(delegate
    {
        _form.UpdateSnapshot(_manager.Snapshot());

        if (_showMenuAfterRefresh)
        {
            _showMenuAfterRefresh = false;

            if (_menuForm != null &&
                !_menuForm.IsDisposed &&
                _menuForm.Visible &&
                _menuForm.Panel != null &&
                !_menuForm.Panel.IsDisposed)
            {
                MenuPanel panel = _menuForm.Panel;

                int oldHeight = panel.Height;

                panel.UpdateRows(BuildMenuRows());

                int newHeight = panel.Height;

                _menuForm.ClientSize = panel.Size;

                _menuForm.Location = new Point(
                    _menuForm.Left,
                    _menuForm.Top - (newHeight - oldHeight));
            }
        }
    }));
}
catch
{
}
            });
        }

        // -------------------------------------------------------
        // Main panel
        // -------------------------------------------------------

        public void ShowPanel()
        {
            _form.ShowPanel();
        }

        // -------------------------------------------------------
        // Dispose
        // -------------------------------------------------------

        public void Dispose()
        {
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                try
                {
                    _menuForm.Close();
                }
                catch
                {
                }

                _menuForm = null;
            }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
        }
    }
}
