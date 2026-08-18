using System;
using System.Reflection;
using Forms = System.Windows.Forms;

namespace BatteryPulse
{
    /// <summary>
    /// Windows notification-area entry point for users who need to find or
    /// close the background top bar without opening Task Manager.
    /// </summary>
    public sealed class TopBarTrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon notifyIcon;
        private readonly Forms.ContextMenuStrip menu;
        private readonly System.Drawing.Icon icon;
        private readonly Action openAdvanced;
        private DateTime lastOpenRequestUtc = DateTime.MinValue;
        private bool disposed;

        public TopBarTrayIcon(Action showTopBar, Action openAdvanced, Action exit)
        {
            this.openAdvanced = openAdvanced;
            icon = LoadApplicationIcon();
            menu = new Forms.ContextMenuStrip();

            Forms.ToolStripMenuItem show = new Forms.ToolStripMenuItem("顯示頂端狀態列");
            show.Click += delegate
            {
                if (showTopBar != null) showTopBar();
            };
            menu.Items.Add(show);

            Forms.ToolStripMenuItem advanced = new Forms.ToolStripMenuItem("開啟進階儀表板");
            advanced.Click += delegate
            {
                if (openAdvanced != null) openAdvanced();
            };
            menu.Items.Add(advanced);

            menu.Items.Add(new Forms.ToolStripSeparator());

            Forms.ToolStripMenuItem close = new Forms.ToolStripMenuItem("離開 Battery Pulse");
            close.Click += delegate
            {
                if (exit != null) exit();
            };
            menu.Items.Add(close);

            notifyIcon = new Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Battery Pulse｜左鍵開啟進階頁面，右鍵開啟選單",
                ContextMenuStrip = menu,
                Visible = true
            };
            // NotifyIcon.MouseClick can be swallowed by the shell when the
            // notification-area flyout is open. MouseUp is the reliable
            // single-click signal; the debounce also prevents DoubleClick
            // from opening the dashboard twice.
            notifyIcon.MouseUp += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left) RequestOpenAdvanced();
            };
            notifyIcon.MouseDown += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left) RequestOpenAdvanced();
            };
            notifyIcon.MouseClick += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left) RequestOpenAdvanced();
            };
            notifyIcon.DoubleClick += delegate
            {
                RequestOpenAdvanced();
            };
        }

        private void RequestOpenAdvanced()
        {
            if (disposed || openAdvanced == null) return;
            DateTime now = DateTime.UtcNow;
            if ((now - lastOpenRequestUtc).TotalMilliseconds < 350) return;
            lastOpenRequestUtc = now;
            try { openAdvanced(); } catch { }
        }

        private static System.Drawing.Icon LoadApplicationIcon()
        {
            try
            {
                System.Drawing.Icon extracted = System.Drawing.Icon.ExtractAssociatedIcon(
                    Assembly.GetExecutingAssembly().Location);
                if (extracted != null) return extracted;
            }
            catch { }

            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            catch { }
            try { menu.Dispose(); } catch { }
            try { icon.Dispose(); } catch { }
        }
    }
}
