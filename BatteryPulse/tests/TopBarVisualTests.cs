using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatteryPulse;

internal static class TopBarVisualTests
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var window = new TopStatusBarWindow();
            var surface = window.Content as Grid;
            if (surface == null || surface.Children.Count != 2)
                throw new Exception("Top bar transparent surface was not created.");

            var shell = surface.Children[0] as Border;
            var anchor = surface.Children[1] as Border;
            if (shell == null || anchor == null || anchor.Width != 1 || anchor.Height != 1)
                throw new Exception("Top bar layers are not arranged as expected.");
            AssertTransparent(window.Background, "window");
            AssertTransparent(surface.Background, "surface");
            AssertTransparent(shell.Background, "shell");

            var row = shell.Child as StackPanel;
            StackPanel usage = null;
            if (row != null)
                foreach (UIElement child in row.Children)
                {
                    var group = child as StackPanel;
                    if (group != null && group.Children.Count == 5 && group.Children[2] is Canvas)
                        usage = group;
                }
            if (usage == null || !(usage.Children[0] is Image) || !(usage.Children[1] is TextBlock) || !(usage.Children[3] is TextBlock))
                throw new Exception("Reset icon is not between the five-hour quota and reset time.");
            var codexIcon = (Image)usage.Children[0];
            if (codexIcon.Width != 11 || codexIcon.Height != 11 || codexIcon.Effect != null || codexIcon.Source == null)
                throw new Exception("Codex icon must be visible without a frame or shadow at reset icon size.");
            var resetIcon = (Canvas)usage.Children[2];
            if (resetIcon.Width != 11 || resetIcon.Height != 11 || resetIcon.Effect != null || resetIcon.Children.Count != 2)
                throw new Exception("Reset icon size or appearance differs from the Codex icon.");
            var resetTime = (TextBlock)usage.Children[3];
            if (resetTime.TextAlignment != TextAlignment.Left)
                throw new Exception("Reset time starts too far from its update icon.");
            var weekly = (TextBlock)usage.Children[4];
            if (weekly.Text != "--/周")
                throw new Exception("Weekly Codex quota is not labeled /周.");

            AssertStableSlot(window, "percentText", "9%", "100%");
            AssertStableSlot(window, "cpuText", "CPU 9 °C", "CPU 100 °C");
            AssertStableSlot(window, "gpuText", "GPU 9 °C", "GPU 100 °C");
            AssertStableSlot(window, "ramText", "RAM 9%", "RAM 100%");
            AssertStableSlot(window, "cpuUsageText", "CPU 9%", "CPU 100%");
            AssertStableSlot(window, "gpuUsageText", "GPU 9%", "GPU 100%");
            AssertStableSlot(window, "codexFiveHourText", "9/5時", "100/5時");
            AssertStableSlot(window, "codexWeeklyText", "9/周", "100/周");
            AssertSlotFits(window, "chargeValue", "9999.9 W");
            AssertSlotFits(window, "powerValue", "9999.9 W");
            AssertSlotFits(window, "etaText", "續航 999h59");
            AssertSlotFits(window, "codexFiveHourText", "100/5時");
            AssertSlotFits(window, "codexFiveHourResetText", "23:59");
            AssertSlotFits(window, "codexWeeklyText", "100/周");

            surface.Measure(new Size(900, 30));
            surface.Arrange(new Rect(0, 0, 900, 30));
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(900, 30, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TopBarVisualTests.png")))
                png.Save(file);
            byte[] pixels = new byte[900 * 30 * 4];
            bitmap.CopyPixels(pixels, 900 * 4, 0);

            // The screenshot defect was a full-width faint bar. Empty points
            // across the top and bottom must have no alpha at all.
            AssertAlpha(pixels, 900, 5, 2, 0);
            AssertAlpha(pixels, 900, 450, 2, 0);
            AssertAlpha(pixels, 900, 895, 2, 0);
            AssertAlpha(pixels, 900, 450, 29, 0);
            AssertAlpha(pixels, 900, 899, 29, 0);
            if (pixels[(29 * 900 * 4) + 3] != 1)
                throw new Exception("One-pixel compositor anchor is missing.");

            double smallWidth = MeasureChargingRow(window, surface, row, 9.9);
            double largeWidth = MeasureChargingRow(window, surface, row, 999.9);
            if (Math.Abs(smallWidth - largeWidth) > 0.1)
                throw new Exception("Charging watt changes still shift the centered status row.");

            Console.WriteLine("Top bar alpha, weekly label, reset icon, and numeric slot widths passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void AssertTransparent(Brush brush, string name)
    {
        var solid = brush as SolidColorBrush;
        if (solid == null || solid.Color.A != 0)
            throw new Exception(name + " background is not transparent.");
    }

    private static void AssertAlpha(byte[] pixels, int width, int x, int y, byte expected)
    {
        byte alpha = pixels[((y * width) + x) * 4 + 3];
        if (alpha != expected)
            throw new Exception("Unexpected alpha " + alpha + " at " + x + "," + y + ".");
    }

    private static void AssertStableSlot(TopStatusBarWindow window, string fieldName, string shortText, string longText)
    {
        var field = typeof(TopStatusBarWindow).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new Exception("Missing status field: " + fieldName);
        var text = field.GetValue(window) as TextBlock;
        if (text == null) throw new Exception("Status field is not text: " + fieldName);
        text.Text = shortText;
        text.Measure(new Size(double.PositiveInfinity, 28));
        double shortWidth = text.DesiredSize.Width;
        text.Text = longText;
        text.Measure(new Size(double.PositiveInfinity, 28));
        if (Math.Abs(shortWidth - text.DesiredSize.Width) > 0.1)
            throw new Exception(fieldName + " changes the centered row width as digits change.");
    }

    private static void AssertSlotFits(TopStatusBarWindow window, string fieldName, string sample)
    {
        var field = typeof(TopStatusBarWindow).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        var value = field.GetValue(window) as TextBlock;
        double reservedWidth = value.Width;
        string originalText = value.Text;
        value.Width = double.NaN;
        value.Text = sample;
        value.Measure(new Size(double.PositiveInfinity, 28));
        double neededWidth = value.DesiredSize.Width;
        value.Width = reservedWidth;
        value.Text = originalText;
        if (neededWidth > reservedWidth - 1)
            throw new Exception(fieldName + " clips " + sample + " (" + neededWidth + " > " + reservedWidth + ").");
    }

    private static double MeasureChargingRow(TopStatusBarWindow window, Grid surface, StackPanel row, double watts)
    {
        window.UpdateSnapshot(new BatterySnapshot
        {
            IsAcLine = true,
            IsCharging = true,
            Watts = watts,
            SystemWatts = 34.4,
            Percent = 79
        });
        surface.InvalidateMeasure();
        surface.Measure(new Size(900, 30));
        surface.Arrange(new Rect(0, 0, 900, 30));
        surface.UpdateLayout();
        bool found = false;
        foreach (UIElement child in row.Children)
        {
            var group = child as StackPanel;
            if (group == null || group.Visibility != Visibility.Visible) continue;
            foreach (UIElement item in group.Children)
            {
                var text = item as TextBlock;
                if (text == null || text.Text != watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " W")
                    continue;
                if (text.Width < 54 || text.TextAlignment != TextAlignment.Left ||
                    Typography.GetNumeralAlignment(text) != FontNumeralAlignment.Tabular)
                    throw new Exception("Charging watt slot is not fixed and tabular.");
                found = true;
            }
        }
        if (!found) throw new Exception("Charging watt reading is not visible in the test row.");
        return row.DesiredSize.Width;
    }
}
