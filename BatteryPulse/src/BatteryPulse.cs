using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;

[assembly: AssemblyTitle("Battery Pulse")]
[assembly: AssemblyProduct("Battery Pulse")]
[assembly: AssemblyDescription("Battery, power and temperature desktop dashboard")]
[assembly: AssemblyCompany("彰化的驕傲")]
[assembly: AssemblyCopyright("Copyright © 彰化的驕傲 2026")]
[assembly: AssemblyVersion("2.2.3.0")]
[assembly: AssemblyFileVersion("2.2.3.0")]

namespace BatteryPulse
{
    public sealed partial class BatteryWindow : Window
    {
        private const double CollapsedHeight = 194;
        private const double ExpandedHeight = 642;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer updateTimer;
        private readonly BatteryReader reader;
        private readonly Border shell;
        private readonly Brush compactShellBackground;
        private readonly Brush compactShellBorderBrush;
        private readonly Thickness compactShellBorderThickness;
        private readonly Effect compactShellEffect;
        private readonly Thickness compactShellMargin;
        private readonly Thickness compactShellPadding;
        private readonly CornerRadius compactShellCornerRadius;
        private readonly Grid header;
        private readonly ScaleTransform shellScale;
        private readonly ReactiveDotMesh dotMesh;
        private readonly AppSettings settings;
        private StackPanel details;
        private TextBlock titleText;
        private TextBlock statusPill;
        private TextBlock syncLabel;
        private TextBlock heroPercent;
        private TextBlock heroLabel;
        private TextBlock wattsValue;
        private TextBlock systemWattsValue;
        private TextBlock dayEnergyValue;
        private TextBlock monthEnergyValue;
        private TextBlock voltageValue;
        private TextBlock healthValue;
        private TextBlock rateValue;
        private TextBlock chevron;
        private TextBlock refreshGlyph;
        private MeterBar batteryBar;
        private MetricRow wattsRow;
        private MetricRow systemWattsRow;
        private MetricRow cpuTempRow;
        private MetricRow gpuTempRow;
        private TempScale cpuTempScale;
        private MenuItem topmostMenu;
        private MenuItem startupMenu;
        private MenuItem shadowMenu;
        private bool expanded;
        private bool dragging;
        private bool closing;
        private int refreshInProgress;
        private Point mouseDownPoint;
        private double? smoothedSystemWatts;
        private DateTime lastSettingsSaveAt = DateTime.MinValue;
        private int updateCheckInProgress;
        private UpdateInfo latestUpdateInfo;
        internal event Action<BatterySnapshot> SnapshotUpdated;

        public BatteryWindow()
        {
            Title = "Battery Pulse";
            Width = 390;
            Height = CollapsedHeight;
            MinWidth = 390;
            MaxWidth = 390;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            SnapsToDevicePixels = true;

            settings = AppSettings.Load();
            BatteryLimitController.RestoreLastApplied(settings.BatteryLimitHasApplied ? (int?)settings.BatteryLimitPercent : null);
            reader = new BatteryReader();
            shellScale = new ScaleTransform(1, 1);

            shell = new Border
            {
                Margin = new Thickness(10),
                Padding = new Thickness(20, 18, 20, 18),
                CornerRadius = new CornerRadius(28),
                BorderThickness = new Thickness(1),
                BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(150, 255, 255, 255),
                    Color.FromArgb(38, 185, 216, 204),
                    new Point(0, 0), new Point(1, 1)),
                Background = new LinearGradientBrush(
                    Color.FromArgb(218, 64, 82, 75),
                    Color.FromArgb(190, 105, 124, 116),
                    new Point(0, 0), new Point(1, 1)),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(12, 18, 16),
                    BlurRadius = 38,
                    ShadowDepth = 12,
                    Opacity = 0.24
                },
                RenderTransform = shellScale,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            compactShellBackground = shell.Background;
            compactShellBorderBrush = shell.BorderBrush;
            compactShellBorderThickness = shell.BorderThickness;
            compactShellEffect = shell.Effect;
            compactShellMargin = shell.Margin;
            compactShellPadding = shell.Padding;
            compactShellCornerRadius = shell.CornerRadius;

            scene = new Grid { ClipToBounds = true };
            compactRoot = new StackPanel();

