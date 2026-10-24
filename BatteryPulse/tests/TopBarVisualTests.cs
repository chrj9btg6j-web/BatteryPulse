using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            if (usage == null || !(usage.Children[1] is TextBlock) || !(usage.Children[3] is TextBlock))
                throw new Exception("Reset icon is not between the five-hour quota and reset time.");
            var resetIcon = (Canvas)usage.Children[2];
            if (resetIcon.Width != 11 || resetIcon.Height != 11 || resetIcon.Effect != null || resetIcon.Children.Count != 2)
                throw new Exception("Reset icon size or appearance differs from the Codex mark.");

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

            Console.WriteLine("Top bar empty pixels are transparent; only the compositor anchor has alpha 1.");
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
}
