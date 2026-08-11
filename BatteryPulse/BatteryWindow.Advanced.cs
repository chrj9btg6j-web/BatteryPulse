using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BatteryPulse
{
    public sealed partial class BatteryWindow
    {
        private Grid scene;
        private StackPanel compactRoot;
        private Grid advancedRoot;
        private AdvancedDashboard advancedDashboard;
        private TelemetryStore telemetryStore;
        private RollingTelemetry telemetryHistory;
        private BatterySnapshot latestSnapshot;
        private bool advancedMode;
        private bool advancedWindowed;
        private bool topBarHostMode;
        private Action topBarReturnRequested;
        private Func<Rect> topBarWorkAreaProvider;
        private Rect widgetBounds;
        private DateTime lastTelemetryWrite = DateTime.MinValue;

        internal void ConfigureTopBarHost(Action returnToTopBar)
        {
            ConfigureTopBarHost(returnToTopBar, null);
        }

        internal void ConfigureTopBarHost(Action returnToTopBar, Func<Rect> workAreaProvider)
        {
            topBarHostMode = true;
            topBarReturnRequested = returnToTopBar;
            topBarWorkAreaProvider = workAreaProvider;
            ShowInTaskbar = false;
            Topmost = false;
            Opacity = 0;
            Left = -10000;
            Top = -10000;
        }

        internal void ShutdownTopBarHost()
        {
            closing = true;
            try { Close(); } catch { }
        }

        private Border CreateAdvancedEntryButton()
        {
            var button = new Border
            {
                Width = 25,
                Height = 25,
                CornerRadius = new CornerRadius(7),
                Background = Brush("#16FFFFFF"),
                BorderBrush = Brush("#24FFFFFF"),
                BorderThickness = new Thickness(1),
                Focusable = true,
                Cursor = Cursors.Hand,
                ToolTip = "進階儀表板",
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "↗",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#FFE7F2ED"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            button.MouseEnter += delegate { button.Background = Brush("#32FFFFFF"); };
            button.MouseLeave += delegate { button.Background = Brush("#16FFFFFF"); };
            button.GotKeyboardFocus += delegate { button.BorderBrush = Brush("#BFFFFFFF"); };
            button.LostKeyboardFocus += delegate { button.BorderBrush = Brush("#24FFFFFF"); };
            button.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            button.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenAdvancedDashboard();
            };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                OpenAdvancedDashboard();
            };
            return button;
        }

        private Grid BuildAdvancedRoot()
        {
            telemetryStore = new TelemetryStore();
            telemetryHistory = new RollingTelemetry(TimeSpan.FromMinutes(30));
            advancedDashboard = new AdvancedDashboard(this, settings, telemetryStore, telemetryHistory);
            return advancedDashboard.Root;
        }

        private void InitializeAdvancedData()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                telemetryStore.PruneOldFiles();
                IList<TelemetryPoint> recent = telemetryStore.LoadRecent(TimeSpan.FromMinutes(30));
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        telemetryHistory.AddRange(recent);
                        if (advancedDashboard != null) advancedDashboard.UpdateHistory(telemetryHistory.Snapshot());
                    }), DispatcherPriority.Background);
                }
                catch { }
            });
        }

        private void RecordTelemetryAndUpdateAdvanced(BatterySnapshot data)
        {
            latestSnapshot = data;
            TelemetryPoint point = TelemetryPoint.FromSnapshot(data);
            telemetryHistory.Add(point);
            IList<TelemetryPoint> points = telemetryHistory.Snapshot();
            if (advancedDashboard != null) advancedDashboard.Update(data, points);

            if ((data.ReadAt - lastTelemetryWrite).TotalSeconds >= 5)
            {
                lastTelemetryWrite = data.ReadAt;
                ThreadPool.QueueUserWorkItem(delegate { telemetryStore.Append(point); });
            }
        }

        internal void OpenAdvancedDashboard()
        {
            if (advancedMode || !IsLoaded) return;
            bool instantOpen = topBarHostMode;
            if (instantOpen)
            {
                ShowInTaskbar = false;
                WindowState = WindowState.Normal;
                Opacity = 1;
            }
            SaveWidgetPlacement();
            advancedMode = true;
            advancedWindowed = true;
            SetAdvancedSurface(true);
            dotMesh.Visibility = Visibility.Collapsed;

            BeginAnimation(HeightProperty, null);
            if (!instantOpen)
            {
                Height = expanded ? CalculateExpandedHeight() : CollapsedHeight;
                widgetBounds = new Rect(Left, Top, 390, Height);
            }

            Topmost = false;
            if (topmostMenu != null) topmostMenu.IsChecked = false;
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
            ResizeMode = ResizeMode.NoResize;

            advancedRoot.Visibility = instantOpen ? Visibility.Collapsed : Visibility.Visible;
            advancedRoot.IsHitTestVisible = !instantOpen;
            advancedRoot.Opacity = 1;
            compactRoot.Visibility = Visibility.Collapsed;
            compactRoot.IsHitTestVisible = false;
            compactRoot.Opacity = 1;

            Rect area = GetCurrentWorkArea();
            Rect target = GetAdvancedTarget(area);
            if (instantOpen)
            {
                BeginAnimation(LeftProperty, null);
                BeginAnimation(TopProperty, null);
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                Left = target.Left;
                Top = target.Top;
                Width = target.Width;
                Height = target.Height;
                LockAdvancedBounds(target);
                Show();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!advancedMode) return;
                    advancedRoot.Visibility = Visibility.Visible;
                    advancedRoot.IsHitTestVisible = true;
                    advancedDashboard.PrepareForOpen(latestSnapshot, telemetryHistory.Snapshot());
                    advancedDashboard.FocusCurrentPage();
                }), DispatcherPriority.Loaded);
                return;
            }

            advancedDashboard.PrepareForOpen(latestSnapshot, telemetryHistory.Snapshot());
            advancedRoot.Opacity = 0;
            compactRoot.Visibility = Visibility.Visible;
            advancedRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            compactRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));

            ApplyFixedAdvancedBounds(target);
            compactRoot.Visibility = Visibility.Collapsed;
            compactRoot.Opacity = 1;
            advancedDashboard.FocusCurrentPage();
        }

        internal void ReturnToWidget()
        {
            if (!advancedMode) return;
            if (topBarHostMode)
            {
                advancedRoot.Visibility = Visibility.Collapsed;
                advancedRoot.IsHitTestVisible = false;
                advancedRoot.Opacity = 1;
                advancedMode = false;
                advancedWindowed = true;
                WindowState = WindowState.Normal;
                SetAdvancedSurface(false);
                dotMesh.Visibility = Visibility.Visible;
                Hide();
                if (topBarReturnRequested != null) topBarReturnRequested();
                return;
            }
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            UnlockAdvancedBounds();

            compactRoot.Visibility = Visibility.Visible;
            compactRoot.IsHitTestVisible = true;
            compactRoot.Opacity = 0;
            compactRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            advancedRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));

            double targetHeight = expanded ? CalculateExpandedHeight() : CollapsedHeight;
            Rect area = GetCurrentWorkArea();
            double left = Math.Max(area.Left, Math.Min(widgetBounds.Left, area.Right - 390));
            double top = Math.Max(area.Top, Math.Min(widgetBounds.Top, area.Bottom - targetHeight));
            Rect target = new Rect(left, top, 390, targetHeight);
            AnimateWindowBounds(target, 390, delegate
            {
                advancedRoot.Visibility = Visibility.Collapsed;
                advancedRoot.IsHitTestVisible = false;
                advancedRoot.Opacity = 1;
                advancedMode = false;
                advancedWindowed = true;
                Width = 390;
                MinWidth = 390;
                MaxWidth = 390;
                Height = targetHeight;
                SetAdvancedSurface(false);
                dotMesh.Visibility = Visibility.Visible;
                SaveWidgetPlacement();
            });
        }

        internal void MinimizeAdvanced()
        {
            if (topBarHostMode)
            {
                ReturnToWidget();
                return;
            }
            WindowState = WindowState.Minimized;
        }

        internal void ToggleAdvancedSize()
        {
            if (!advancedMode) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Rect area = GetCurrentWorkArea();
            advancedWindowed = !advancedWindowed;
            Rect target = GetAdvancedTarget(area);
            UnlockAdvancedBounds();
            ApplyFixedAdvancedBounds(target);
        }

        internal void DragAdvancedWindow()
        {
            if (!advancedMode || !advancedWindowed) return;
            try { DragMove(); } catch { }
        }

        private void SetAdvancedSurface(bool enabled)
        {
            if (enabled)
            {
                // The dashboard itself owns the neutral surface and rounded corners.
                Background = Brush("#FFD5D9DD");
                shell.Margin = new Thickness(0);
                shell.Padding = new Thickness(0);
                shell.CornerRadius = new CornerRadius(0);
                shell.BorderThickness = new Thickness(0);
                shell.BorderBrush = Brushes.Transparent;
                shell.Background = Brushes.Transparent;
                shell.Effect = null;
                Glass.Clear(this);
            }
            else
            {
                Background = Brushes.Transparent;
                shell.Margin = compactShellMargin;
                shell.Padding = compactShellPadding;
                shell.CornerRadius = compactShellCornerRadius;
                shell.BorderThickness = compactShellBorderThickness;
                shell.Background = compactShellBackground;
                shell.BorderBrush = compactShellBorderBrush;
                shell.Effect = compactShellEffect;
                advancedRoot.Clip = null;
                Glass.Clear(this);
            }
        }

        private void UpdateAdvancedClip()
        {
            if (advancedRoot == null || advancedRoot.ActualWidth <= 0 || advancedRoot.ActualHeight <= 0) return;
            double radius = Math.Min(28, Math.Min(advancedRoot.ActualWidth, advancedRoot.ActualHeight) / 2);
            advancedRoot.Clip = new RectangleGeometry(
                new Rect(0, 0, advancedRoot.ActualWidth, advancedRoot.ActualHeight),
                radius,
                radius);
        }

        private Rect GetAdvancedTarget(Rect area)
        {
            const double advancedAspectRatio = 1.7;
            if (!advancedWindowed)
            {
                double availableWidth = Math.Max(1, area.Width - 48);
                double availableHeight = Math.Max(1, area.Height - 48);
                double width = availableWidth;
                double height = width / advancedAspectRatio;
                if (height > availableHeight)
                {
                    height = availableHeight;
                    width = height * advancedAspectRatio;
                }
                return new Rect(
                    area.Left + (area.Width - width) / 2,
                    area.Top + (area.Height - height) / 2,
                    width,
                    height);
            }

            double windowedAvailableWidth = Math.Max(1, area.Width - 48);
            double windowedAvailableHeight = Math.Max(1, area.Height - 48);
            double windowedWidth = Math.Min(1020, windowedAvailableWidth);
            double windowedHeight = Math.Min(600, windowedAvailableHeight);
            if (windowedWidth < 720 || windowedHeight < 480)
            {
                windowedWidth = Math.Min(windowedAvailableWidth, 1020);
                windowedHeight = Math.Min(windowedAvailableHeight, 600);
            }
            return new Rect(
                area.Left + (area.Width - windowedWidth) / 2,
                area.Top + (area.Height - windowedHeight) / 2,
                windowedWidth,
                windowedHeight);
        }

        private Rect GetCurrentWorkArea()
        {
            if (topBarHostMode && topBarWorkAreaProvider != null)
            {
                try
                {
                    Rect supplied = topBarWorkAreaProvider();
                    if (supplied.Width > 0 && supplied.Height > 0) return supplied;
                }
                catch { }
            }

            Rect fallback = SystemParameters.WorkArea;
            try
            {
                Forms.Screen screen = Forms.Screen.FromPoint(Forms.Control.MousePosition);
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    Point topLeft = source.CompositionTarget.TransformFromDevice.Transform(
                        new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
                    Point bottomRight = source.CompositionTarget.TransformFromDevice.Transform(
                        new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
                    return new Rect(topLeft, bottomRight);
                }
            }
            catch { }
            return fallback;
        }

        private void AnimateWindowBounds(Rect target, int milliseconds, Action completed)
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                BeginAnimation(LeftProperty, null);
                BeginAnimation(TopProperty, null);
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                Left = target.Left;
                Top = target.Top;
                Width = target.Width;
                Height = target.Height;
                if (completed != null) completed();
                return;
            }
            var ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            TimeSpan duration = TimeSpan.FromMilliseconds(milliseconds);
            var leftAnimation = new DoubleAnimation(Left, target.Left, duration) { EasingFunction = ease };
            var topAnimation = new DoubleAnimation(Top, target.Top, duration) { EasingFunction = ease };
            var widthAnimation = new DoubleAnimation(ActualWidth > 0 ? ActualWidth : Width, target.Width, duration) { EasingFunction = ease };
            var heightAnimation = new DoubleAnimation(ActualHeight > 0 ? ActualHeight : Height, target.Height, duration) { EasingFunction = ease };
            widthAnimation.Completed += delegate
            {
                BeginAnimation(LeftProperty, null);
                BeginAnimation(TopProperty, null);
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                Left = target.Left;
                Top = target.Top;
                Width = target.Width;
                Height = target.Height;
                if (completed != null) completed();
            };
            BeginAnimation(LeftProperty, leftAnimation);
            BeginAnimation(TopProperty, topAnimation);
            BeginAnimation(WidthProperty, widthAnimation);
            BeginAnimation(HeightProperty, heightAnimation);
        }

        private void ApplyFixedAdvancedBounds(Rect target)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Left = target.Left;
            Top = target.Top;
            Width = target.Width;
            Height = target.Height;
            LockAdvancedBounds(target);
        }

        private void LockAdvancedBounds(Rect target)
        {
            MinWidth = target.Width;
            MaxWidth = target.Width;
            MinHeight = target.Height;
            MaxHeight = target.Height;
        }

        private void UnlockAdvancedBounds()
        {
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
        }

        private void RestoreWidgetPlacement()
        {
            Rect area = SystemParameters.WorkArea;
            double left = settings.HasWindowPosition ? settings.WindowLeft : area.Right - Width - 18;
            double top = settings.HasWindowPosition ? settings.WindowTop : area.Top + 18;
            left = Math.Max(area.Left, Math.Min(left, area.Right - Width));
            top = Math.Max(area.Top, Math.Min(top, area.Bottom - Height));
            Left = left;
            Top = top;
        }

        private void SaveWidgetPlacement()
        {
            if (advancedMode || topBarHostMode || WindowState != WindowState.Normal) return;
            settings.HasWindowPosition = true;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WidgetExpanded = expanded;
            settings.Save();
        }

        private void OnAdvancedWindowClosing()
        {
            if (!advancedMode && !topBarHostMode) SaveWidgetPlacement();
            settings.Save();
        }

        internal void DashboardRefresh()
        {
            RefreshBattery(true);
        }

        internal void DashboardSetTextShadow(bool enabled)
        {
            settings.TextShadow = enabled;
            settings.Save();
            ApplyTextShadow();
        }

        internal void DashboardSetTopmost(bool enabled)
        {
            Topmost = enabled;
            if (topmostMenu != null) topmostMenu.IsChecked = enabled;
        }

        internal void DashboardSetStartup(bool enabled)
        {
            try
            {
                StartupManager.Set(enabled);
                if (startupMenu != null) startupMenu.IsChecked = enabled;
            }
            catch
            {
                MessageBox.Show("無法更新開機啟動設定。", "Battery Pulse", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        internal void DashboardResetEnergy()
        {
            ResetEnergy(true, true);
            if (latestSnapshot != null) advancedDashboard.Update(latestSnapshot, telemetryHistory.Snapshot());
        }
    }

    public sealed class TelemetryPoint
    {
        public DateTime At;
        public double? BatteryPercent;
        public double? BatteryWatts;
        public string BatteryMode;
        public double? SystemWatts;
        public double? BatteryTempC;
        public double? CpuTempC;
        public string CpuSource;
        public double? GpuTempC;
        public string GpuSource;
        public string GpuStatus;
        public bool IsAcLine;
        public string ChargerType;
        public string ChargerTypeSource;

        public static TelemetryPoint FromSnapshot(BatterySnapshot data)
        {
            return new TelemetryPoint
            {
                At = data.ReadAt,
                BatteryPercent = data.Percent,
                BatteryWatts = data.Watts,
                BatteryMode = data.BatteryPowerMode,
                SystemWatts = data.SystemWatts,
                BatteryTempC = data.BatteryTempC,
                CpuTempC = data.CpuTempC,
                CpuSource = data.CpuTempSource,
                GpuTempC = data.GpuTempC,
                GpuSource = data.GpuTempSource,
                GpuStatus = data.GpuStatus,
                IsAcLine = data.IsAcLine,
                ChargerType = data.ChargerType,
                ChargerTypeSource = data.ChargerTypeSource
            };
        }

        public string ToCsv()
        {
            return string.Join(",", new[]
            {
                Csv(At.ToString("o", CultureInfo.InvariantCulture)),
                Number(BatteryPercent),
                Number(BatteryWatts),
                Csv(BatteryMode),
                Number(SystemWatts),
                Number(BatteryTempC),
                Number(CpuTempC),
                Csv(CpuSource),
                Number(GpuTempC),
                Csv(GpuSource),
                Csv(GpuStatus),
                IsAcLine ? "1" : "0",
                Csv(ChargerType),
                Csv(ChargerTypeSource)
            });
        }

        public static TelemetryPoint Parse(string line)
        {
            List<string> cells = SplitCsv(line);
            if (cells.Count < 12) return null;
            DateTime at;
            if (!DateTime.TryParse(cells[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at)) return null;
            return new TelemetryPoint
            {
                At = at,
                BatteryPercent = ParseNumber(cells[1]),
                BatteryWatts = ParseNumber(cells[2]),
                BatteryMode = cells[3],
                SystemWatts = ParseNumber(cells[4]),
                BatteryTempC = ParseNumber(cells[5]),
                CpuTempC = ParseNumber(cells[6]),
                CpuSource = cells[7],
                GpuTempC = ParseNumber(cells[8]),
                GpuSource = cells[9],
                GpuStatus = cells[10],
                IsAcLine = cells[11] == "1",
                ChargerType = cells.Count > 12 && !string.IsNullOrWhiteSpace(cells[12]) ? cells[12] : "未知",
                ChargerTypeSource = cells.Count > 13 && !string.IsNullOrWhiteSpace(cells[13]) ? cells[13] : "舊版資料未記錄"
            };
        }

        private static string Number(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static double? ParseNumber(string value)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? (double?)parsed : null;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static List<string> SplitCsv(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    result.Add(current.ToString());
                    current.Length = 0;
                }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }
    }

    public sealed class RollingTelemetry
    {
        private readonly object sync = new object();
        private readonly List<TelemetryPoint> points = new List<TelemetryPoint>();
        private readonly TimeSpan range;

        public RollingTelemetry(TimeSpan rangeValue)
        {
            range = rangeValue;
        }

        public void Add(TelemetryPoint point)
        {
            if (point == null) return;
            lock (sync)
            {
                points.Add(point);
                Prune(DateTime.Now - range);
            }
        }

        public void AddRange(IEnumerable<TelemetryPoint> values)
        {
            if (values == null) return;
            lock (sync)
            {
                points.AddRange(values.Where(delegate(TelemetryPoint p) { return p != null; }));
                points.Sort(delegate(TelemetryPoint a, TelemetryPoint b) { return a.At.CompareTo(b.At); });
                Prune(DateTime.Now - range);
            }
        }

        public IList<TelemetryPoint> Snapshot()
        {
            lock (sync) return points.ToList();
        }

        private void Prune(DateTime cutoff)
        {
            points.RemoveAll(delegate(TelemetryPoint p) { return p.At < cutoff; });
        }
    }

    public sealed class DailyTelemetrySummary
    {
        public DateTime Date;
        public string FilePath;
        public int Samples;
        public double? AverageSystemWatts;
        public double? MaxCpuTempC;
        public double? MaxGpuTempC;
        public double EnergyWh;
    }

    public sealed class TelemetryStore
    {
        public const int RetentionDays = 7;
        private const string Header = "timestamp,battery_percent,battery_watts,battery_mode,system_watts,battery_temp_c,cpu_temp_c,cpu_source,gpu_temp_c,gpu_source,gpu_status,ac_line";
        private readonly object fileSync = new object();

        public void Append(TelemetryPoint point)
        {
            if (point == null) return;
            lock (fileSync)
            {
                try
                {
                    PruneOldFilesCore();
                    string path = Path.Combine(AppSettings.HistoryDirectory, point.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
                    if (!File.Exists(path)) File.WriteAllText(path, Header + Environment.NewLine, new UTF8Encoding(true));
                    File.AppendAllText(path, point.ToCsv() + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            }
        }

        public void PruneOldFiles()
        {
            lock (fileSync) PruneOldFilesCore();
        }

        private void PruneOldFilesCore()
        {
            try
            {
                DateTime cutoff = DateTime.Today.AddDays(-(RetentionDays - 1));
                foreach (string path in Directory.GetFiles(AppSettings.HistoryDirectory, "*.csv"))
                {
                    DateTime date;
                    if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date < cutoff)
                        File.Delete(path);
                }
            }
            catch { }
        }

        public IList<TelemetryPoint> LoadRecent(TimeSpan range)
        {
            var result = new List<TelemetryPoint>();
            DateTime cutoff = DateTime.Now - range;
            lock (fileSync)
            {
                try
                {
                    foreach (string path in Directory.GetFiles(AppSettings.HistoryDirectory, "*.csv"))
                    {
                        DateTime fileDate;
                        if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fileDate)) continue;
                        if (fileDate.Date < cutoff.Date) continue;
                        foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
                        {
                            TelemetryPoint point = TelemetryPoint.Parse(line);
                            if (point != null && point.At >= cutoff) result.Add(point);
                        }
                    }
                }
                catch { }
            }
            result.Sort(delegate(TelemetryPoint a, TelemetryPoint b) { return a.At.CompareTo(b.At); });
            return result;
        }

        public IList<DailyTelemetrySummary> GetDailySummaries()
        {
            var result = new List<DailyTelemetrySummary>();
            lock (fileSync)
            {
                PruneOldFilesCore();
                try
                {
                    foreach (string path in Directory.GetFiles(AppSettings.HistoryDirectory, "*.csv").OrderByDescending(delegate(string p) { return p; }))
                    {
                        DateTime date;
                        if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                        var points = new List<TelemetryPoint>();
                        foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
                        {
                            TelemetryPoint point = TelemetryPoint.Parse(line);
                            if (point != null) points.Add(point);
                        }
                        result.Add(Summarize(date, path, points));
                    }
                }
                catch { }
            }
            return result;
        }

        public bool Export(string sourcePath, string destinationPath)
        {
            lock (fileSync)
            {
                try
                {
                    File.Copy(sourcePath, destinationPath, true);
                    return true;
                }
                catch { return false; }
            }
        }

        private static DailyTelemetrySummary Summarize(DateTime date, string path, IList<TelemetryPoint> points)
        {
            var summary = new DailyTelemetrySummary { Date = date, FilePath = path, Samples = points.Count };
            var watts = points.Where(delegate(TelemetryPoint p) { return p.SystemWatts.HasValue; }).Select(delegate(TelemetryPoint p) { return p.SystemWatts.Value; }).ToList();
            var cpu = points.Where(delegate(TelemetryPoint p) { return p.CpuTempC.HasValue; }).Select(delegate(TelemetryPoint p) { return p.CpuTempC.Value; }).ToList();
            var gpu = points.Where(delegate(TelemetryPoint p) { return p.GpuTempC.HasValue; }).Select(delegate(TelemetryPoint p) { return p.GpuTempC.Value; }).ToList();
            if (watts.Count > 0) summary.AverageSystemWatts = watts.Average();
            if (cpu.Count > 0) summary.MaxCpuTempC = cpu.Max();
            if (gpu.Count > 0) summary.MaxGpuTempC = gpu.Max();

            for (int i = 1; i < points.Count; i++)
            {
                if (!points[i - 1].SystemWatts.HasValue || !points[i].SystemWatts.HasValue) continue;
                double seconds = (points[i].At - points[i - 1].At).TotalSeconds;
                if (seconds <= 0 || seconds > 15) continue;
                summary.EnergyWh += ((points[i - 1].SystemWatts.Value + points[i].SystemWatts.Value) / 2.0) * seconds / 3600.0;
            }
            return summary;
        }
    }
}
