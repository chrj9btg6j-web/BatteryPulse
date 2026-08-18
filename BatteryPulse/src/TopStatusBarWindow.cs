using System;
using System.Collections.Generic;
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
using Path = System.IO.Path;

namespace BatteryPulse
{
    public sealed class TopStatusBarWindow : Window
    {
        private readonly Border shell;
        private readonly StackPanel statusRow;
        private readonly TextBlock titleText;
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
        private readonly TextBlock etaText;
        private readonly TextBlock ramText;
        private readonly TextBlock cpuUsageText;
        private readonly TextBlock gpuUsageText;
        private readonly DropShadowEffect shadow;
        private readonly List<Border> layoutSpacers = new List<Border>();
        private AppSettings topBarSettings;
        private DateTime settingsWriteTime = DateTime.MinValue;
        private bool chargeBlinking;
        private bool powerBlinking;
        private Action openAdvanced;
        private Rect lastScreenWorkArea = SystemParameters.WorkArea;
        private readonly System.Windows.Threading.DispatcherTimer visibilityGuard;
        private bool closed;

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const int GwlExStyle = -20;
        private const long WsExTransparent = 0x00000020L;
        private const long WsExNoActivate = 0x08000000L;
        private const int WmNcHitTest = 0x0084;
        private const int WmMouseActivate = 0x0021;
        private const int HtTransparent = -1;
        private const int MaNoActivate = 3;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

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
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            AllowsTransparency = true;
            // Keep one almost-transparent composited pixel at the window root.
            // Some Windows desktop compositions drop a fully transparent
            // WPF top-level window before its text can be displayed.
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
            Opacity = 1;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            SnapsToDevicePixels = true;
            topBarSettings = AppSettings.Load();
            settingsWriteTime = ReadSettingsWriteTime();

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
                Margin = new Thickness(12, 0, 12, 0),
                IsHitTestVisible = false
            };
            titleText = TextCell(
                string.IsNullOrWhiteSpace(topBarSettings.CustomTitle) ? "BATTERY PULSE" : topBarSettings.CustomTitle,
                12.5,
                FontWeights.Medium,
                "#FFFFFFFF");
            statusRow.Children.Add(titleText);
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

            etaText = TextCell(string.Empty, 11.5, FontWeights.Normal, "#E8FFFFFF");
            statusRow.Children.Add(etaText);
            ramText = TextCell(string.Empty, 11.5, FontWeights.Normal, "#E8FFFFFF");
            statusRow.Children.Add(ramText);
            cpuUsageText = TextCell(string.Empty, 11.5, FontWeights.Normal, "#E8FFFFFF");
            statusRow.Children.Add(cpuUsageText);
            gpuUsageText = TextCell(string.Empty, 11.5, FontWeights.Normal, "#E8FFFFFF");
            statusRow.Children.Add(gpuUsageText);

            shell = new Border
            {
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                // A fully transparent WPF layered window can be composed as an
                // empty surface during cold start. Alpha 1 is visually
                // transparent but keeps the top bar in the compositor.
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                Effect = null,
                Cursor = Cursors.Arrow,
                IsHitTestVisible = false
            };
            shell.Child = statusRow;
            Content = shell;
            IsHitTestVisible = false;

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
            etaText.Visibility = Visibility.Collapsed;
            ramText.Visibility = Visibility.Collapsed;
            cpuUsageText.Visibility = Visibility.Collapsed;
            gpuUsageText.Visibility = Visibility.Collapsed;

            RebuildStatusLayout();

            visibilityGuard = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            visibilityGuard.Tick += delegate { EnsureVisibleIfNeeded(); };

