using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace BatteryPulse
{
    public sealed class TopStatusBarWindow : Window
    {
        private readonly Border shell;
        private readonly StackPanel statusRow;
        private readonly TextBlock chargerText;
        private readonly Border chargerSpacer;
        private readonly Border titleSpacer;
        private readonly Image chargeIcon;
        private readonly TextBlock chargePlus;
        private readonly TextBlock chargeValue;
        private readonly StackPanel chargeGroup;
        private readonly Border chargeSpacer;
        private readonly TextBlock percentText;
        private readonly Border percentSpacer;
        private readonly Image powerIcon;
        private readonly TextBlock powerValue;
        private readonly StackPanel powerGroup;
        private readonly Border powerSpacer;
        private readonly TextBlock cpuText;
        private readonly Border cpuSpacer;
        private readonly TextBlock gpuText;
        private readonly DropShadowEffect shadow;
        private bool chargeBlinking;
        private bool powerBlinking;
        private Action openAdvanced;
        private Rect lastScreenWorkArea = SystemParameters.WorkArea;

        public TopStatusBarWindow()
        {
            Title = "Battery Pulse Top Bar";
            Width = 720;
            Height = 30;
            MinWidth = 560;
            MaxWidth = 900;
            MinHeight = 30;
            MaxHeight = 30;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            SnapsToDevicePixels = true;

            shadow = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.14
            };

            statusRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 28,
                Margin = new Thickness(12, 0, 12, 0)
            };

            statusRow.Children.Add(TextCell("BATTERY PULSE", 12.5, FontWeights.Medium, "#FFFFFFFF"));
            titleSpacer = Spacer(14);
            statusRow.Children.Add(titleSpacer);

            chargerText = TextCell("AC?", 11.5, FontWeights.Normal, "#F2FFFFFF");
            statusRow.Children.Add(chargerText);
            chargerSpacer = Spacer(14);
            statusRow.Children.Add(chargerSpacer);

            chargeIcon = IconCell(LoadIcon("BatteryPulse.ChargeLightning.png"), false);
            chargePlus = TextCell("+", 11.5, FontWeights.Medium, "#FF2FAF6F");
            chargePlus.Margin = new Thickness(0, 0, 5, 0);
            chargeValue = TextCell(string.Empty, 12.5, FontWeights.Medium, "#FFFFFFFF");
            chargeGroup = MetricGroup(chargeIcon, chargePlus, chargeValue);
            statusRow.Children.Add(chargeGroup);
            chargeSpacer = Spacer(14);
            statusRow.Children.Add(chargeSpacer);

            percentText = TextCell(string.Empty, 12.5, FontWeights.Medium, "#FFFFFFFF");
            statusRow.Children.Add(percentText);
            percentSpacer = Spacer(14);
            statusRow.Children.Add(percentSpacer);

            powerIcon = IconCell(LoadIcon("BatteryPulse.PowerLightning.png"), true);
            powerValue = TextCell(string.Empty, 12.5, FontWeights.Medium, "#FFFFFFFF");
            powerGroup = MetricGroup(powerIcon, powerValue);
            statusRow.Children.Add(powerGroup);
            powerSpacer = Spacer(14);
            statusRow.Children.Add(powerSpacer);

            cpuText = TextCell(string.Empty, 11.5, FontWeights.Normal, "#E8FFFFFF");
            statusRow.Children.Add(cpuText);
            cpuSpacer = Spacer(14);
            statusRow.Children.Add(cpuSpacer);

            gpuText = TextCell(string.Empty, 11.5, FontWeights.Normal, "#E8FFFFFF");
            statusRow.Children.Add(gpuText);

            shell = new Border
            {
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Effect = null,
                Cursor = Cursors.Hand,
                ToolTip = "Battery Pulse 即時狀態；點擊開啟進階儀表板"
            };
            shell.Child = statusRow;
            Content = shell;
            ContextMenu = BuildMenu();

            chargeIcon.Visibility = Visibility.Collapsed;
            chargePlus.Visibility = Visibility.Collapsed;
            chargeGroup.Visibility = Visibility.Collapsed;
            chargeSpacer.Visibility = Visibility.Collapsed;
            powerIcon.Visibility = Visibility.Collapsed;
            powerGroup.Visibility = Visibility.Collapsed;
            powerSpacer.Visibility = Visibility.Collapsed;
            chargerText.Visibility = Visibility.Collapsed;
            chargerSpacer.Visibility = Visibility.Collapsed;
            percentText.Visibility = Visibility.Collapsed;
            percentSpacer.Visibility = Visibility.Collapsed;
            cpuText.Visibility = Visibility.Collapsed;
            cpuSpacer.Visibility = Visibility.Collapsed;
            gpuText.Visibility = Visibility.Collapsed;

            shell.MouseEnter += delegate { AnimateShadow(true); };
            shell.MouseLeave += delegate { AnimateShadow(false); };
            shell.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenAdvanced();
            };

            Loaded += delegate
            {
                PlaceAtTopCenter();
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            };
        }

        public void SetOpenAdvancedAction(Action action)
        {
            openAdvanced = action;
        }

        public void ShowTopBar()
        {
            PlaceAtTopCenter();
            Show();
            Opacity = 1;
        }

        public void Reposition()
        {
            PlaceAtTopCenter();
        }

        internal Rect GetScreenWorkArea()
        {
            return lastScreenWorkArea;
        }

        public void UpdateSnapshot(BatterySnapshot data)
        {
            if (data == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(delegate { UpdateSnapshot(data); }));
                return;
            }

            bool hasChargePower = HasPositive(data.Watts);
            bool hasSystemPower = HasPositive(data.SystemWatts);
            bool hasPercent = data.Percent.HasValue && data.Percent.Value >= 0;
            bool hasCpu = HasTemperature(data.CpuTempC);
            bool hasGpu = HasTemperature(data.GpuTempC);
            bool charging = data.IsAcLine && (data.IsCharging ||
                (hasChargePower && string.Equals(data.BatteryPowerMode, "充電", StringComparison.OrdinalIgnoreCase)));
            // 放電時的 data.Watts 是電池流出功率，不應再以「充電瓦數」顯示。
            bool showChargeGroup = charging && hasChargePower;
            string type = data.IsAcLine
                ? (string.IsNullOrWhiteSpace(data.ChargerType) ? string.Empty : data.ChargerType)
                : string.Empty;
            string percent = hasPercent
                ? Math.Round(data.Percent.Value).ToString("0", CultureInfo.InvariantCulture) + "%"
                : string.Empty;
            bool knownCharger = data.IsAcLine && !string.IsNullOrWhiteSpace(DisplayChargerType(data.ChargerType));

            chargerText.Text = knownCharger ? ChargerLabel(data, charging) : string.Empty;
            chargerText.Visibility = knownCharger ? Visibility.Visible : Visibility.Collapsed;
            chargerSpacer.Visibility = knownCharger ? Visibility.Visible : Visibility.Collapsed;
            chargeGroup.Visibility = showChargeGroup ? Visibility.Visible : Visibility.Collapsed;
            chargeSpacer.Visibility = showChargeGroup ? Visibility.Visible : Visibility.Collapsed;
            chargePlus.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
            chargeValue.Text = FormatWatts(data.Watts);
            percentText.Text = percent;
            percentText.Visibility = hasPercent ? Visibility.Visible : Visibility.Collapsed;
            percentSpacer.Visibility = hasPercent ? Visibility.Visible : Visibility.Collapsed;
            powerGroup.Visibility = hasSystemPower ? Visibility.Visible : Visibility.Collapsed;
            powerSpacer.Visibility = hasSystemPower ? Visibility.Visible : Visibility.Collapsed;
            powerValue.Text = FormatWatts(data.SystemWatts);
            cpuText.Text = hasCpu ? "CPU " + FormatTemperature(data.CpuTempC) : string.Empty;
            cpuText.Visibility = hasCpu ? Visibility.Visible : Visibility.Collapsed;
            cpuSpacer.Visibility = hasCpu ? Visibility.Visible : Visibility.Collapsed;
            gpuText.Text = hasGpu ? "GPU " + FormatTemperature(data.GpuTempC) : string.Empty;
            gpuText.Visibility = hasGpu ? Visibility.Visible : Visibility.Collapsed;
            SetBlinking(chargeIcon, charging, ref chargeBlinking, 860);
            SetBlinking(powerIcon, hasSystemPower, ref powerBlinking, 980);
            shell.ToolTip = BuildSourceToolTip(data, type);
        }

        private static TextBlock TextCell(string text, double fontSize, FontWeight weight, string color)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brush(color),
                FontSize = Math.Max(11, fontSize),
                FontWeight = weight,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 28,
                MinHeight = 28,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 2,
                    ShadowDepth = 0,
                    Opacity = 0.86
                }
            };
        }

        private static Image IconCell(ImageSource source, bool outlined)
        {
            var icon = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                Width = 6,
                Height = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                RenderTransform = new TranslateTransform(-2, -5),
                SnapsToDevicePixels = true
            };
            if (outlined)
            {
                icon.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 1.2,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            return icon;
        }

        private static StackPanel MetricGroup(params UIElement[] children)
        {
            var group = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Height = 28,
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (UIElement child in children) group.Children.Add(child);
            return group;
        }

        private static Border Spacer(double width)
        {
            return new Border
            {
                Width = width,
                Height = 1,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
        }

        private static void AddSpacer(StackPanel row, double width)
        {
            row.Children.Add(Spacer(width));
        }

        private static void BeginBlink(UIElement icon, double minimumOpacity, int milliseconds)
        {
            icon.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(minimumOpacity, 1.0, TimeSpan.FromMilliseconds(milliseconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
        }

        private static void SetBlinking(Image icon, bool enabled, ref bool active, int milliseconds)
        {
            if (enabled)
            {
                icon.Visibility = Visibility.Visible;
                if (!active)
                {
                    active = true;
                    BeginBlink(icon, 0.4, milliseconds);
                }
                return;
            }

            if (active)
            {
                active = false;
                icon.BeginAnimation(UIElement.OpacityProperty, null);
            }
            icon.Opacity = 0;
            icon.Visibility = Visibility.Collapsed;
        }

        private static ImageSource LoadIcon(string resourceName)
        {
            try
            {
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resourceName);
                    if (File.Exists(path)) stream = File.OpenRead(path);
                }
                if (stream == null) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                stream.Dispose();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        private static string FormatTemperature(double? temperature)
        {
            return HasTemperature(temperature)
                ? Math.Round(temperature.Value).ToString("0", CultureInfo.InvariantCulture) + " °C"
                : string.Empty;
        }

        private static string ChargerLabel(BatterySnapshot data, bool charging)
        {
            if (!data.IsAcLine) return string.Empty;
            string label = DisplayChargerType(data.ChargerType);
            return charging ? label + "+" : label;
        }

        private void OpenAdvanced()
        {
            if (openAdvanced == null) return;
            Hide();
            openAdvanced();
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var advanced = new MenuItem { Header = "開啟進階儀表板" };
            advanced.Click += delegate { OpenAdvanced(); };
            menu.Items.Add(advanced);
            var reposition = new MenuItem { Header = "重新定位到目前螢幕" };
            reposition.Click += delegate { Reposition(); };
            menu.Items.Add(reposition);
            menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "離開 Battery Pulse" };
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            return menu;
        }

        private static string FormatWatts(double? watts)
        {
            return HasPositive(watts)
                ? watts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W"
                : string.Empty;
        }

        private static bool HasPositive(double? value)
        {
            return value.HasValue && value.Value > 0;
        }

        private static bool HasTemperature(double? value)
        {
            return value.HasValue && value.Value > 0 && value.Value < 130;
        }

        private static string DisplayChargerType(string type)
        {
            if (string.IsNullOrWhiteSpace(type) || type == "未知" || type == "未接電") return string.Empty;
            if (type == "原廠充電器") return "AC";
            if (type == "USB-PD") return "PD";
            return string.Empty;
        }

        private static string BuildSourceToolTip(BatterySnapshot data, string type)
        {
            string typeSource = string.IsNullOrWhiteSpace(data.ChargerTypeSource) ? "未知" : data.ChargerTypeSource;
            string systemSource = data.IsAcLine
                ? "LibreHardwareMonitor 元件功率或可用電池資料"
                : "Windows BatteryStatus / DischargeRate";
            return "充電：Windows BatteryStatus / ChargeRate（電池吸收功率）\n" +
                "耗電：" + systemSource + "\n" +
                "充電器：" + type + "（" + typeSource + "）\n" +
                "更新：" + data.ReadAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void AnimateShadow(bool active)
        {
            shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                new DoubleAnimation(active ? 12 : 8, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(active ? 0.22 : 0.14, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        private void PlaceAtTopCenter()
        {
            Rect placementArea = SystemParameters.WorkArea;
            Rect workArea = placementArea;
            try
            {
                Forms.Screen screen = Forms.Screen.FromPoint(Forms.Control.MousePosition);
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    Point topLeft = source.CompositionTarget.TransformFromDevice.Transform(
                        new Point(screen.Bounds.Left, screen.Bounds.Top));
                    Point bottomRight = source.CompositionTarget.TransformFromDevice.Transform(
                        new Point(screen.Bounds.Right, screen.Bounds.Bottom));
                    placementArea = new Rect(topLeft, bottomRight);

                    Point workTopLeft = source.CompositionTarget.TransformFromDevice.Transform(
                        new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
                    Point workBottomRight = source.CompositionTarget.TransformFromDevice.Transform(
                        new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
                    workArea = new Rect(workTopLeft, workBottomRight);
                }
            }
            catch { }

            lastScreenWorkArea = workArea;
            Width = Math.Min(900, Math.Max(560, placementArea.Width - 16));
            Left = placementArea.Left + Math.Max(0, (placementArea.Width - Width) / 2);
            Top = placementArea.Top;
        }

        private static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }

    public static class TopBarProgram
    {
        [STAThread]
        public static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\BatteryPulseTopBar", out created))
            {
                if (!created) return;

                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        WriteCrash(e.Exception);
                        e.Handled = true;
                        app.Shutdown(1);
                    };

                    var bar = new TopStatusBarWindow();
                    BatteryWindow host = null;
                    bar.SetOpenAdvancedAction(delegate
                    {
                        if (host != null) host.OpenAdvancedDashboard();
                    });
                    bar.Closed += delegate
                    {
                        if (host != null) host.ShutdownTopBarHost();
                        app.Shutdown();
                    };

                    app.MainWindow = bar;
                    bar.Show();
                    // 先讓頂端列完成繪製，再建立完整儀表板主機，避免啟動時看似沒有反應。
                    app.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            host = new BatteryWindow();
                            host.ConfigureTopBarHost(delegate { bar.ShowTopBar(); }, delegate { return bar.GetScreenWorkArea(); });
                            host.SnapshotUpdated += delegate(BatterySnapshot data) { bar.UpdateSnapshot(data); };
                            host.Show();
                            host.Hide();
                        }
                        catch (Exception ex)
                        {
                            WriteCrash(ex);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    app.Run();
                }
                catch (Exception ex)
                {
                    WriteCrash(ex);
                }
            }
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BatteryPulse.TopBar.crash.log"), ex.ToString());
            }
            catch { }
        }

    }
}
