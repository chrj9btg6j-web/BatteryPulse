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
        private bool disposed;

        public TopBarTrayIcon(Action showTopBar, Action openAdvanced, Action exit)
        {
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
                Text = "Battery Pulse｜左鍵顯示，右鍵開啟選單",
                ContextMenuStrip = menu,
                Visible = true
            };
            notifyIcon.MouseClick += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left && showTopBar != null)
                    showTopBar();
            };
            notifyIcon.DoubleClick += delegate
            {
                if (openAdvanced != null) openAdvanced();
            };
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