            Loaded += delegate
            {
                PlaceAtTopCenter();
                EnsureNativeVisibility();
                visibilityGuard.Start();
                // Keep the status bar immediately visible. A transparent-window
                // fade can leave a layered WPF window visually absent after a
                // cold start even though its process is still running.
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
            };
            SourceInitialized += delegate
            {
                InstallPointerHitTestHook();
                EnsureNativeVisibility();
            };
            Closed += delegate
            {
                closed = true;
                visibilityGuard.Stop();
            };
        }

        private void RebuildStatusLayout()
        {
            if (statusRow == null || titleText == null || topBarSettings == null) return;
            statusRow.Children.Clear();
            layoutSpacers.Clear();
            statusRow.Children.Add(titleText);
            foreach (string id in topBarSettings.GetTopBarItems())
            {
                FrameworkElement element = GetItemElement(id);
                if (element == null) continue;
                Border spacer = Spacer(14);
                layoutSpacers.Add(spacer);
                statusRow.Children.Add(spacer);
                statusRow.Children.Add(element);
            }
            ApplyLayoutVisibility();
        }

        private FrameworkElement GetItemElement(string id)
        {
            switch (id)
            {
                case "charger": return chargerText;
                case "charge": return chargeGroup;
                case "percent": return percentText;
                case "power": return powerGroup;
                case "cpuTemp": return cpuText;
                case "gpuTemp": return gpuText;
                case "eta": return etaText;
                case "ram": return ramText;
                case "cpuUsage": return cpuUsageText;
                case "gpuUsage": return gpuUsageText;
                default: return null;
            }
        }

        private void ApplyLayoutVisibility()
        {
            for (int i = 0; i < layoutSpacers.Count; i++)
            {
                int childIndex = 2 + i * 2;
                if (childIndex >= statusRow.Children.Count) break;
                FrameworkElement item = statusRow.Children[childIndex] as FrameworkElement;
                layoutSpacers[i].Visibility = item != null && item.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void ReloadSettingsIfChanged()
        {
            DateTime current = ReadSettingsWriteTime();
            if (current == settingsWriteTime) return;
            settingsWriteTime = current;
            topBarSettings = AppSettings.Load();
            titleText.Text = string.IsNullOrWhiteSpace(topBarSettings.CustomTitle)
                ? "BATTERY PULSE"
                : topBarSettings.CustomTitle;
            RebuildStatusLayout();
        }

        private static DateTime ReadSettingsWriteTime()
        {
            try
            {
                return File.Exists(AppSettings.SettingsFilePath)
                    ? File.GetLastWriteTimeUtc(AppSettings.SettingsFilePath)
                    : DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        private bool IsItemEnabled(string id)
        {
            return topBarSettings != null && topBarSettings.IsTopBarItemEnabled(id);
        }

        private static string FormatEta(double seconds)
        {
            if (seconds < 60) return "ETA <1m";
            TimeSpan value = TimeSpan.FromSeconds(seconds);
            if (value.TotalHours >= 1)
                return "ETA " + ((int)Math.Floor(value.TotalHours)).ToString(CultureInfo.InvariantCulture) + "h" + value.Minutes.ToString("00", CultureInfo.InvariantCulture);
            return "ETA " + Math.Max(1, (int)Math.Round(value.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "m";
        }

        private static string FormatEstimate(BatterySnapshot data)
        {
            if (data == null) return string.Empty;
            if (data.RuntimeEtaSeconds.HasValue && data.RuntimeEtaSeconds.Value > 0)
                return "續航 " + FormatEta(data.RuntimeEtaSeconds.Value).Replace("ETA ", string.Empty);
            return string.Empty;
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue && value.Value >= 0 && value.Value <= 100
                ? Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture) + "%"
                : string.Empty;
        }

        public void SetOpenAdvancedAction(Action action)
        {
            openAdvanced = action;
        }

        public void ShowTopBar()
        {
            EnsureVisible(true);
        }

        public void EnsureVisible()
        {
            EnsureVisible(false);
        }

        private void EnsureVisible(bool reposition)
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(delegate { EnsureVisible(reposition); })); }
                catch (Exception ex) { RuntimeDiagnostics.Write("排程頂端列顯示恢復", ex); }
                return;
            }

            if (closed) return;
            try
            {
                if (reposition || !IsVisible || Visibility != Visibility.Visible || WindowState == WindowState.Minimized)
                    PlaceAtTopCenter();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (!IsVisible) Show();
                Visibility = Visibility.Visible;
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                EnsureNativeVisibility();
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("恢復頂端列顯示", ex);
            }
        }

        private void EnsureVisibleIfNeeded()
        {
            if (closed || !IsLoaded) return;
            try
            {
                if (!IsVisible || Visibility != Visibility.Visible || WindowState == WindowState.Minimized || Opacity < 0.01)
                    EnsureVisible(true);
                else
                    EnsureNativeVisibility();
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("頂端列可見性巡檢", ex);
            }
        }

        private void EnsureNativeVisibility()
        {
            if (!IsLoaded || closed) return;
            try
            {
                Topmost = true;
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;
                SetWindowPos(
                    handle,
                    HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("恢復頂端列 Z 序", ex);
            }
        }

        public void Reposition()
        {
            EnsureVisible(true);
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
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate { UpdateSnapshot(data); }));
                }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.Write("排程頂端列快照", ex);
                }
                return;
            }

            try
            {
                ReloadSettingsIfChanged();

            bool hasChargePower = HasPositive(data.Watts);
            bool hasSystemPower = HasPositive(data.SystemWatts);
            bool hasPercent = data.Percent.HasValue && data.Percent.Value >= 0;
            bool hasCpu = HasTemperature(data.CpuTempC);
            bool hasGpu = HasTemperature(data.GpuTempC);
            bool hasRam = data.MemoryUsedPercent.HasValue && data.MemoryUsedPercent.Value >= 0;
            bool hasCpuUsage = data.CpuUsagePercent.HasValue && data.CpuUsagePercent.Value >= 0;
            bool hasGpuUsage = data.GpuUsagePercent.HasValue && data.GpuUsagePercent.Value >= 0;
            bool charging = data.IsAcLine && (data.IsCharging ||
                (hasChargePower && string.Equals(data.BatteryPowerMode, "充電", StringComparison.OrdinalIgnoreCase)));
            bool hasEta = data.RuntimeEtaSeconds.HasValue && data.RuntimeEtaSeconds.Value > 0;
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
            chargerText.Visibility = knownCharger && IsItemEnabled("charger") ? Visibility.Visible : Visibility.Collapsed;
            chargeGroup.Visibility = showChargeGroup && IsItemEnabled("charge") ? Visibility.Visible : Visibility.Collapsed;
            chargePlus.Visibility = chargeGroup.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            chargeValue.Text = FormatWatts(data.Watts);
            percentText.Text = percent;
            percentText.Visibility = hasPercent && IsItemEnabled("percent") ? Visibility.Visible : Visibility.Collapsed;
            powerGroup.Visibility = hasSystemPower && IsItemEnabled("power") ? Visibility.Visible : Visibility.Collapsed;
            powerValue.Text = FormatWatts(data.SystemWatts);
            cpuText.Text = hasCpu ? "CPU " + FormatTemperature(data.CpuTempC) : string.Empty;
            cpuText.Visibility = hasCpu && IsItemEnabled("cpuTemp") ? Visibility.Visible : Visibility.Collapsed;
            gpuText.Text = hasGpu ? "GPU " + FormatTemperature(data.GpuTempC) : string.Empty;
            gpuText.Visibility = hasGpu && IsItemEnabled("gpuTemp") ? Visibility.Visible : Visibility.Collapsed;
            etaText.Text = hasEta ? FormatEstimate(data) : string.Empty;
            etaText.Visibility = hasEta && IsItemEnabled("eta") ? Visibility.Visible : Visibility.Collapsed;
            ramText.Text = hasRam ? "RAM " + FormatPercent(data.MemoryUsedPercent) : string.Empty;
            ramText.Visibility = hasRam && IsItemEnabled("ram") ? Visibility.Visible : Visibility.Collapsed;
            cpuUsageText.Text = hasCpuUsage ? "CPU " + FormatPercent(data.CpuUsagePercent) : string.Empty;
            cpuUsageText.Visibility = hasCpuUsage && IsItemEnabled("cpuUsage") ? Visibility.Visible : Visibility.Collapsed;
            gpuUsageText.Text = hasGpuUsage ? "GPU " + FormatPercent(data.GpuUsagePercent) : string.Empty;
            gpuUsageText.Visibility = hasGpuUsage && IsItemEnabled("gpuUsage") ? Visibility.Visible : Visibility.Collapsed;
            SetBlinking(chargeIcon, charging, ref chargeBlinking, 860);
            SetBlinking(powerIcon, hasSystemPower, ref powerBlinking, 980);
            ApplyLayoutVisibility();
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("頂端狀態列套用快照", ex);
            }
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
            // 保留頂端列在進階儀表板上方，讓展開後仍可即時看到關鍵狀態。
            // 進階頁返回小工具時，host 仍會呼叫 ShowTopBar；此處不需先隱藏。
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
                ? (string.IsNullOrWhiteSpace(data.SystemWattsSource)
                    ? "LibreHardwareMonitor 元件功率估算"
                    : data.SystemWattsSource + "（可能包含 GPU 元件功耗；非 PD／變壓器總輸入）")
                : "Windows BatteryStatus / DischargeRate";
            string gpuSource = string.IsNullOrWhiteSpace(data.GpuUsageSource)
                ? "GPU 使用率：未取得"
                : "GPU 使用率：" + data.GpuUsageSource + "（優先 Core／3D）";
            return "充電：Windows BatteryStatus / ChargeRate（電池吸收功率）\n" +
                "耗電：" + systemSource + "\n" +
                gpuSource + "\n" +
                "充電器：" + type + "（" + typeSource + "）\n" +
                "更新：" + data.ReadAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void InstallPointerHitTestHook()
        {
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            if (source == null) return;

            source.AddHook(PointerHitTestHook);
            MakeWindowClickThrough(source.Handle);
        }

        private static void MakeWindowClickThrough(IntPtr hwnd)
        {
            try
            {
                long current = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
                long updated = current | WsExTransparent | WsExNoActivate;
                if (updated != current)
                {
                    SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updated));
                    SetWindowPos(
                        hwnd,
                        IntPtr.Zero,
                        0,
                        0,
                        0,
                        0,
                        SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                }
            }
            catch { }
        }

        private IntPtr PointerHitTestHook(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmMouseActivate)
            {
                handled = true;
                return new IntPtr(MaNoActivate);
            }

            if (message != WmNcHitTest)
                return IntPtr.Zero;

            handled = true;
            // The top bar is display-only. Every mouse point passes through to
            // the application underneath; the notification-area icon is the
            // sole entry point for the advanced dashboard.
            return new IntPtr(HtTransparent);
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
        private const string ShowSignalName = "Local\\BatteryPulseTopBarShow";
        private const uint WindowStationAccess = 0x037F;
        private const uint DesktopAccess = 0x01FF;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenWindowStation(string name, bool inherit, uint access);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenDesktop(string name, uint flags, bool inherit, uint access);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessWindowStation(IntPtr station);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr desktop);

        [STAThread]
        public static void Main(string[] args)
        {
            Thread uiThread = new Thread(new ThreadStart(delegate { RunApplication(args); }))
            {
                IsBackground = false,
                Name = "BatteryPulse interactive UI"
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiThread.Join();
        }

        private static void RunApplication(string[] args)
        {
            AttachToInteractiveDesktop();
            RuntimeDiagnostics.AttachGlobalHandlers();
            StartupManager.EnsureFirstRun(args);
            bool created;
            using (var mutex = new Mutex(true, "Local\\BatteryPulseTopBar", out created))
            {
                if (!created)
                {
                    SignalExistingInstance();
                    return;
                }

                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        RuntimeDiagnostics.Write("TopBar WPF Dispatcher", e.Exception);
                        e.Handled = true;
                    };

                    var bar = new TopStatusBarWindow();
                    BatteryWindow host = null;
                    System.Windows.Threading.Dispatcher hostDispatcher = null;
                    Thread hostThread = null;
                    TopBarTrayIcon tray = null;
                    int hostReady = 0;
                    int openRequestPending = 0;
                    int hostShutdownRequested = 0;
                    int stopSignalThread = 0;
                    var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
                    var signalThread = new Thread(new ThreadStart(delegate
                    {
                        while (Interlocked.CompareExchange(ref stopSignalThread, 0, 0) == 0)
                        {
                            try
                            {
                                showSignal.WaitOne();
                                if (Interlocked.CompareExchange(ref stopSignalThread, 0, 0) != 0) break;
                                app.Dispatcher.BeginInvoke(new Action(delegate { bar.EnsureVisible(); }));
                            }
                            catch { break; }
                        }
                    }))
                    {
                        IsBackground = true,
                        Name = "BatteryPulse TopBar visibility recovery"
                    };
                    signalThread.Start();
                    // The tray icon can be clicked while the hidden dashboard host is
                    // still constructing on its STA thread. Send the request directly
                    // to the dashboard dispatcher so the click-through top bar is not
                    // part of the opening path.
                    Action openAdvancedFromTray = delegate
                    {
                        try
                        {
                            bar.Dispatcher.BeginInvoke(new Action(delegate { bar.EnsureVisible(); }),
                                System.Windows.Threading.DispatcherPriority.Normal);
                        }
                        catch (Exception ex) { RuntimeDiagnostics.Write("排程頂端列顯示", ex); }

                        if (Interlocked.Exchange(ref openRequestPending, 1) != 0) return;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try
                            {
                                for (int attempts = 0; attempts < 100; attempts++)
                                {
                                    BatteryWindow dashboard = host;
                                    System.Windows.Threading.Dispatcher dispatcher = hostDispatcher;
                                    bool ready = dashboard != null &&
                                        dispatcher != null &&
                                        Interlocked.CompareExchange(ref hostReady, 0, 0) != 0 &&
                                        !dispatcher.HasShutdownStarted;
                                    if (ready)
                                    {
                                        try
                                        {
                                            dispatcher.BeginInvoke(new Action(delegate
                                            {
                                                try
                                                {
                                                    if (!dashboard.IsLoaded)
                                                    {
                                                        dashboard.Show();
                                                        dashboard.Hide();
                                                    }
                                                    dashboard.OpenAdvancedDashboard();
                                                }
                                                catch (Exception ex) { RuntimeDiagnostics.Write("開啟進階儀表板", ex); }
                                                finally { Interlocked.Exchange(ref openRequestPending, 0); }
                                            }), System.Windows.Threading.DispatcherPriority.Send);
                                            return;
                                        }
                                        catch (Exception ex) { RuntimeDiagnostics.Write("排程進階儀表板", ex); }
                                        return;
                                    }
                                    Thread.Sleep(100);
                                }
                                RuntimeDiagnostics.Write("工作列開啟進階頁面", new InvalidOperationException("儀表板初始化逾時"));
                            }
                            catch (Exception ex) { RuntimeDiagnostics.Write("工作列開啟進階頁面", ex); }
                            finally { Interlocked.Exchange(ref openRequestPending, 0); }
                        });
                    };
                    bar.SetOpenAdvancedAction(openAdvancedFromTray);
                    bar.Closed += delegate
                    {
                        Interlocked.Exchange(ref stopSignalThread, 1);
                        Interlocked.Exchange(ref hostReady, 0);
                        Interlocked.Exchange(ref openRequestPending, 0);
                        try { showSignal.Set(); } catch { }
                        try { if (tray != null) tray.Dispose(); } catch { }
                        BatteryWindow dashboard = host;
                        System.Windows.Threading.Dispatcher dispatcher = hostDispatcher;
                        Interlocked.Exchange(ref hostShutdownRequested, 1);
                        if (dashboard != null && dispatcher != null && !dispatcher.HasShutdownStarted)
                        {
                            try
                            {
                                dispatcher.BeginInvoke(new Action(delegate
                                {
                                    try { dashboard.ShutdownTopBarHost(); } catch { }
                                    try { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal); } catch { }
                                }), System.Windows.Threading.DispatcherPriority.Normal);
                            }
                            catch { }
                        }
                        app.Shutdown();
                    };

                    tray = new TopBarTrayIcon(
                        delegate { bar.EnsureVisible(); },
                        openAdvancedFromTray,
                        delegate { bar.Close(); });

                    app.MainWindow = bar;
                    bar.Show();
                    bar.EnsureVisible();
                    // 儀表板包含完整頁面與硬體讀取初始化，放到獨立 STA 執行緒，
                    // 避免它阻塞頂端列自己的繪製與滑鼠互動。
                    hostThread = new Thread(new ThreadStart(delegate
                    {
                        try
                        {
                            System.Windows.Threading.Dispatcher dashboardDispatcher =
                                System.Windows.Threading.Dispatcher.CurrentDispatcher;
                            hostDispatcher = dashboardDispatcher;
                            host = new BatteryWindow();
                            host.ConfigureTopBarHost(delegate { bar.ShowTopBar(); }, delegate { return bar.GetScreenWorkArea(); });
                            host.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
                            {
                                if (Interlocked.CompareExchange(ref hostShutdownRequested, 0, 0) != 0) return;
                                e.Cancel = true;
                                try { host.CancelTopBarHostClose(); }
                                catch (Exception ex) { RuntimeDiagnostics.Write("保留進階宿主視窗", ex); }
                            };
                            host.SnapshotUpdated += delegate(BatterySnapshot data)
                            {
                                try
                                {
                                    bar.Dispatcher.BeginInvoke(new Action(delegate { bar.UpdateSnapshot(data); }),
                                        System.Windows.Threading.DispatcherPriority.Background);
                                }
                                catch (Exception ex) { RuntimeDiagnostics.Write("頂端列快照回呼", ex); }
                            };
                            host.Show();
                            host.Hide();
                            Interlocked.Exchange(ref hostReady, 1);
                            // The hidden dashboard host must never determine the
                            // visibility of the compact top bar.
                            bar.Dispatcher.BeginInvoke(new Action(delegate { bar.ShowTopBar(); }),
                                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                            System.Windows.Threading.Dispatcher.Run();
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Exchange(ref hostReady, 0);
                            RuntimeDiagnostics.Write("頂端列初始化", ex);
                            WriteCrash(ex);
                        }
                    }))
                    {
                        IsBackground = true,
                        Name = "BatteryPulse dashboard host"
                            };
                            hostThread.SetApartmentState(ApartmentState.STA);
                            hostThread.Start();
                    app.Run();
                    Interlocked.Exchange(ref stopSignalThread, 1);
                    try { showSignal.Set(); } catch { }
                    try { if (signalThread.IsAlive) signalThread.Join(500); } catch { }
                    try { if (hostThread.IsAlive) hostThread.Join(500); } catch { }
                    showSignal.Dispose();
                }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.Write("頂端列外層例外", ex);
                    WriteCrash(ex);
                }
            }
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(ShowSignalName))
                    signal.Set();
            }
            catch { }
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BatteryPulse.TopBar.crash.log"), ex.ToString());
            }
            catch { }
        }

        private static string AttachToInteractiveDesktop()
        {
            try
            {
                IntPtr station = OpenWindowStation("WinSta0", false, WindowStationAccess);
                if (station == IntPtr.Zero)
                    return "station failed " + System.Runtime.InteropServices.Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                if (!SetProcessWindowStation(station))
                    return "station switch failed " + System.Runtime.InteropServices.Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);

                IntPtr desktop = OpenDesktop("Default", 0, false, DesktopAccess);
                if (desktop == IntPtr.Zero)
                    return "desktop failed " + System.Runtime.InteropServices.Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                if (!SetThreadDesktop(desktop))
                    return "desktop switch failed " + System.Runtime.InteropServices.Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                return "WinSta0\\Default";
            }
            catch (Exception ex)
            {
                return "desktop switch exception " + ex.GetType().FullName + " " + ex.Message;
            }
        }

    }
}