            dotMesh = new ReactiveDotMesh { IsHitTestVisible = false };
            scene.Children.Add(dotMesh);
            scene.Children.Add(compactRoot);
            scene.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(42, 0, 42, 0),
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(1),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Colors.Transparent, 0),
                        new GradientStop(Color.FromArgb(170, 255, 255, 255), 0.5),
                        new GradientStop(Colors.Transparent, 1)
                    }
                },
                IsHitTestVisible = false
            });
            advancedRoot = BuildAdvancedRoot();
            advancedRoot.Visibility = Visibility.Collapsed;
            advancedRoot.SizeChanged += delegate { UpdateAdvancedClip(); };
            scene.Children.Add(advancedRoot);
            shell.Child = scene;
            Content = shell;

            header = BuildHeader();
            compactRoot.Children.Add(header);
            compactRoot.Children.Add(BuildCompactSummary());
            details = BuildDetails();
            details.Visibility = Visibility.Collapsed;
            compactRoot.Children.Add(details);
            shell.ContextMenu = BuildMenu();

            header.MouseLeftButtonDown += HeaderMouseDown;
            header.MouseMove += HeaderMouseMove;
            header.MouseLeftButtonUp += HeaderMouseUp;
            shell.MouseEnter += delegate
            {
                AnimateShellShadow(0.32, 28, 180);
            };
            shell.MouseLeave += delegate
            {
                AnimateShellShadow(0.24, 38, 260);
                dotMesh.ClearFocus();
            };
            shell.MouseMove += LiquidMouseMove;

            Loaded += OnLoaded;
            Closing += delegate(object sender, CancelEventArgs e)
            {
                if (topBarHostMode && advancedMode && !closing)
                {
                    e.Cancel = true;
                    ReturnToWidget();
                    return;
                }
                closing = true;
                SaveWidgetPlacement();
                OnAdvancedWindowClosing();
            };
            Closed += delegate
            {
                if (refreshTimer != null) refreshTimer.Stop();
                if (updateTimer != null) updateTimer.Stop();
            };

            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            refreshTimer.Tick += delegate { RefreshBattery(false); };
            // 啟動時會立即檢查；後續每 12 小時檢查一次，避免背景網路與 CPU 使用過於頻繁。
            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(12) };
            updateTimer.Tick += delegate { CheckForUpdates(); };
            ApplyTextShadow();
        }

        private Grid BuildHeader()
        {
            var grid = new Grid { Height = 58, Cursor = Cursors.Hand };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            var logo = new Border
            {
                Width = 42,
                Height = 42,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("#55FFFFFF"),
                Background = new LinearGradientBrush(Brush("#FFBCEFD8").Color, Brush("#FF7EA7D8").Color, 45),
                Child = new TextBlock
                {
                    Text = "⚡",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#FF173028"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            grid.Children.Add(logo);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleLine = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            titleText = new TextBlock
            {
                Text = settings.CustomTitle,
                Foreground = Brush("#FFF6FAF7"),
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold
            };
            statusPill = new TextBlock
            {
                Text = "SYNC",
                Margin = new Thickness(8, 1, 0, 0),
                Foreground = Brush("#FFBDEDD8"),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold
            };
            titleLine.Children.Add(titleText);
            titleLine.Children.Add(statusPill);
            titleStack.Children.Add(titleLine);
            syncLabel = new TextBlock
            {
                Text = "等待電池資料",
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = Brush("#FFB0BDB5"),
                FontSize = 10
            };
            titleStack.Children.Add(syncLabel);
            Grid.SetColumn(titleStack, 1);
            grid.Children.Add(titleStack);

            var heroStack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            heroPercent = new TextBlock
            {
                Text = "--%",
                Foreground = Brushes.White,
                FontSize = 27,
                FontWeight = FontWeights.Light,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            heroLabel = new TextBlock
            {
                Text = "電池電量",
                Foreground = Brush("#FFA9B5AE"),
                FontSize = 9,
                Margin = new Thickness(0, -1, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            heroStack.Children.Add(heroPercent);
            heroStack.Children.Add(heroLabel);
            Grid.SetColumn(heroStack, 2);
            grid.Children.Add(heroStack);

            Border advancedButton = CreateAdvancedEntryButton();
            Grid.SetColumn(advancedButton, 3);
            grid.Children.Add(advancedButton);

            chevron = new TextBlock
            {
                Text = "⌄",
                Foreground = Brush("#FFC2CEC7"),
                FontSize = 17,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };
            Grid.SetColumn(chevron, 4);
            grid.Children.Add(chevron);
            return grid;
        }

        private static Border BuildDotMesh()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(42, 214, 244, 225)),
                null,
                new EllipseGeometry(new Point(2.2, 2.2), 1.05, 1.05)));
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                null,
                new EllipseGeometry(new Point(11.5, 9.5), 0.75, 0.75)));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 18, 18),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
                Opacity = 0.55
            };
            brush.Freeze();
            return new Border
            {
                Background = brush,
                Opacity = 0.72,
                IsHitTestVisible = false
            };
        }

        private Grid BuildCompactSummary()
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 66 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            grid.Children.Add(CompactCell("電池功率", out wattsValue));
            var line = new Border { Width = 1, Height = 58, Background = Brush("#24FFFFFF"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRowSpan(line, 3);
            Grid.SetColumn(line, 1);
            grid.Children.Add(line);
            var right = CompactCell("電腦耗電", out systemWattsValue);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            var day = CompactCell("今日耗能", out dayEnergyValue);
            Grid.SetRow(day, 2);
            grid.Children.Add(day);
            var month = CompactCell("本月耗能", out monthEnergyValue);
            Grid.SetRow(month, 2);
            Grid.SetColumn(month, 2);
            grid.Children.Add(month);
            return grid;
        }

        private StackPanel BuildDetails()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(new Border { Height = 1, Background = Brush("#20FFFFFF"), Margin = new Thickness(0, 0, 0, 9) });
            batteryBar = new MeterBar("電池電量", "#FF54D4B1");
            panel.Children.Add(batteryBar.Root);

            wattsRow = new MetricRow("電池功率", "#FFD7F3E2");
            systemWattsRow = new MetricRow("電腦耗電", "#FFBCEFD8");
            cpuTempRow = new MetricRow("CPU 溫度", "#FFDDEFE8");
            gpuTempRow = new MetricRow("NVIDIA 溫度", "#FFDDEFE8");
            panel.Children.Add(wattsRow.Root);
            panel.Children.Add(systemWattsRow.Root);
            panel.Children.Add(cpuTempRow.Root);
            panel.Children.Add(gpuTempRow.Root);
            cpuTempScale = new TempScale();
            panel.Children.Add(cpuTempScale.Root);

            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 33 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            voltageValue = MiniValue("電壓", grid, 0);
            healthValue = MiniValue("狀態", grid, 1);
            panel.Children.Add(grid);

            var footer = new Grid { Margin = new Thickness(0, 11, 0, 0), Height = 25 };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            rateValue = new TextBlock
            {
                Text = "1 秒背景更新",
                Foreground = Brush("#FF66736E"),
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            footer.Children.Add(rateValue);
            var refreshButton = new Border
            {
                Width = 25,
                Height = 25,
                CornerRadius = new CornerRadius(9),
                Background = Brush("#16FFFFFF"),
                Cursor = Cursors.Hand,
                ToolTip = "立即更新"
            };
            refreshGlyph = new TextBlock
            {
                Text = "↻",
                Foreground = Brush("#FFC8D2CE"),
                FontSize = 15,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };
            refreshButton.Child = refreshGlyph;
            refreshButton.MouseLeftButtonUp += delegate { RefreshBattery(true); };
            Grid.SetColumn(refreshButton, 1);
            footer.Children.Add(refreshButton);
            panel.Children.Add(footer);
            return panel;
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu
            {
                Background = Brush("#F3161B22"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#35FFFFFF"),
                Padding = new Thickness(4)
            };
            menu.Items.Add(MenuItem("立即更新", delegate { RefreshBattery(true); }));
            menu.Items.Add(MenuItem("開啟進階儀表板", delegate { OpenAdvancedDashboard(); }));
            menu.Items.Add(MenuItem("自訂顯示文字", delegate { RenameTitle(); }));
            menu.Items.Add(MenuItem("重置今日耗能", delegate { ResetEnergy(true, false); }));
            menu.Items.Add(MenuItem("重置本月耗能", delegate { ResetEnergy(false, true); }));
            menu.Items.Add(MenuItem("重置全部耗能", delegate { ResetEnergy(true, true); }));
            shadowMenu = MenuItem("背景深淺自動文字陰影", delegate
            {
                settings.TextShadow = !settings.TextShadow;
                settings.Save();
                ApplyTextShadow();
            });
            shadowMenu.IsCheckable = true;
            shadowMenu.IsChecked = settings.TextShadow;
            menu.Items.Add(shadowMenu);
            topmostMenu = MenuItem("永遠置頂", delegate
            {
                Topmost = !Topmost;
                topmostMenu.IsChecked = Topmost;
            });
            topmostMenu.IsCheckable = true;
            topmostMenu.IsChecked = Topmost;
            menu.Items.Add(topmostMenu);
            startupMenu = MenuItem("開機啟動", ToggleStartup);
            startupMenu.IsCheckable = true;
            startupMenu.IsChecked = StartupManager.IsEnabled();
            menu.Items.Add(startupMenu);
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("離開 Battery Pulse", delegate { Close(); }));
            return menu;
        }

        private static TextBlock MiniValue(string label, Grid parent, int col)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#FF6E7B76"), FontSize = 9.5 });
            var value = new TextBlock
            {
                Text = "--",
                Foreground = Brush("#FFE9EFED"),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(value);
            Grid.SetColumn(stack, col);
            parent.Children.Add(stack);
            return value;
        }

        private static StackPanel CompactCell(string label, out TextBlock value)
        {
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush("#FF7E8985"),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            value = new TextBlock
            {
                Text = "--",
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brush("#FFE9EFED"),
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(value);
            return panel;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            expanded = settings.WidgetExpanded;
            details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            Height = expanded ? CalculateExpandedHeight() : CollapsedHeight;
            ((RotateTransform)chevron.RenderTransform).Angle = expanded ? 180 : 0;
            if (!topBarHostMode) RestoreWidgetPlacement();
            Opacity = 0;
            shellScale.ScaleX = shellScale.ScaleY = 1.0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            InitializeAdvancedData();
            RefreshBattery(false);
            refreshTimer.Start();
            CheckForUpdates();
            updateTimer.Start();
        }

        private void CheckForUpdates()
        {
            if (Interlocked.CompareExchange(ref updateCheckInProgress, 1, 0) != 0) return;
            UpdateService.CheckAsync(settings.UpdateApiUrl, settings.UpdatePageUrl, delegate(UpdateInfo info)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Interlocked.Exchange(ref updateCheckInProgress, 0);
                        latestUpdateInfo = info;
                        if (advancedDashboard != null) advancedDashboard.UpdateUpdateStatus(info);
                    }), DispatcherPriority.Background);
                }
                catch
                {
                    Interlocked.Exchange(ref updateCheckInProgress, 0);
                }
            });
        }

        private void RefreshBattery(bool animateRefresh)
        {
            if (animateRefresh && refreshGlyph != null)
            {
                var rotate = (RotateTransform)refreshGlyph.RenderTransform;
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(520))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            }

            if (Interlocked.CompareExchange(ref refreshInProgress, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                BatterySnapshot data = null;
                try { data = reader.Read(); }
                catch (Exception ex) { RuntimeDiagnostics.Write("背景讀取硬體快照", ex); }

                if (closing)
                {
                    Interlocked.Exchange(ref refreshInProgress, 0);
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Interlocked.Exchange(ref refreshInProgress, 0);
                        if (!closing && data != null)
                        {
                            // Switching ASUS power modes can briefly invalidate a
                            // sensor or GPU object. A single incomplete frame must
                            // not take down the WPF dispatcher or close the app.
                            try { ApplySnapshot(data); }
                            catch (Exception ex) { RuntimeDiagnostics.Write("套用硬體快照", ex); }
                        }
                    }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref refreshInProgress, 0);
                    RuntimeDiagnostics.Write("排程快照更新", ex);
                }
            });
        }

        private void ApplySnapshot(BatterySnapshot data)
        {
            ApplySystemWattsSmoothing(data);
            AccumulateEnergy(data);
            heroPercent.Text = data.Percent.HasValue ? Math.Round(data.Percent.Value).ToString("0", CultureInfo.InvariantCulture) + "%" : "--%";
            heroPercent.Foreground = BatteryBrush(data.Percent);
            heroLabel.Text = data.IsCharging ? "充電中" : data.IsAcLine ? "接上電源" : "電池電量";
            statusPill.Text = data.StatusText.ToUpperInvariant();
            statusPill.Foreground = data.IsCharging ? Brush("#FF7ABEFF") : Brush("#FF9EDCCB");
            syncLabel.Text = "更新於 " + data.ReadAt.ToString("HH:mm:ss");
            syncLabel.Foreground = Brush("#FF82918B");

            wattsValue.Text = FormatBatteryWatts(data);
            systemWattsValue.Text = FormatWatts(data.SystemWatts);
            dayEnergyValue.Text = FormatEnergy(settings.DayWh);
            monthEnergyValue.Text = FormatEnergy(settings.MonthWh);
            voltageValue.Text = data.VoltageMv.HasValue ? (data.VoltageMv.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V" : "N/A";
            healthValue.Text = data.StatusText;
            rateValue.Text = data.SourceNote;

            batteryBar.Set(data.Percent, data.StatusText);
            wattsRow.Root.Visibility = HasPositive(data.Watts) ? Visibility.Visible : Visibility.Collapsed;
            systemWattsRow.Root.Visibility = HasPositive(data.SystemWatts) ? Visibility.Visible : Visibility.Collapsed;
            cpuTempRow.Root.Visibility = HasPositive(data.CpuTempC) ? Visibility.Visible : Visibility.Collapsed;
            cpuTempScale.Root.Visibility = HasPositive(data.CpuTempC) || HasPositive(data.GpuTempC) ? Visibility.Visible : Visibility.Collapsed;
            gpuTempRow.Root.Visibility = HasPositive(data.GpuTempC) ? Visibility.Visible : Visibility.Collapsed;
            wattsRow.Set(FormatBatteryWatts(data), data.Watts.HasValue && data.Watts.Value > 0 ? Math.Min(100, Math.Abs(data.Watts.Value) * 4) : 0);
            systemWattsRow.Set(FormatWatts(data.SystemWatts), data.SystemWatts.HasValue && data.SystemWatts.Value > 0 ? Math.Min(100, Math.Abs(data.SystemWatts.Value) * 2) : 0);
            cpuTempRow.Set(FormatTemp(data.CpuTempC), TempScore(data.CpuTempC));
            gpuTempRow.Set(FormatTemp(data.GpuTempC), TempScore(data.GpuTempC));
            cpuTempScale.Set(data.CpuTempC, data.CpuTempSource, data.GpuTempC, data.GpuTempSource);
            RecordTelemetryAndUpdateAdvanced(data);
            try
            {
                if (SnapshotUpdated != null) SnapshotUpdated(data);
            }
            catch (Exception ex) { RuntimeDiagnostics.Write("通知頂端狀態列", ex); }
            AdjustExpandedHeight();
        }

        private static bool HasPositive(double? value)
        {
            return value.HasValue && value.Value > 0;
        }

        private void AdjustExpandedHeight()
        {
            if (!expanded) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    details.Measure(new Size(Width - 56, double.PositiveInfinity));
                    double target = Math.Max(CollapsedHeight, 154 + details.DesiredSize.Height);
                    BeginAnimation(HeightProperty,
                        new DoubleAnimation(Height, target, TimeSpan.FromMilliseconds(220))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.Write("調整小工具高度", ex);
                }
            }), DispatcherPriority.Loaded);
        }

        private void ApplySystemWattsSmoothing(BatterySnapshot data)
        {
            if (!data.SystemWatts.HasValue || data.SystemWatts.Value <= 0)
            {
                smoothedSystemWatts = null;
                data.SystemWatts = null;
                return;
            }
            if (!smoothedSystemWatts.HasValue)
                smoothedSystemWatts = data.SystemWatts.Value;
            else
                smoothedSystemWatts = smoothedSystemWatts.Value * 0.65 + data.SystemWatts.Value * 0.35;
            data.SystemWatts = smoothedSystemWatts.Value;
        }

        private void AccumulateEnergy(BatterySnapshot data)
        {
            DateTime now = DateTime.Now;
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (settings.DayKey != today)
            {
                settings.DayKey = today;
                settings.DayWh = 0;
            }
            if (settings.MonthKey != month)
            {
                settings.MonthKey = month;
                settings.MonthWh = 0;
            }

            if (settings.LastEnergyAt != DateTime.MinValue && data.SystemWatts.HasValue && data.SystemWatts.Value > 0)
            {
                double seconds = Math.Max(0, Math.Min(30, (now - settings.LastEnergyAt).TotalSeconds));
                double wh = data.SystemWatts.Value * seconds / 3600.0;
                settings.DayWh += wh;
                settings.MonthWh += wh;
            }
            settings.LastEnergyAt = now;
            if ((now - lastSettingsSaveAt).TotalSeconds >= 10)
            {
                settings.Save();
                lastSettingsSaveAt = now;
            }
        }

        private void RenameTitle()
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("輸入要顯示在浮窗上的文字：", "Battery Pulse", settings.CustomTitle);
            if (string.IsNullOrWhiteSpace(input)) return;
            settings.CustomTitle = input.Trim();
            settings.Save();
            titleText.Text = settings.CustomTitle;
        }

        private void ResetEnergy(bool day, bool month)
        {
            DateTime now = DateTime.Now;
            if (day)
            {
                settings.DayKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                settings.DayWh = 0;
                dayEnergyValue.Text = FormatEnergy(0);
            }
            if (month)
            {
                settings.MonthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                settings.MonthWh = 0;
                monthEnergyValue.Text = FormatEnergy(0);
            }
            settings.LastEnergyAt = now;
            settings.Save();
        }

        private void ApplyTextShadow()
        {
            Effect shadow = settings.TextShadow
                ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.82 }
                : null;
            ApplyTextEffect(this, shadow);
            if (shadowMenu != null) shadowMenu.IsChecked = settings.TextShadow;
        }

        private static void ApplyTextEffect(DependencyObject root, Effect effect)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                TextBlock text = child as TextBlock;
                if (text != null) text.Effect = effect;
                ApplyTextEffect(child, effect);
            }
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            settings.WidgetExpanded = expanded;
            settings.Save();
            if (expanded) details.Visibility = Visibility.Visible;
            double target = expanded ? CalculateExpandedHeight() : CollapsedHeight;
            var ease = expanded
                ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.34 }
                : new CubicEase { EasingMode = EasingMode.EaseInOut };
            var animation = new DoubleAnimation(Height, target, TimeSpan.FromMilliseconds(expanded ? 390 : 300)) { EasingFunction = ease };
            if (!expanded) animation.Completed += delegate { details.Visibility = Visibility.Collapsed; };
            BeginAnimation(HeightProperty, animation);
            var turn = new DoubleAnimation(expanded ? 0 : 180, expanded ? 180 : 0, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 } };
            ((RotateTransform)chevron.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, turn);
            if (expanded) AdjustExpandedHeight();
        }

        private double CalculateExpandedHeight()
        {
            details.Measure(new Size(Width - 56, double.PositiveInfinity));
            return Math.Max(CollapsedHeight, 154 + details.DesiredSize.Height);
        }

        private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            mouseDownPoint = e.GetPosition(this);
            dragging = false;
            header.CaptureMouse();
        }

        private void HeaderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !header.IsMouseCaptured) return;
            Point now = e.GetPosition(this);
            if (!dragging && (Math.Abs(now.X - mouseDownPoint.X) > 5 || Math.Abs(now.Y - mouseDownPoint.Y) > 5))
            {
                dragging = true;
                header.ReleaseMouseCapture();
                try { DragMove(); } catch { }
            }
        }

        private void HeaderMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
            if (!dragging) ToggleExpanded();
            else SaveWidgetPlacement();
        }

        private void AnimateScale(double value, int milliseconds, IEasingFunction easing)
        {
            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
            shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private void AnimateShellShadow(double opacity, double blur, int milliseconds)
        {
            DropShadowEffect shadow = shell.Effect as DropShadowEffect;
            if (shadow == null) return;
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
            shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                new DoubleAnimation(blur, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        }

        private void LiquidMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Point p = e.GetPosition(shell);
            dotMesh.SetFocus(p);
        }

        private void ToggleStartup(object sender, RoutedEventArgs e)
        {
            bool enable = !StartupManager.IsEnabled();
            try
            {
                StartupManager.Set(enable);
                startupMenu.IsChecked = enable;
            }
            catch
            {
                startupMenu.IsChecked = StartupManager.IsEnabled();
                MessageBox.Show("無法更新開機啟動設定。", "Battery Pulse", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string FormatWatts(double? watts)
        {
            if (!watts.HasValue || watts.Value <= 0) return "--";
            return watts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W";
        }

        private static string FormatBatteryWatts(BatterySnapshot data)
        {
            if (!data.Watts.HasValue || data.Watts.Value <= 0) return "--";
            string prefix = string.IsNullOrEmpty(data.BatteryPowerMode) ? "電池" : data.BatteryPowerMode;
            return prefix + " " + data.Watts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W";
        }

        private static string FormatEnergy(double wh)
        {
            if (wh >= 1000) return (wh / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " kWh";
            return wh.ToString("0.0", CultureInfo.InvariantCulture) + " Wh";
        }

        private static string FormatTemp(double? temp)
        {
            if (!temp.HasValue || temp.Value <= 0) return "--";
            return temp.Value.ToString("0", CultureInfo.InvariantCulture) + " °C";
        }

        private static double TempScore(double? temp)
        {
            if (!temp.HasValue) return 0;
            return Math.Max(0, Math.Min(100, (temp.Value - 20) * 1.6));
        }

        private static Brush BatteryBrush(double? percent)
        {
            if (!percent.HasValue) return Brush("#FFE9EFED");
            if (percent.Value <= 15) return Brush("#FFFF8D80");
            if (percent.Value <= 35) return Brush("#FFFFCE7A");
            return Brush("#FFB8F0DC");
        }

        private static MenuItem MenuItem(string text, RoutedEventHandler action)
        {
            var item = new MenuItem { Header = text, Padding = new Thickness(10, 7, 14, 7) };
            item.Click += action;
            return item;
        }

        internal static SolidColorBrush Brush(string hex) { return new SolidColorBrush(Col(hex)); }
        internal static Color Col(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    }

    public sealed class ReactiveDotMesh : FrameworkElement
    {
        private Point focus;
        private bool hasFocus;
        private bool renderQueued;
        private readonly SolidColorBrush[] brushes = new SolidColorBrush[16];
        private readonly double[] radii = new double[16];

        public ReactiveDotMesh()
        {
            for (int i = 0; i < brushes.Length; i++)
            {
                double intensity = i / (double)(brushes.Length - 1);
                byte alpha = (byte)Math.Max(22, Math.Min(210, 32 + intensity * 178));
                byte green = (byte)Math.Min(255, 185 + intensity * 55);
                byte blue = (byte)Math.Min(255, 164 + intensity * 68);
                brushes[i] = new SolidColorBrush(Color.FromArgb(alpha, 174, green, blue));
                brushes[i].Freeze();
                radii[i] = 0.85 + intensity * 1.45;
            }
        }

        public void SetFocus(Point point)
        {
            focus = point;
            hasFocus = true;
            QueueRender();
        }

        public void ClearFocus()
        {
            hasFocus = false;
            QueueRender();
        }

        private void QueueRender()
        {
            if (renderQueued) return;
            renderQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                renderQueued = false;
                InvalidateVisual();
            }), DispatcherPriority.Render);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double step = 18;
            double near = 76;
            double mid = 168;
            for (double y = 9; y < ActualHeight; y += step)
            {
                for (double x = 9; x < ActualWidth; x += step)
                {
                    double intensity = 0;
                    if (hasFocus)
                    {
                        double dx = x - focus.X;
                        double dy = y - focus.Y;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        double nearGlow = Math.Max(0, 1 - d / near);
                        double midGlow = Math.Max(0, 1 - Math.Abs(d - near) / mid) * 0.34;
                        intensity = Math.Min(1, nearGlow * nearGlow + midGlow);
                    }

                    int level = Math.Max(0, Math.Min(brushes.Length - 1, (int)Math.Round(intensity * (brushes.Length - 1))));
                    dc.DrawEllipse(brushes[level], null, new Point(x, y), radii[level], radii[level]);
                }
            }
        }
    }

    public sealed class TempScale
    {
        public Grid Root { get; private set; }
        private readonly Grid track;
        private readonly Grid scaleLayer;
        private readonly Border cpuMarker;
        private readonly Border gpuMarker;
        private readonly TextBlock caption;

        public TempScale()
        {
            Root = new Grid { Height = 42, Margin = new Thickness(0, 2, 0, 8) };
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });

            var labels = new Grid { Visibility = Visibility.Collapsed };
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });
            labels.Children.Add(Label("Safe <70C", "#FF9EDCCB", System.Windows.HorizontalAlignment.Left));
            var warm = Label("70-85C", "#FFFFCE7A", System.Windows.HorizontalAlignment.Center);
            Grid.SetColumn(warm, 1);
            labels.Children.Add(warm);
            var hot = Label(">85C", "#FFFF8D80", System.Windows.HorizontalAlignment.Right);
            Grid.SetColumn(hot, 2);
            labels.Children.Add(hot);
            Root.Children.Add(labels);

            scaleLayer = new Grid { Height = 0, ClipToBounds = false, Visibility = Visibility.Collapsed };
            track = new Grid { Height = 8, ClipToBounds = false, VerticalAlignment = VerticalAlignment.Center };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });
            track.Children.Add(Segment("#9954D4B1", new CornerRadius(4, 0, 0, 4)));
            var yellow = Segment("#99E5B96B", new CornerRadius(0));
            Grid.SetColumn(yellow, 1);
            track.Children.Add(yellow);
            var red = Segment("#99FF8D80", new CornerRadius(0, 4, 4, 0));
            Grid.SetColumn(red, 2);
            track.Children.Add(red);
            scaleLayer.Children.Add(track);
            cpuMarker = Marker("C", "#FF7ABEFF", VerticalAlignment.Top);
            gpuMarker = Marker("G", "#FFC99CFF", VerticalAlignment.Bottom);
            scaleLayer.Children.Add(cpuMarker);
            scaleLayer.Children.Add(gpuMarker);
            Grid.SetRow(scaleLayer, 1);
            Root.Children.Add(scaleLayer);

            caption = new TextBlock
            {
                Text = "CPU 來源：等待感測器",
                Foreground = BatteryWindow.Brush("#FFAEBBB3"),
                FontSize = 9.5,
                Margin = new Thickness(0, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15
            };
            Grid.SetRow(caption, 0);
            Root.Children.Add(caption);
        }

        public void Set(double? cpuTemp, string cpuSource, double? gpuTemp, string gpuSource)
        {
            if (!cpuTemp.HasValue && !gpuTemp.HasValue)
            {
                caption.Text = "溫度來源：沒有可用感測器";
                cpuMarker.Opacity = 0;
                gpuMarker.Opacity = 0;
                return;
            }

            cpuMarker.Opacity = 0;
            gpuMarker.Opacity = 0;
            string zone = cpuTemp.HasValue ? (cpuTemp.Value < 70 ? "safe" : (cpuTemp.Value < 85 ? "warm" : "hot")) : "unknown";
            string method = string.IsNullOrEmpty(cpuSource) ? "Windows sensor" : cpuSource;
            bool acpi = method.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0;
            string cpuText = cpuTemp.HasValue
                ? (acpi
                    ? "C=CPU：" + method + "，可能是系統熱區，僅供參考"
                    : "C=CPU：" + method + "，" + zone)
                : "C=CPU：無資料";
            string gpuText = gpuTemp.HasValue
                ? "G=NVIDIA：" + (string.IsNullOrEmpty(gpuSource) ? "GPU Core" : gpuSource)
                : "G=NVIDIA：無資料";
            caption.Text = cpuText + " / " + gpuText;
        }

        private void MoveMarker(Border marker, double? temp)
        {
            if (!temp.HasValue) return;
            double width = Math.Max(0, scaleLayer.ActualWidth - marker.Width);
            double x = width * Math.Max(0, Math.Min(100, temp.Value)) / 100.0;
            marker.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(marker.Margin, new Thickness(x, 0, 0, 0), TimeSpan.FromMilliseconds(420))
                { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        }

        private static Border Marker(string text, string color, VerticalAlignment verticalAlignment)
        {
            return new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = BatteryWindow.Brush(color),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = verticalAlignment,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.55 },
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private static TextBlock Label(string text, string color, System.Windows.HorizontalAlignment align)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = BatteryWindow.Brush(color),
                FontSize = 9.2,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = align
            };
        }

        private static Border Segment(string color, CornerRadius radius)
        {
            return new Border
            {
                Height = 0,
                CornerRadius = radius,
                Background = Brushes.Transparent,
                Opacity = 0
            };
        }
    }

    public sealed class MetricRow
    {
        public Grid Root { get; private set; }
        private readonly TextBlock value;
        private readonly Color fillColor;

        public MetricRow(string label, string color)
        {
            fillColor = BatteryWindow.Col(color);
            Root = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 4) };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = BatteryWindow.Brush("#FFC7D1CA"),
                FontSize = 11.2,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            value = new TextBlock
            {
                Text = "--",
                Foreground = new SolidColorBrush(fillColor),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 92,
                Margin = new Thickness(14, 0, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 1);
            Root.Children.Add(value);
        }

        public void Set(string text, double score)
        {
            value.Text = text;
        }
    }

    public sealed class MeterBar
    {
        public Grid Root { get; private set; }
        private readonly TextBlock percent;
        private readonly TextBlock note;

        public MeterBar(string title, string color)
        {
            Color fillColor = BatteryWindow.Col(color);
            Root = new Grid { Height = 42, Margin = new Thickness(0, 0, 0, 5) };
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            var labels = new Grid();
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            labels.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = BatteryWindow.Brush("#FFC7D1CA"),
                FontSize = 11.2,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            percent = new TextBlock
            {
                Text = "--%",
                Foreground = new SolidColorBrush(fillColor),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(percent, 1);
            labels.Children.Add(percent);
            Root.Children.Add(labels);
            note = new TextBlock
            {
                Text = "等待資料",
                Foreground = BatteryWindow.Brush("#FF8FA198"),
                FontSize = 9.5,
                Margin = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(note, 1);
            Root.Children.Add(note);
        }

        public void Set(double? value, string noteText)
        {
            percent.Text = value.HasValue ? Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture) + "%" : "--%";
            note.Text = noteText;
        }
    }

    public sealed class StorageVolumeSnapshot
    {
        public string Name;
        public double TotalGiB;
        public double UsedGiB;
        public double FreeGiB;
        public double UsedPercent;
    }

    public sealed class EnergyProcessSnapshot
    {
        public string Name;
        public double SharePercent;
        public double? EstimatedWatts;
    }

    // Keeps GPU usage and temperature tied to the same physical adapter.
    public sealed class GpuDeviceSnapshot
    {
        public string Name;
        public double? UsagePercent;
        public double? TemperatureC;
        public string UsageSource;
        public string TemperatureSource;
    }

    public sealed class BatterySnapshot
    {
        public double? Percent;
        public double? Watts;
        public string BatteryPowerMode;
        public double? SystemWatts;
        public string SystemWattsSource = "未取得";
        public double EstimatedComponentWatts;
        // These values are intentionally nullable. A charger label alone is not
        // enough evidence of its rated or negotiated power.
        public double? AdapterRatedWatts;
        public double? PdNegotiatedWatts;
        public string AdapterPowerSource = "未取得";
        public double? MemoryUsedPercent;
        public double? MemoryUsedMib;
        public double? MemoryTotalMib;
        public string MemorySource;
        public double? StorageUsedPercent;
        public double? StorageUsedGiB;
        public double? StorageFreeGiB;
        public double? StorageTotalGiB;
        public string StorageSource;
        public List<StorageVolumeSnapshot> StorageVolumes = new List<StorageVolumeSnapshot>();
        public List<EnergyProcessSnapshot> EnergyRanking = new List<EnergyProcessSnapshot>();
        public string EnergyRankingSource;
        public double? CpuUsagePercent;
        public string CpuUsageSource;
        public double? GpuUsagePercent;
        public string GpuUsageSource;
        public string GpuName;
        public List<GpuDeviceSnapshot> GpuDevices = new List<GpuDeviceSnapshot>();
        public bool DiscreteGpuUnavailable;
        public double? VoltageMv;
        public double? BatteryTempC;
        public double? StorageTempC;
        public string StorageTempSource;
        public double? DesignCapacityMwh;
        public double? FullChargeCapacityMwh;
        public double? CurrentCapacityMwh;
        public string CurrentCapacitySource;
        public double? CycleCount;
        public string BatteryName;
        public string BatteryManufacturer;
        public int BatteryLifeRemainingSeconds = -1;
        public double? CpuTempC;
        public string CpuTempSource;
        public double? GpuTempC;
        public string GpuTempSource;
        public string GpuStatus = "未偵測";
        public bool IsCharging;
        public bool IsAcLine;
        public string ChargerType = "未知";
        public string ChargerTypeSource = "尚未取得來源";
        public string StatusText = "未知";
        public string SourceNote = "30 秒自動更新";
        public double? ChargeEtaSeconds;
        public string ChargeEtaSource;
        public double? RuntimeEtaSeconds;
        public string ChargeForecastState = "未取得";
        public string RuntimeForecastState = "未取得";
        public double? ChargeLimitPercent;
        public string ChargeLimitSource;
        public bool ChargeLimitSupported;
        public bool ChargeLimitCanWrite;
        public string ChargeLimitMode;
        public string ChargeLimitProvider;
        public int[] ChargeLimitOptions = new int[0];
        public bool ChargeLimitIsLastApplied;
        public string ChargeLimitStateNote;
        public DateTime ReadAt = DateTime.Now;
    }

    public sealed class BatteryReader
    {
        private readonly LhmReader lhmReader = new LhmReader();
        private readonly PerformanceReader performanceReader = new PerformanceReader();

        public BatterySnapshot Read()
        {
            var data = new BatterySnapshot { ReadAt = DateTime.Now };
            try
            {
                System.Windows.Forms.PowerStatus ps = System.Windows.Forms.SystemInformation.PowerStatus;
                if (ps.BatteryLifePercent >= 0) data.Percent = ps.BatteryLifePercent * 100.0;
                data.IsCharging = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) == System.Windows.Forms.BatteryChargeStatus.Charging;
                data.IsAcLine = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                data.StatusText = StatusText(ps);
                data.BatteryLifeRemainingSeconds = ps.BatteryLifeRemaining;
            }
            catch (Exception ex) { RuntimeDiagnostics.Write("Windows 電源狀態", ex); }

            RunSafe("WMI 電池資料", delegate { ReadWmiBattery(data); });
            RunSafe("WMI 顯示卡狀態", delegate { ReadGpuStatus(data); });
            RunSafe("硬體監控 WMI", delegate { ReadHardwareMonitorSensors(data); });
            RunSafe("LibreHardwareMonitor 感測器", delegate { lhmReader.Read(data); });
            RunSafe("ACPI 溫度計數器", delegate { ReadThermalCounter(data); });
            RunSafe("ACPI 溫度區域", delegate { ReadThermalZone(data); });
            RunSafe("功耗快照整理", delegate { FinalizePower(data); });
            RunSafe("效能與程序功耗", delegate { performanceReader.Read(data); });
            RunSafe("充電器類型辨識", delegate { ChargerTypeDetector.Enrich(data); });
            RunSafe("充電上限辨識", delegate { BatteryLimitController.Enrich(data); });
            RunSafe("顯示卡快照整理", delegate
            {
                if (data.GpuUsagePercent.HasValue && data.GpuUsagePercent.Value > 0.5)
                    data.GpuStatus = "\u4f7f\u7528\u4e2d";
                data.SourceNote = SourceNote(data);
            });
            return data;
        }

        private static void RunSafe(string stage, Action action)
        {
            try
            {
                if (action != null) action();
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write(stage, ex);
            }
        }

        private static void FinalizePower(BatterySnapshot data)
        {
            if (data.Watts.HasValue && string.IsNullOrEmpty(data.BatteryPowerMode))
            {
                if (data.IsCharging) data.BatteryPowerMode = "充電";
                else if (!data.IsAcLine) data.BatteryPowerMode = "放電";
                else data.Watts = null;
            }
            if ((!data.SystemWatts.HasValue || data.SystemWatts.Value <= 0) && !data.IsAcLine && data.Watts.HasValue && data.Watts.Value > 0)
            {
                data.SystemWatts = Math.Abs(data.Watts.Value);
                data.SystemWattsSource = "Windows BatteryStatus / DischargeRate";
            }
            if ((!data.SystemWatts.HasValue || data.SystemWatts.Value <= 0) && data.EstimatedComponentWatts > 0)
            {
                data.SystemWatts = data.EstimatedComponentWatts;
                data.SystemWattsSource = "LibreHardwareMonitor 元件功率估算";
            }
            if (!data.SystemWatts.HasValue || data.SystemWatts.Value <= 0)
                data.SystemWattsSource = "未取得";
        }

        private static void ReadWmiBattery(BatterySnapshot data)
        {
            TryQuery("root\\wmi", "SELECT * FROM BatteryStatus", delegate(ManagementBaseObject item)
            {
                // 某些 ASUS TUF 機型的 WinForms PowerStatus 會回報 Unknown，
                // 但 root\\wmi 的 BatteryStatus 仍有 PowerOnline / Charging。
                // 先採用這些硬體回報，再判斷 ChargeRate，避免把充電誤判成電池供電。
                bool? powerOnline = Flag(item, "PowerOnline");
                bool? charging = Flag(item, "Charging");
                bool? discharging = Flag(item, "Discharging");
                if (powerOnline.HasValue) data.IsAcLine = powerOnline.Value;
                if (charging.HasValue) data.IsCharging = charging.Value;
                if (discharging.HasValue && discharging.Value) data.IsCharging = false;
                if (data.IsCharging) data.IsAcLine = true;

                double? chargeMw = Number(item, "ChargeRate");
                double? dischargeMw = Number(item, "DischargeRate");
                double? currentMw = Number(item, "CurrentRate");
                double? remainingMwh = Number(item, "RemainingCapacity");
                if (remainingMwh.HasValue && remainingMwh.Value > 0)
                {
                    data.CurrentCapacityMwh = remainingMwh.Value;
                    data.CurrentCapacitySource = "Windows BatteryStatus / RemainingCapacity";
                }
                bool hasCharge = chargeMw.HasValue && chargeMw.Value > 0;
                bool hasDischarge = dischargeMw.HasValue && dischargeMw.Value > 0;
                bool chargeSignal = hasCharge && (data.IsCharging ||
                    (charging.HasValue ? charging.Value : !hasDischarge));
                if (chargeSignal)
                {
                    data.Watts = NormalizeWatts(chargeMw.Value);
                    data.BatteryPowerMode = "充電";
                    data.IsAcLine = true;
                    data.IsCharging = true;
                }
                else if (hasDischarge && (!data.IsAcLine || !data.IsCharging))
                {
                    data.Watts = NormalizeWatts(dischargeMw.Value);
                    data.BatteryPowerMode = "放電";
                    data.IsCharging = false;
                    if (data.Watts.Value > 0)
                    {
                        data.SystemWatts = data.Watts;
                        data.SystemWattsSource = "Windows BatteryStatus / DischargeRate";
                    }
                }
                if (!data.Watts.HasValue && currentMw.HasValue && currentMw.Value > 0 && (data.IsCharging || !data.IsAcLine))
                {
                    data.Watts = NormalizeWatts(currentMw.Value);
                    data.BatteryPowerMode = data.IsCharging ? "充電" : "放電";
                    if (data.IsCharging) data.IsAcLine = true;
                }
                double? voltage = Number(item, "Voltage");
                if (voltage.HasValue && voltage.Value > 0) data.VoltageMv = voltage.Value;
                double? temp = Number(item, "Temperature") ?? Number(item, "BatteryTemperature");
                if (temp.HasValue) data.BatteryTempC = NormalizeTemp(temp.Value);
            });

            TryQuery("root\\wmi", "SELECT * FROM BatteryFullChargedCapacity", delegate(ManagementBaseObject item)
            {
                double? capacity = Number(item, "FullChargedCapacity");
                if (capacity.HasValue && capacity.Value > 0) data.FullChargeCapacityMwh = capacity.Value;
                double? voltage = Number(item, "Voltage");
                if (voltage.HasValue && voltage.Value > 0 && !data.VoltageMv.HasValue) data.VoltageMv = voltage.Value;
            });

            TryQuery("root\\wmi", "SELECT * FROM BatteryStaticData", delegate(ManagementBaseObject item)
            {
                double? capacity = Number(item, "DesignedCapacity");
                if (capacity.HasValue && capacity.Value > 0) data.DesignCapacityMwh = capacity.Value;
                string deviceName = Text(item, "DeviceName");
                string manufacturer = Text(item, "ManufactureName");
                if (!string.IsNullOrWhiteSpace(deviceName)) data.BatteryName = deviceName;
                if (!string.IsNullOrWhiteSpace(manufacturer)) data.BatteryManufacturer = manufacturer;
            });

            TryQuery("root\\wmi", "SELECT * FROM BatteryCycleCount", delegate(ManagementBaseObject item)
            {
                double? cycles = Number(item, "CycleCount");
                if (cycles.HasValue && cycles.Value >= 0) data.CycleCount = cycles.Value;
            });

            TryQuery("root\\cimv2", "SELECT * FROM Win32_Battery", delegate(ManagementBaseObject item)
            {
                double? batteryStatus = Number(item, "BatteryStatus");
                if (batteryStatus.HasValue && batteryStatus.Value >= 6 && batteryStatus.Value <= 9)
                {
                    // Win32_Battery 6-9 代表各種「充電中」狀態。
                    data.IsCharging = true;
                    data.IsAcLine = true;
                    if (string.Equals(data.BatteryPowerMode, "放電", StringComparison.OrdinalIgnoreCase))
                    {
                        data.Watts = null;
                        data.SystemWatts = null;
                        data.BatteryPowerMode = null;
                    }
                }
                double? percent = Number(item, "EstimatedChargeRemaining");
                if (percent.HasValue) data.Percent = percent.Value;
                double? voltage = Number(item, "DesignVoltage");
                if (voltage.HasValue && voltage.Value > 0 && !data.VoltageMv.HasValue) data.VoltageMv = voltage.Value;
                string deviceName = Text(item, "Name");
                string manufacturer = Text(item, "Manufacturer");
                if (!string.IsNullOrWhiteSpace(deviceName) && string.IsNullOrWhiteSpace(data.BatteryName)) data.BatteryName = deviceName;
                if (!string.IsNullOrWhiteSpace(manufacturer) && string.IsNullOrWhiteSpace(data.BatteryManufacturer)) data.BatteryManufacturer = manufacturer;
                object status = item["Status"];
                if (status != null && !string.IsNullOrWhiteSpace(status.ToString())) data.StatusText = status.ToString();
            });

            if (!data.CurrentCapacityMwh.HasValue && data.FullChargeCapacityMwh.HasValue && data.Percent.HasValue &&
                data.FullChargeCapacityMwh.Value > 0 && data.Percent.Value >= 0)
            {
                data.CurrentCapacityMwh = data.FullChargeCapacityMwh.Value * Math.Max(0, Math.Min(100, data.Percent.Value)) / 100.0;
                data.CurrentCapacitySource = "Windows BatteryStatus / 電量百分比推算";
            }
        }

        private static void ReadGpuStatus(BatterySnapshot data)
        {
            TryQuery("root\\cimv2", "SELECT * FROM Win32_VideoController", delegate(ManagementBaseObject item)
            {
                string name = Text(item, "Name");
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return;
                double? code = Number(item, "ConfigManagerErrorCode");
                if (code.HasValue && Math.Abs(code.Value - 22) < 0.1)
                    data.DiscreteGpuUnavailable = true;
                if (code.HasValue && Math.Abs(code.Value - 22) < 0.1)
                    data.GpuStatus = "已停用";
                else if (code.HasValue && code.Value > 0)
                    data.GpuStatus = "裝置異常 " + code.Value.ToString("0", CultureInfo.InvariantCulture);
                else
                    data.GpuStatus = "待機／感測器休眠";
            });
        }

        private static void ReadThermalZone(BatterySnapshot data)
        {
            TryQuery("root\\wmi", "SELECT * FROM MSAcpi_ThermalZoneTemperature", delegate(ManagementBaseObject item)
            {
                double? raw = Number(item, "CurrentTemperature");
                if (!raw.HasValue) return;
                double c = raw.Value / 10.0 - 273.15;
                if (c > 0 && c < 130)
                {
                    bool currentIsAcpi = string.IsNullOrEmpty(data.CpuTempSource) || data.CpuTempSource.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!data.CpuTempC.HasValue || (currentIsAcpi && c > data.CpuTempC.Value))
                    {
                        data.CpuTempC = c;
                        data.CpuTempSource = "ACPI WMI";
                    }
                }
            });
        }

        private static void ReadThermalCounter(BatterySnapshot data)
        {
            if (data.CpuTempC.HasValue) return;
            try
            {
                var category = new PerformanceCounterCategory("Thermal Zone Information");
                string[] instances = category.GetInstanceNames();
                double best = 0;
                foreach (string instance in instances)
                {
                    using (var counter = new PerformanceCounter("Thermal Zone Information", "Temperature", instance, true))
                    {
                        double raw = counter.NextValue();
                        double c = raw > 200 ? raw - 273.15 : raw;
                        if (c > 0 && c < 130 && c > best) best = c;
                    }
                }
                if (best > 0)
                {
                    data.CpuTempC = best;
                    data.CpuTempSource = "ACPI Thermal Zone";
                }
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("ACPI Thermal Zone", ex);
            }
        }

        private static void ReadHardwareMonitorSensors(BatterySnapshot data)
        {
            ReadSensorNamespace(data, "root\\LibreHardwareMonitor");
            ReadSensorNamespace(data, "root\\OpenHardwareMonitor");
        }

        private static void ReadSensorNamespace(BatterySnapshot data, string scope)
        {
            TryQuery(scope, "SELECT * FROM Sensor", delegate(ManagementBaseObject item)
            {
                string sensorType = Text(item, "SensorType");
                if (!EqualsText(sensorType, "Temperature")) return;

                string name = Text(item, "Name");
                string parent = Text(item, "Parent");
                string identifier = Text(item, "Identifier");
                double? value = Number(item, "Value");
                if (!value.HasValue || value.Value <= 0 || value.Value >= 130) return;

                string haystack = (name + " " + parent + " " + identifier).ToLowerInvariant();
                bool cpuHardware = haystack.IndexOf("cpu", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("processor", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("ryzen", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("intel", StringComparison.Ordinal) >= 0;
                bool cpuSensor = haystack.IndexOf("package", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("tctl", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("tdie", StringComparison.Ordinal) >= 0 ||
                    (cpuHardware && haystack.IndexOf("core", StringComparison.Ordinal) >= 0);
                if (cpuHardware && cpuSensor)
                {
                    if (!data.CpuTempC.HasValue || IsBetterCpuSensor(name, value.Value, data.CpuTempC.Value))
                    {
                        data.CpuTempC = value.Value;
                        data.CpuTempSource = scope.IndexOf("Libre", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "LibreHardwareMonitor"
                            : "OpenHardwareMonitor";
                    }
                }

                if (haystack.IndexOf("battery", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("batt", StringComparison.Ordinal) >= 0)
                {
                    if (!data.BatteryTempC.HasValue) data.BatteryTempC = value.Value;
                }

                if (HardwareTemperatureClassifier.IsGpuTemperatureSensor(haystack))
                {
                    if (HardwareTemperatureClassifier.ShouldUseGpuTemp(haystack, name, value.Value, data))
                    {
                        data.GpuTempC = value.Value;
                        data.GpuTempSource = HardwareTemperatureClassifier.GpuSourceLabel(haystack, name,
                            scope.IndexOf("Libre", StringComparison.OrdinalIgnoreCase) >= 0
                                ? "LibreHardwareMonitor"
                                : "OpenHardwareMonitor");
                    }
                }

                if (HardwareTemperatureClassifier.IsStorageTemperatureSensor(haystack) &&
                    HardwareTemperatureClassifier.ShouldUseStorageTemp(haystack, name, data))
                {
                    data.StorageTempC = value.Value;
                    string monitor = scope.IndexOf("Libre", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "LibreHardwareMonitor"
                        : "OpenHardwareMonitor";
                    data.StorageTempSource = monitor + " / " + (string.IsNullOrWhiteSpace(name) ? "儲存裝置" : name);
                }
            });
        }

        private static void TryQuery(string scopeName, string query, Action<ManagementBaseObject> handle)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(scopeName, query))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject item in results) handle(item);
                }
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("WMI " + scopeName, ex);
            }
        }

        private static double? Number(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                if (value == null) return null;
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        private static bool? Flag(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                if (value == null) return null;
                if (value is bool) return (bool)value;
                string text = value.ToString();
                bool parsed;
                if (bool.TryParse(text, out parsed)) return parsed;
                double numeric;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out numeric))
                    return Math.Abs(numeric) > 0.5;
            }
            catch { }
            return null;
        }

        private static string Text(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value == null ? string.Empty : value.ToString();
            }
            catch { return string.Empty; }
        }

        private static bool EqualsText(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBetterCpuSensor(string sensorName, double candidate, double current)
        {
            if (sensorName != null && sensorName.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return candidate > current;
        }

        private static double NormalizeTemp(double raw)
        {
            if (raw > 2000) return raw / 10.0 - 273.15;
            if (raw > 200) return raw / 10.0;
            return raw;
        }

        private static double NormalizeWatts(double raw)
        {
            return raw > 250 ? raw / 1000.0 : raw;
        }

        private static string StatusText(System.Windows.Forms.PowerStatus ps)
        {
            if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) return "無電池";
            if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) == System.Windows.Forms.BatteryChargeStatus.Charging) return "充電中";
            if (ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online) return "外接電源";
            if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Low) == System.Windows.Forms.BatteryChargeStatus.Low) return "低電量";
            if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Critical) == System.Windows.Forms.BatteryChargeStatus.Critical) return "危急";
            return "電池供電";
        }

        private static string SourceNote(BatterySnapshot data)
        {
            var parts = new List<string>();
            parts.Add("1 秒背景更新");
            if (!data.Watts.HasValue) parts.Add("瓦數 N/A");
            if (!data.SystemWatts.HasValue) parts.Add("電腦耗電 N/A");
            if (!data.StorageTempC.HasValue) parts.Add("儲存裝置溫度 N/A");
            if (!data.CpuTempC.HasValue) parts.Add("CPU 溫度 N/A");
            if (!data.GpuTempC.HasValue) parts.Add("GPU 溫度 N/A");
            return string.Join(" · ", parts.ToArray());
        }
    }

    internal static class HardwareTemperatureClassifier
    {
        public static bool IsGpuTemperatureSensor(string haystack)
        {
            string text = Lower(haystack);
            if (string.IsNullOrEmpty(text)) return false;
            if (ContainsAny(text, "nvidia", "geforce", "rtx", "gtx", "gpu")) return true;
            if (IsCpuText(text)) return false;
            return ContainsAny(text, "radeon", "vega", "uhd graphics", "iris", "arc graphics", "graphics");
        }

        public static bool IsStorageTemperatureSensor(string haystack)
        {
            string text = Lower(haystack);
            if (string.IsNullOrEmpty(text) || IsCpuText(text) ||
                ContainsAny(text, "battery", "batt", "gpu", "nvidia", "geforce", "radeon", "graphics"))
                return false;
            return ContainsAny(text, "nvme", "ssd", "hdd", "hard disk", "storage", "drive", "disk", "s.m.a.r.t", "smart");
        }

        public static bool ShouldUseStorageTemp(string haystack, string sensorName, BatterySnapshot data)
        {
            if (data == null || !data.StorageTempC.HasValue) return true;
            string candidate = Lower(haystack + " " + sensorName);
            string current = Lower(data.StorageTempSource);
            bool candidateNvme = ContainsAny(candidate, "nvme", "ssd");
            bool currentNvme = ContainsAny(current, "nvme", "ssd");
            if (candidateNvme && !currentNvme) return true;
            if (currentNvme && !candidateNvme) return false;
            return false;
        }

        public static bool IsGpuHardware(string hardwareType, string hardwareName)
        {
            string type = Lower(hardwareType);
            string name = Lower(hardwareName);
            if (ContainsAny(type, "gpu", "graphics")) return true;
            if (ContainsAny(type, "cpu", "processor", "motherboard")) return false;
            if (IsCpuText(name)) return false;
            return ContainsAny(name, "nvidia", "geforce", "rtx", "gtx", "gpu", "radeon", "vega", "uhd graphics", "iris", "arc graphics", "graphics");
        }

        public static bool IsDiscreteGpuHardware(string hardwareType, string hardwareName)
        {
            string text = Lower((hardwareType ?? string.Empty) + " " + (hardwareName ?? string.Empty));
            return ContainsAny(text, "nvidia", "geforce", "rtx", "gtx");
        }

        public static bool ShouldUseGpuTemp(string haystack, string sensorName, double candidate, BatterySnapshot data)
        {
            if (!data.GpuTempC.HasValue) return true;
            string text = Lower(haystack);
            string name = Lower(sensorName);
            bool candidateNvidia = ContainsAny(text, "nvidia", "geforce", "rtx", "gtx");
            bool currentNvidia = ContainsAny(data.GpuTempSource, "nvidia", "geforce", "rtx", "gtx");
            if (candidateNvidia && !currentNvidia) return true;
            if (currentNvidia && !candidateNvidia) return false;

            bool candidateCore = name.IndexOf("gpu core", StringComparison.Ordinal) >= 0;
            bool currentCore = Lower(data.GpuTempSource).IndexOf("gpu core", StringComparison.Ordinal) >= 0;
            if (candidateCore && !currentCore) return true;
            if (currentCore && !candidateCore) return false;
            return candidate > data.GpuTempC.Value;
        }

        public static string GpuSourceLabel(string haystack, string sensorName, string fallback)
        {
            string text = Lower(haystack);
            string name = Lower(sensorName);
            if (ContainsAny(text, "nvidia", "geforce", "rtx", "gtx") &&
                name.IndexOf("gpu core", StringComparison.Ordinal) >= 0)
                return "NVIDIA GPU Core";
            if (ContainsAny(text, "nvidia", "geforce", "rtx", "gtx"))
                return "NVIDIA GPU";
            if (ContainsAny(text, "radeon", "vega"))
                return "AMD Radeon GPU";
            if (ContainsAny(text, "uhd graphics", "iris", "intel"))
                return "Intel Graphics GPU";
            if (name.IndexOf("gpu core", StringComparison.Ordinal) >= 0)
                return "GPU Core";
            return fallback;
        }

        private static bool IsCpuText(string text)
        {
            return ContainsAny(text, "cpu", "processor", "ryzen", "tctl", "tdie", "package");
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string value in values)
                if (text.IndexOf(value, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string Lower(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant();
        }
    }

    public sealed class AppSettings
    {
        public static readonly string[] TopBarItemIds =
        {
            "charger", "charge", "percent", "power", "cpuTemp", "gpuTemp",
            "eta", "ram", "cpuUsage", "gpuUsage"
        };

        public string CustomTitle = "BATTERY PULSE";
        public bool TextShadow = true;
        public bool HasWindowPosition;
        public double WindowLeft;
        public double WindowTop;
        public bool WidgetExpanded;
        public double PdWatts = 100;
        public int BatteryLimitPercent = 80;
        public bool BatteryLimitHasApplied;
        public double CpuWarnC = 85;
        public double GpuWarnC = 85;
        public bool AlertsEnabled = true;
        public string UpdateApiUrl = UpdateService.DefaultApiUrl;
        public string UpdatePageUrl = UpdateService.DefaultPageUrl;
        public string TopBarItems = "charger,charge,percent,power,cpuTemp,gpuTemp,eta,ram,cpuUsage,gpuUsage";
        public string TopBarHiddenItems = "ram,cpuUsage,gpuUsage";
        public bool TopBarEtaDefaultApplied;
        public double DayWh;
        public double MonthWh;
        public string DayKey = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string MonthKey = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        public DateTime LastEnergyAt = DateTime.MinValue;
        public static string AppDirectory
        {
            get
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryPulse");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string HistoryDirectory
        {
            get
            {
                string dir = System.IO.Path.Combine(AppDirectory, "History");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string PathName { get { return System.IO.Path.Combine(AppDirectory, "settings.ini"); } }

        internal static string SettingsFilePath { get { return PathName; } }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(PathName))
                {
                    settings.TopBarEtaDefaultApplied = true;
                    return settings;
                }
                foreach (string line in File.ReadAllLines(PathName))
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string key = line.Substring(0, idx);
                    string value = line.Substring(idx + 1);
                    if (key == "title" && !string.IsNullOrWhiteSpace(value)) settings.CustomTitle = value;
                    if (key == "shadow") settings.TextShadow = value == "1";
                    if (key == "hasWindowPosition") settings.HasWindowPosition = value == "1";
                    if (key == "windowPosition") settings.HasWindowPosition = value == "1";
                    if (key == "windowLeft") settings.WindowLeft = ParseDouble(value);
                    if (key == "windowTop") settings.WindowTop = ParseDouble(value);
                    if (key == "widgetExpanded") settings.WidgetExpanded = value == "1";
                    if (key == "expanded") settings.WidgetExpanded = value == "1";
                    if (key == "pdWatts") settings.PdWatts = Math.Max(20, ParseDouble(value));
                    if (key == "batteryLimitPercent") settings.BatteryLimitPercent = (int)Math.Max(40, Math.Min(100, ParseDouble(value)));
                    if (key == "batteryLimitHasApplied") settings.BatteryLimitHasApplied = value == "1";
                    if (key == "cpuWarnC") settings.CpuWarnC = Math.Max(60, ParseDouble(value));
                    if (key == "gpuWarnC") settings.GpuWarnC = Math.Max(60, ParseDouble(value));
                    if (key == "alertsEnabled") settings.AlertsEnabled = value == "1";
                    if (key == "updateApiUrl" && !string.IsNullOrWhiteSpace(value)) settings.UpdateApiUrl = value.Trim();
                    if (key == "updatePageUrl" && !string.IsNullOrWhiteSpace(value)) settings.UpdatePageUrl = value.Trim();
                    if (key == "topBarItems" && !string.IsNullOrWhiteSpace(value)) settings.TopBarItems = value;
                    if (key == "topBarHiddenItems") settings.TopBarHiddenItems = value;
                    if (key == "topBarEtaDefaultApplied") settings.TopBarEtaDefaultApplied = value == "1";
                    if (key == "dayWh") settings.DayWh = ParseDouble(value);
                    if (key == "monthWh") settings.MonthWh = ParseDouble(value);
                    if (key == "dayKey" && !string.IsNullOrWhiteSpace(value)) settings.DayKey = value;
                    if (key == "monthKey" && !string.IsNullOrWhiteSpace(value)) settings.MonthKey = value;
                    if (key == "lastEnergyAt") settings.LastEnergyAt = ParseDate(value);
                }
            }
            catch { }
            if (!settings.TopBarEtaDefaultApplied)
            {
                // 舊版曾預設隱藏 ETA；只遷移一次，之後仍允許使用者自行關閉。
                settings.SetTopBarItemEnabled("eta", true);
                settings.TopBarEtaDefaultApplied = true;
                settings.Save();
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(PathName, new[]
                {
                    "title=" + CustomTitle,
                    "shadow=" + (TextShadow ? "1" : "0"),
                    "hasWindowPosition=" + (HasWindowPosition ? "1" : "0"),
                    "windowPosition=" + (HasWindowPosition ? "1" : "0"),
                    "windowLeft=" + WindowLeft.ToString("R", CultureInfo.InvariantCulture),
                    "windowTop=" + WindowTop.ToString("R", CultureInfo.InvariantCulture),
                    "widgetExpanded=" + (WidgetExpanded ? "1" : "0"),
                    "expanded=" + (WidgetExpanded ? "1" : "0"),
                    "pdWatts=" + PdWatts.ToString("R", CultureInfo.InvariantCulture),
                    "batteryLimitPercent=" + BatteryLimitPercent.ToString(CultureInfo.InvariantCulture),
                    "batteryLimitHasApplied=" + (BatteryLimitHasApplied ? "1" : "0"),
                    "cpuWarnC=" + CpuWarnC.ToString("R", CultureInfo.InvariantCulture),
                    "gpuWarnC=" + GpuWarnC.ToString("R", CultureInfo.InvariantCulture),
                    "alertsEnabled=" + (AlertsEnabled ? "1" : "0"),
                    "updateApiUrl=" + UpdateApiUrl,
                    "updatePageUrl=" + UpdatePageUrl,
                    "topBarItems=" + TopBarItems,
                    "topBarHiddenItems=" + TopBarHiddenItems,
                    "topBarEtaDefaultApplied=" + (TopBarEtaDefaultApplied ? "1" : "0"),
                    "dayWh=" + DayWh.ToString("R", CultureInfo.InvariantCulture),
                    "monthWh=" + MonthWh.ToString("R", CultureInfo.InvariantCulture),
                    "dayKey=" + DayKey,
                    "monthKey=" + MonthKey,
                    "lastEnergyAt=" + LastEnergyAt.ToString("o", CultureInfo.InvariantCulture)
                });
            }
            catch { }
        }

        public List<string> GetTopBarItems()
        {
            var result = new List<string>();
            string[] stored = (TopBarItems ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string item in stored)
            {
                string id = item.Trim();
                if (Array.IndexOf(TopBarItemIds, id) >= 0 && !result.Contains(id)) result.Add(id);
            }
            foreach (string id in TopBarItemIds)
            {
                if (!result.Contains(id)) result.Add(id);
            }
            return result;
        }

        public bool IsTopBarItemEnabled(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            string[] hidden = (TopBarHiddenItems ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return !hidden.Any(delegate(string value) { return string.Equals(value.Trim(), id, StringComparison.OrdinalIgnoreCase); });
        }

        public void SetTopBarItemEnabled(string id, bool enabled)
        {
            var hidden = new List<string>((TopBarHiddenItems ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            hidden.RemoveAll(delegate(string value) { return string.Equals(value.Trim(), id, StringComparison.OrdinalIgnoreCase); });
            if (!enabled) hidden.Add(id);
            TopBarHiddenItems = string.Join(",", hidden.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public static string TopBarItemLabel(string id)
        {
            switch (id)
            {
                case "charger": return "充電器類型";
                case "charge": return "充電瓦數";
                case "percent": return "電池電量";
                case "power": return "電腦耗電";
                case "cpuTemp": return "CPU 溫度";
                case "gpuTemp": return "GPU 溫度";
                case "eta": return "續行";
                case "ram": return "RAM 使用率";
                case "cpuUsage": return "CPU 使用率";
                case "gpuUsage": return "GPU 使用率";
                default: return id;
            }
        }

        private static double ParseDouble(string value)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static DateTime ParseDate(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)
                ? parsed
                : DateTime.MinValue;
        }
    }

    public sealed class LhmReader
    {
        private object computer;
        private Assembly assembly;
        private bool initialized;
        private bool resolverAttached;
        private DateTime lastInitializeAttempt = DateTime.MinValue;
        private DateTime retryAfter = DateTime.MinValue;
        private int consecutiveFailures;
        private bool initializedWithGpu;

        public void Read(BatterySnapshot data)
        {
            if (data == null) return;
            if (DateTime.Now < retryAfter)
            {
                ClearUnpairedGpuData(data);
                return;
            }

            try
            {
                bool enableGpu = !data.DiscreteGpuUnavailable;
                if (computer != null && initializedWithGpu != enableGpu)
                    InvalidateComputer();
                EnsureInitialized(enableGpu);
                if (computer == null)
                {
                    ClearUnpairedGpuData(data);
                    return;
                }
                PropertyInfo hardwareProperty = computer.GetType().GetProperty("Hardware");
                object hardwareList = hardwareProperty == null ? null : hardwareProperty.GetValue(computer, null);
                if (hardwareList == null)
                {
                    ClearUnpairedGpuData(data);
                    return;
                }
                ScanHardwareList(hardwareList, data, data.DiscreteGpuUnavailable);
                SelectActiveGpu(data);
                // A missing adapter during a mode switch is expected. A
                // successful enumeration, even with no active GPU, is not a
                // monitor failure and should not trigger a reset.
                consecutiveFailures = 0;
                retryAfter = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                ClearUnpairedGpuData(data);
                consecutiveFailures++;
                retryAfter = DateTime.Now.AddSeconds(Math.Min(10, Math.Max(2, consecutiveFailures * 2)));
                RuntimeDiagnostics.Write("GPU hardware re-enumeration", ex);
                if (consecutiveFailures >= 3) InvalidateComputer();
            }
        }

        private static void ClearUnpairedGpuData(BatterySnapshot data)
        {
            if (data == null) return;
            data.GpuName = null;
            data.GpuUsagePercent = null;
            data.GpuUsageSource = null;
            data.GpuTempC = null;
            data.GpuTempSource = null;
            if (data.GpuDevices != null) data.GpuDevices.Clear();
        }

        private void EnsureInitialized(bool enableGpu)
        {
            if (computer != null) return;
            if (initialized && (DateTime.Now - lastInitializeAttempt).TotalSeconds < 10) return;
            initialized = true;
            lastInitializeAttempt = DateTime.Now;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidateDirs =
            {
                Path.Combine(baseDir, "runtime", "LibreHardwareMonitor"),
                Path.Combine(baseDir, "LibreHardwareMonitor"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "runtime", "LibreHardwareMonitor")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "runtime", "LibreHardwareMonitor"))
            };
            string dir = null;
            foreach (string candidate in candidateDirs)
            {
                if (File.Exists(Path.Combine(candidate, "LibreHardwareMonitorLib.dll")))
                {
                    dir = candidate;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(dir)) return;
            string dll = Path.Combine(dir, "LibreHardwareMonitorLib.dll");

            if (!resolverAttached)
            {
                resolverAttached = true;
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
                {
                    string simple = new AssemblyName(args.Name).Name + ".dll";
                    string local = Path.Combine(dir, simple);
                    if (File.Exists(local)) return Assembly.LoadFrom(local);
                    return null;
                };
            }

            assembly = Assembly.LoadFrom(dll);
            Type computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer");
            if (computerType == null) return;
            computer = Activator.CreateInstance(computerType);
            SetBool(computer, "IsCpuEnabled", true);
            SetBool(computer, "IsGpuEnabled", enableGpu);
            SetBool(computer, "IsMotherboardEnabled", true);
            SetBool(computer, "IsBatteryEnabled", true);
            computerType.GetMethod("Open").Invoke(computer, null);
            initializedWithGpu = enableGpu;
        }

        private void InvalidateComputer()
        {
            // Do not invoke LibreHardwareMonitor.Close() while a GPU driver is
            // being switched by G Helper. Drop the managed object and rebuild
            // it after the retry cooldown instead.
            computer = null;
            assembly = null;
            initialized = false;
            initializedWithGpu = false;
        }

        private void ScanHardwareList(object hardwareList, BatterySnapshot data, bool skipDiscreteGpu)
        {
            System.Collections.IEnumerable items = hardwareList as System.Collections.IEnumerable;
            if (items == null) return;
            foreach (object hardware in items)
            {
                try
                {
                    ScanHardware(hardware, data, skipDiscreteGpu);
                }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.Write("LibreHardwareMonitor 硬體項目", ex);
                }
            }
        }

        private void ScanHardware(object hardware, BatterySnapshot data, bool skipDiscreteGpu)
        {
            if (hardware == null) return;
            string type = PropText(hardware, "HardwareType");
            string hardwareName = PropText(hardware, "Name");
            if (skipDiscreteGpu && HardwareTemperatureClassifier.IsDiscreteGpuHardware(type, hardwareName)) return;

            MethodInfo update = hardware.GetType().GetMethod("Update");
            if (update != null) update.Invoke(hardware, null);
            PropertyInfo sensorsProperty = hardware.GetType().GetProperty("Sensors");
            object sensors = sensorsProperty == null ? null : sensorsProperty.GetValue(hardware, null);
            System.Collections.IEnumerable sensorItems = sensors as System.Collections.IEnumerable;
            if (sensorItems != null)
            {
                foreach (object sensor in sensorItems) ScanSensor(sensor, type, hardwareName, data);
            }

            PropertyInfo subHardwareProperty = hardware.GetType().GetProperty("SubHardware");
            if (subHardwareProperty != null)
                ScanHardwareList(subHardwareProperty.GetValue(hardware, null), data, skipDiscreteGpu);
        }

        private static void ScanSensor(object sensor, string hardwareType, string hardwareName, BatterySnapshot data)
        {
            string sensorType = PropText(sensor, "SensorType");
            double? value = PropNumber(sensor, "Value");
            if (!value.HasValue) return;

            string sensorName = PropText(sensor, "Name");
            string haystack = (hardwareType + " " + hardwareName + " " + sensorName).ToLowerInvariant();
            if (string.Equals(sensorType, "Power", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Value < 0) return;
                ReadPowerSensor(haystack, sensorName, value.Value, data);
                return;
            }

            if (string.Equals(sensorType, "Load", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Value < 0 || value.Value > 100) return;
                bool gpuHardware = HardwareTemperatureClassifier.IsGpuHardware(hardwareType, hardwareName);
                bool gpuLoad = gpuHardware && (haystack.IndexOf("3d", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("d3d", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("core", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("gpu", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("compute", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("video decode", StringComparison.Ordinal) >= 0 ||
                    haystack.IndexOf("video encode", StringComparison.Ordinal) >= 0);
                if (gpuLoad)
                {
                    GpuDeviceSnapshot gpu = GetGpuDevice(data, hardwareType, hardwareName);
                    if (!gpu.UsagePercent.HasValue || value.Value > gpu.UsagePercent.Value)
                    {
                        gpu.UsagePercent = value.Value;
                        gpu.UsageSource = "LibreHardwareMonitor / " + sensorName;
                    }
                }
                return;
            }

            if (!string.Equals(sensorType, "Temperature", StringComparison.OrdinalIgnoreCase)) return;
            if (value.Value <= 0 || value.Value >= 130) return;
            bool cpuHardware = haystack.IndexOf("cpu", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("processor", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("ryzen", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("intel", StringComparison.Ordinal) >= 0;
            bool cpuSensor = haystack.IndexOf("package", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("tctl", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("tdie", StringComparison.Ordinal) >= 0 ||
                (cpuHardware && haystack.IndexOf("core", StringComparison.Ordinal) >= 0);
            if (cpuHardware && cpuSensor)
            {
                if (!data.CpuTempC.HasValue || IsPreferredCpuSensor(sensorName, value.Value, data.CpuTempC.Value))
                {
                    data.CpuTempC = value.Value;
                    data.CpuTempSource = "LibreHardwareMonitor " + sensorName;
                }
            }

            if (haystack.IndexOf("battery", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("batt", StringComparison.Ordinal) >= 0)
            {
                if (!data.BatteryTempC.HasValue) data.BatteryTempC = value.Value;
            }

            if (HardwareTemperatureClassifier.IsGpuHardware(hardwareType, hardwareName))
            {
                GpuDeviceSnapshot gpu = GetGpuDevice(data, hardwareType, hardwareName);
                if (!gpu.TemperatureC.HasValue || IsPreferredGpuTemperature(sensorName, value.Value, gpu))
                {
                    gpu.TemperatureC = value.Value;
                    gpu.TemperatureSource = "LibreHardwareMonitor / " + sensorName;
                }
            }
        }

        private static GpuDeviceSnapshot GetGpuDevice(BatterySnapshot data, string hardwareType, string hardwareName)
        {
            string key = string.IsNullOrWhiteSpace(hardwareName) ? hardwareType : hardwareName;
            if (string.IsNullOrWhiteSpace(key)) key = "GPU";
            key = key.Trim();

            foreach (GpuDeviceSnapshot item in data.GpuDevices)
            {
                if (string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase)) return item;
            }

            var created = new GpuDeviceSnapshot { Name = key };
            data.GpuDevices.Add(created);
            return created;
        }

        private static bool IsPreferredGpuTemperature(string sensorName, double candidate, GpuDeviceSnapshot current)
        {
            string candidateName = (sensorName ?? string.Empty).ToLowerInvariant();
            string currentName = (current.TemperatureSource ?? string.Empty).ToLowerInvariant();
            bool candidateCore = candidateName.IndexOf("gpu core", StringComparison.Ordinal) >= 0 ||
                candidateName.IndexOf("core", StringComparison.Ordinal) >= 0;
            bool currentCore = currentName.IndexOf("gpu core", StringComparison.Ordinal) >= 0 ||
                currentName.IndexOf("core", StringComparison.Ordinal) >= 0;
            if (candidateCore && !currentCore) return true;
            if (currentCore && !candidateCore) return false;
            return candidate > current.TemperatureC.Value;
        }

        private static void SelectActiveGpu(BatterySnapshot data)
        {
            GpuDeviceSnapshot active = data.GpuDevices
                .Where(delegate(GpuDeviceSnapshot item)
                {
                    return item.UsagePercent.HasValue && item.UsagePercent.Value > 0.5;
                })
                .OrderByDescending(delegate(GpuDeviceSnapshot item) { return item.UsagePercent.Value; })
                .FirstOrDefault();

            if (active == null)
            {
                // Do not expose an idle adapter's temperature when no adapter has
                // a measurable workload. The UI can then show -- honestly.
                data.GpuName = null;
                data.GpuUsagePercent = null;
                data.GpuUsageSource = null;
                data.GpuTempC = null;
                data.GpuTempSource = null;
                if (data.GpuDevices.Count > 0)
                    data.GpuStatus = "\u672a\u5075\u6e2c\u5230\u4f7f\u7528\u4e2d\u7684 GPU";
                return;
            }

            data.GpuName = active.Name;
            data.GpuUsagePercent = active.UsagePercent;
            data.GpuUsageSource = active.UsageSource;
            data.GpuTempC = active.TemperatureC;
            data.GpuTempSource = active.TemperatureSource;
            data.GpuStatus = "\u4f7f\u7528\u4e2d";
        }

        private static void ReadPowerSensor(string haystack, string sensorName, double watts, BatterySnapshot data)
        {
            if (ChargerTypeDetector.TryFromPowerSensor(haystack, sensorName, data)) return;

            if (haystack.IndexOf("battery", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("batt", StringComparison.Ordinal) >= 0)
            {
                if (watts <= 0) return;
                bool discharge = haystack.IndexOf("discharge", StringComparison.Ordinal) >= 0;
                bool charge = haystack.IndexOf("charge", StringComparison.Ordinal) >= 0 && !discharge;
                if (discharge)
                    data.BatteryPowerMode = "放電";
                else if (charge && data.IsAcLine)
                    data.BatteryPowerMode = "充電";
                else if (data.IsCharging)
                    data.BatteryPowerMode = "充電";
                else if (!data.IsAcLine)
                    data.BatteryPowerMode = "放電";
                else
                    return;
                data.Watts = watts;
                if (discharge || (!data.IsAcLine && haystack.IndexOf("power", StringComparison.Ordinal) >= 0))
                {
                    if (watts > 0)
                    {
                        data.SystemWatts = watts;
                        data.SystemWattsSource = "LibreHardwareMonitor 電池功率感測器";
                    }
                }
                return;
            }

            if (haystack.IndexOf("cpu", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("package", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("gpu", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("graphics", StringComparison.Ordinal) >= 0 ||
                haystack.IndexOf("motherboard", StringComparison.Ordinal) >= 0)
            {
                data.EstimatedComponentWatts += watts;
            }
        }

        private static bool IsPreferredCpuSensor(string sensorName, double candidate, double current)
        {
            if (sensorName != null && sensorName.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sensorName != null && sensorName.IndexOf("tctl", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sensorName != null && sensorName.IndexOf("tdie", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return candidate > current;
        }

        private static void SetBool(object target, string propertyName, bool value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite) property.SetValue(target, value, null);
        }

        private static string PropText(object target, string propertyName)
        {
            try
            {
                PropertyInfo property = target.GetType().GetProperty(propertyName);
                object value = property == null ? null : property.GetValue(target, null);
                return value == null ? string.Empty : value.ToString();
            }
            catch { return string.Empty; }
        }

        private static double? PropNumber(object target, string propertyName)
        {
            try
            {
                PropertyInfo property = target.GetType().GetProperty(propertyName);
                object value = property == null ? null : property.GetValue(target, null);
                if (value == null) return null;
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }
    }

    public static class ChargerTypeDetector
    {
        private static readonly object sync = new object();
        private static DateTime lastPnpRead = DateTime.MinValue;
        private static string cachedType = "未知";
        private static string cachedSource = "尚未取得來源";
        private static int pnpReadInProgress;

        internal static bool TryFromPowerSensor(string haystack, string sensorName, BatterySnapshot data)
        {
            if (data == null || !data.IsAcLine) return false;

            string text = ((haystack ?? string.Empty) + " " + (sensorName ?? string.Empty)).ToLowerInvariant();
            bool isBatterySignal = text.IndexOf("battery charge", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("battery discharge", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("battery power", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("batt charge", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("batt discharge", StringComparison.Ordinal) >= 0;
            bool isPd = text.IndexOf("usb-pd", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("usb pd", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("power delivery", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("pd input", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("type-c pd", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("type c pd", StringComparison.Ordinal) >= 0;
            bool isOriginalAdapter = text.IndexOf("ac adapter", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("power adapter", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("adapter input", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("charger input", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("dc in", StringComparison.Ordinal) >= 0 ||
                (text.IndexOf("asus", StringComparison.Ordinal) >= 0 && text.IndexOf("adapter", StringComparison.Ordinal) >= 0);

            if (isPd && !isBatterySignal)
            {
                data.ChargerType = "USB-PD";
                data.ChargerTypeSource = "LibreHardwareMonitor " + (string.IsNullOrWhiteSpace(sensorName) ? "Power sensor" : sensorName);
                data.AdapterPowerSource = data.ChargerTypeSource;
                return true;
            }
            if (isOriginalAdapter && !isBatterySignal)
            {
                data.ChargerType = "原廠充電器";
                data.ChargerTypeSource = "LibreHardwareMonitor " + (string.IsNullOrWhiteSpace(sensorName) ? "Power sensor" : sensorName);
                data.AdapterPowerSource = data.ChargerTypeSource;
                return true;
            }
            return false;
        }

        public static void Enrich(BatterySnapshot data)
        {
            if (data == null) return;
            if (!data.IsAcLine)
            {
                data.ChargerType = "未接電";
                data.ChargerTypeSource = "Windows PowerStatus";
                data.AdapterPowerSource = data.ChargerTypeSource;
                return;
            }
            if (!string.Equals(data.ChargerType, "未知", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(data.ChargerType)) return;

            bool requestRefresh = false;
            lock (sync)
            {
                data.ChargerType = cachedType;
                data.ChargerTypeSource = cachedSource;
                data.AdapterPowerSource = cachedSource;
                if ((DateTime.Now - lastPnpRead).TotalSeconds >= 12 &&
                    Interlocked.CompareExchange(ref pnpReadInProgress, 1, 0) == 0)
                {
                    lastPnpRead = DateTime.Now;
                    requestRefresh = true;
                }
            }
            if (requestRefresh)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { RefreshPnpHint(); }
                    finally { Interlocked.Exchange(ref pnpReadInProgress, 0); }
                });
            }
        }

        private static void RefreshPnpHint()
        {
            bool foundPd = false;
            bool foundOriginal = false;
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%Power Delivery%' OR Name LIKE '%USB-PD%' OR Name LIKE '%USB PD%' OR Name LIKE '%AC Adapter%'"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture) ?? string.Empty;
                        string text = name.ToLowerInvariant();
                        if (text.IndexOf("power delivery", StringComparison.Ordinal) >= 0 ||
                            text.IndexOf("usb-pd", StringComparison.Ordinal) >= 0 ||
                            text.IndexOf("usb pd", StringComparison.Ordinal) >= 0)
                            foundPd = true;
                        if (text.IndexOf("ac adapter", StringComparison.Ordinal) >= 0 &&
                            text.IndexOf("asus", StringComparison.Ordinal) >= 0)
                            foundOriginal = true;
                    }
                }
            }
            catch { }

            lock (sync)
            {
                if (foundPd && !foundOriginal)
                {
                    cachedType = "USB-PD";
                    cachedSource = "Windows PnP / UCSI 裝置";
                }
                else if (foundOriginal && !foundPd)
                {
                    cachedType = "原廠充電器";
                    cachedSource = "Windows PnP / ASUS AC Adapter";
                }
                else
                {
                    cachedType = "未知";
                    cachedSource = "沒有可用的充電器類型來源";
                }
            }
        }
    }

    public static class StartupManager
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "BatteryPulse";
        private const string PreferencePath = @"Software\BatteryPulse";
        private const string FirstRunValueName = "StartupConfigured";

        public static void EnsureFirstRun(string[] args)
        {
            if (IsTestInvocation(args)) return;

            try
            {
                using (RegistryKey preferences = Registry.CurrentUser.CreateSubKey(PreferencePath))
                {
                    if (preferences == null) throw new InvalidOperationException("Startup preference key is unavailable.");
                    if (preferences.GetValue(FirstRunValueName) != null) return;

                    using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(KeyPath))
                    {
                        if (runKey == null) throw new InvalidOperationException("Startup registry key is unavailable.");
                        runKey.SetValue(ValueName, StartupCommand(), RegistryValueKind.String);
                    }
                    preferences.SetValue(FirstRunValueName, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                // Startup registration must never prevent the app from opening.
                RuntimeDiagnostics.Write("首次設定開機啟動", ex);
            }
        }

        private static bool IsTestInvocation(string[] args)
        {
            if (args != null && args.Any(delegate(string value)
            {
                return string.Equals(value, "--test-instance", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "--no-startup", StringComparison.OrdinalIgnoreCase);
            })) return true;
            return Assembly.GetExecutingAssembly().Location.EndsWith(".test.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string StartupCommand()
        {
            return "\"" + Assembly.GetExecutingAssembly().Location + "\"";
        }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                return key != null && key.GetValue(ValueName) != null;
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (key == null) throw new InvalidOperationException("Startup registry key is unavailable.");
                if (enabled)
                    key.SetValue(ValueName, StartupCommand(), RegistryValueKind.String);
                else
                    key.DeleteValue(ValueName, false);
            }
            using (RegistryKey preferences = Registry.CurrentUser.CreateSubKey(PreferencePath))
            {
                if (preferences == null) throw new InvalidOperationException("Startup preference key is unavailable.");
                // A manual toggle is an explicit user decision, including OFF.
                preferences.SetValue(FirstRunValueName, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);
            }
        }
    }

    public static class Glass
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            SetAccent(window, 0, 0);
        }

        public static void Clear(Window window)
        {
            SetAccent(window, 0, 0);
        }

        private static void SetAccent(Window window, int state, int gradientColor)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = state,
                    AccentFlags = 2,
                    GradientColor = gradientColor,
                    AnimationId = 0
                };
                int size = Marshal.SizeOf(accent);
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
                int light = 0;
                DwmSetWindowAttribute(hwnd, 20, ref light, sizeof(int));
                int corners = 2;
                DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
            }
            catch { }
            finally
            {
                if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            RuntimeDiagnostics.AttachGlobalHandlers();
            try
            {
                bool testInstance = (args != null && args.Any(delegate(string value) { return string.Equals(value, "--test-instance", StringComparison.OrdinalIgnoreCase); })) ||
                    Assembly.GetExecutingAssembly().Location.EndsWith(".test.exe", StringComparison.OrdinalIgnoreCase);
                StartupManager.EnsureFirstRun(args);
                bool created;
                using (var mutex = new Mutex(true, testInstance ? "Local\\BatteryPulseWidgetTest" : "Local\\BatteryPulseWidget", out created))
                {
                    if (!created) return;
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        RuntimeDiagnostics.Write("WPF Dispatcher", e.Exception);
                        e.Handled = true;
                    };
                    var window = new BatteryWindow();
                    app.MainWindow = window;
                    window.Show();
                    app.Run();
                }
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Write("主程序外層例外", ex);
                WriteCrash(ex);
            }
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BatteryPulse.crash.log"), ex.ToString());
            }
            catch { }
        }
    }

    internal static class RuntimeDiagnostics
    {
        private static readonly object Sync = new object();
        private static DateTime lastWrite = DateTime.MinValue;

        public static void AttachGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject, CultureInfo.InvariantCulture));
                Write("AppDomain 未處理例外", ex);
            };
            TaskScheduler.UnobservedTaskException += delegate(object sender, UnobservedTaskExceptionEventArgs e)
            {
                Write("未觀察到的工作例外", e.Exception);
                e.SetObserved();
            };
        }

        public static void Write(string stage, Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (Sync)
                {
                    // Avoid turning a transient sensor fault into a disk-writing loop.
                    if ((DateTime.Now - lastWrite).TotalSeconds < 5) return;
                    lastWrite = DateTime.Now;
                    string path = Path.Combine(AppSettings.AppDirectory, "runtime-diagnostics.log");
                    string line = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) +
                        " [" + (stage ?? "runtime") + "] " + ex + Environment.NewLine;
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
