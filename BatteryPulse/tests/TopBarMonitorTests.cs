using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using BatteryPulse;
using Forms = System.Windows.Forms;

internal static class TopBarMonitorTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y,
        int width, int height, uint flags);

    [STAThread]
    private static int Main()
    {
        TopStatusBarWindow window = null;
        try
        {
            window = new TopStatusBarWindow();
            window.Show();
            window.Hide();
            Pump();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            var requestField = typeof(TopStatusBarWindow).GetField("placementRequest",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var finish = typeof(TopStatusBarWindow).GetMethod("FinishPlacement",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (handle == IntPtr.Zero || requestField == null || finish == null)
                throw new Exception("Monitor placement setup is unavailable.");

            // Exercise the fully populated row, not the initially hidden startup row.
            var rowField = typeof(TopStatusBarWindow).GetField("statusRow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var row = rowField.GetValue(window) as System.Windows.Controls.StackPanel;
            foreach (System.Windows.UIElement child in row.Children)
                child.Visibility = System.Windows.Visibility.Visible;

            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            {
                if (!SetWindowPos(handle, IntPtr.Zero, screen.Bounds.Left + 20,
                    screen.Bounds.Top + 20, 0, 0, 0x0001 | 0x0004 | 0x0010))
                    throw new Exception("Could not move to " + screen.DeviceName);
                Pump();
                finish.Invoke(window, new object[] { screen.DeviceName, (int)requestField.GetValue(window) });
                Pump();
                NativeRect actual;
                if (!GetWindowRect(handle, out actual))
                    throw new Exception("Could not measure " + screen.DeviceName);
                double actualCenter = (actual.Left + actual.Right) / 2.0;
                double expectedCenter = screen.Bounds.Left + screen.Bounds.Width / 2.0;
                if (Math.Abs(actualCenter - expectedCenter) > 2)
                    throw new Exception(screen.DeviceName + " center differs by " +
                        (actualCenter - expectedCenter) + " physical pixels.");
                var scale = row.LayoutTransform as System.Windows.Media.ScaleTransform;
                if (scale != null && scale.ScaleX < 0.999)
                    throw new Exception(screen.DeviceName + " shrinks the original status-bar text to " + scale.ScaleX);
                Console.WriteLine(screen.DeviceName + " centered within 2 physical pixels; window width=" +
                    window.Width.ToString("F1") + ", row width=" + row.DesiredSize.Width.ToString("F1") +
                    ", scale=" + (scale == null ? "1" : scale.ScaleX.ToString("F3")) + ".");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (window != null) window.Close();
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }
}
