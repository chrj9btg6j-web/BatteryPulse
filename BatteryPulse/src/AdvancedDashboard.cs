using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BatteryPulse
{
    public sealed class AdvancedDashboard
    {
        private readonly BatteryWindow owner;
        private readonly AppSettings settings;
        private readonly TelemetryStore store;
        private readonly RollingTelemetry history;
        private readonly Dictionary<string, List<MetricView>> metrics = new Dictionary<string, List<MetricView>>();
        private readonly List<AdvancedNavVisual> navVisuals = new List<AdvancedNavVisual>();
        private readonly List<FrameworkElement> pages = new List<FrameworkElement>();
        private readonly string[] pageTitles = { "總覽", "電源與 PD", "溫度", "電池健康", "30 分鐘趨勢", "每日資料", "智慧警示", "設定" };
        private readonly string[] pageSubtitles =
        {
            "目前電量、供電來源與需要留意的狀態",
            "供電來源、電腦耗電與電池流向",
            "CPU、NVIDIA 與電池感測來源",
            "設計容量、滿充容量、循環與續航估算",
            "最近 30 分鐘的功率與溫度變化",
            "每日一份資料，系統內保留最近七天",
            "只在狀態持續或確實需要處理時提醒",
            "USB-PD 參考值、警示門檻與程式偏好"
        };

        public Grid Root { get; private set; }

        private Grid contentHost;
        private TextBlock pageTitle;
        private TextBlock pageSubtitle;
        private TextBlock liveTime;
        private Border updateBanner;
        private Border updateBannerDot;
        private TextBlock updateBannerText;
        private TextBlock sidebarBattery;
        private TextBlock sidebarState;
        private TextBlock pdDetailText;
        private TextBlock powerSourceText;
        private TextBlock temperatureSourceText;
        private TextBlock batteryRuntimeText;
        private TextBlock batteryIdentityText;
        private TextBlock overviewStateValue;
        private TextBlock overviewStateNote;
        private TextBlock overviewChargeValue;
        private TextBlock overviewChargeNote;
        private TextBlock overviewChargeEtaValue;
        private TextBlock overviewChargeEtaNote;
        private TextBlock overviewRuntimeValue;
        private TextBlock overviewRuntimeNote;
        private TextBlock overviewSystemValue;
        private TextBlock overviewSystemNote;
        private TextBlock overviewTemperatureValue;
        private TextBlock overviewTemperatureNote;
        private TextBlock overviewUsageValue;
        private TextBlock overviewUsageNote;
        private TextBlock overviewMemoryValue;
        private TextBlock overviewMemoryNote;
        private TextBlock overviewStorageValue;
        private TextBlock overviewStorageNote;
        private TextBlock overviewBatteryValue;
        private TextBlock overviewBatteryNote;
        private TextBlock overviewLimitValue;
        private TextBlock overviewLimitNote;
        private Panel overviewLimitOptions;
        private StackPanel overviewLimitCustomRow;
        private Border overviewLimitCard;
        private DispatcherTimer limitSuccessTimer;
        private LinearGradientBrush limitSuccessBrush;
        private bool limitSuccessActive;
        private int limitApplyInProgress;
        private ToggleSwitch limitToggle;
        private TextBlock limitToggleLabel;
        private TextBlock batteryCareText;
        private Border batteryStatusFill;
        private Border batteryStatusTarget;
        private TextBlock batteryStatusPercent;
        private TextBlock batteryStatusState;
        private TextBlock batteryStatusFlow;
        private TextBlock batteryStatusLimit;
        private TextBlock batteryStatusHealth;
        private StackPanel overviewAlerts;
        private StackPanel alertsList;
        private StackPanel dailyList;
        private StackPanel topBarItemsPanel;
        private TelemetryChart powerChart;
        private TelemetryChart temperatureChart;
        private TelemetryChart trendChart;
        private ToggleSwitch shadowToggle;
        private ToggleSwitch alertsToggle;
        private ToggleSwitch topmostToggle;
        private ToggleSwitch startupToggle;
        private NumericStepper pdStepper;
        private NumericStepper cpuStepper;
        private NumericStepper gpuStepper;
        private BatterySnapshot latest;
        private UpdateInfo updateInfo;
        private IList<TelemetryPoint> latestPoints = new List<TelemetryPoint>();
        private int currentPage;
        private bool loadingDays;
        private DateTime lastDaysLoaded = DateTime.MinValue;

        public AdvancedDashboard(BatteryWindow window, AppSettings appSettings, TelemetryStore telemetryStore, RollingTelemetry telemetryHistory)
        {
            owner = window;
            settings = appSettings;
            store = telemetryStore;
            history = telemetryHistory;
            Root = BuildRoot();
            BuildPages();
            SelectPage(0, false);
        }

        public void PrepareForOpen(BatterySnapshot data, IList<TelemetryPoint> points)
        {
            if (data != null) Update(data, points);
            UpdateSettingsControls();
            if (currentPage == 5) LoadDailyRows();
        }

        public void FocusCurrentPage()
        {
            if (contentHost != null) contentHost.Focus();
        }

        public void UpdateUpdateStatus(UpdateInfo info)
        {
            updateInfo = info;
            if (updateBanner == null || updateBannerText == null) return;

            bool available = info != null && info.IsUpdateAvailable && !string.IsNullOrWhiteSpace(info.ReleaseUrl);
            updateBanner.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            updateBannerText.Text = available
                ? "可用更新"
                : string.Empty;
            if (updateBannerDot != null)
                updateBannerDot.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            updateBanner.ToolTip = available
                ? "可用更新 v" + info.LatestVersion + "，點擊開啟更新頁\n" + DisplayUpdateUrl(info.ReleaseUrl)
                : null;
        }

        public void Update(BatterySnapshot data, IList<TelemetryPoint> points)
        {
            if (data == null) return;
            latest = data;
            latestPoints = points ?? new List<TelemetryPoint>();

            liveTime.Text = "更新於 " + data.ReadAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            sidebarBattery.Text = data.Percent.HasValue ? Math.Round(data.Percent.Value).ToString("0", CultureInfo.InvariantCulture) + "%" : "--%";
            sidebarState.Text = data.IsCharging ? "充電中" : (data.IsAcLine ? "外接電源" : "電池供電");

            if (overviewStateValue != null)
            {
                overviewStateValue.Text = FormatPercent(data.Percent);
                overviewStateNote.Text = data.IsCharging ? "充電中" : (data.IsAcLine ? "外接電源" : "電池供電");
            }
            UpdateOverviewCards(data);
            UpdateOverviewLimitCard(data);
            UpdateBatteryLimitControls(data);

            SetMetric("battery", FormatPercent(data.Percent), data.StatusText);
            SetMetric("system", FormatValue(data.SystemWatts, "0.0", " W"), data.SystemWatts.HasValue ? data.SystemWattsSource : "--");
            SetMetric("cpu", FormatTemperature(data.CpuTempC), TemperatureState(data.CpuTempC, settings.CpuWarnC));
            SetMetric("gpu", FormatTemperature(data.GpuTempC), string.IsNullOrEmpty(data.GpuStatus) ? "未偵測" : data.GpuStatus);
            SetMetric("battery_temp", FormatTemperature(data.BatteryTempC), data.BatteryTempC.HasValue ? "電池感測器" : "硬體未提供");

            UpdatePowerSafe(data);
            UpdateBattery(data);
            UpdateBatteryLimitCareText(data);
            UpdateTemperatureSources(data);
            UpdateAlerts(data, latestPoints);

            if (powerChart != null) powerChart.SetPoints(latestPoints);
            if (temperatureChart != null) temperatureChart.SetPoints(latestPoints);
            if (trendChart != null) trendChart.SetPoints(latestPoints);

            if (currentPage == 5 && (DateTime.Now - lastDaysLoaded).TotalSeconds > 30) LoadDailyRows();
        }

        public void UpdateHistory(IList<TelemetryPoint> points)
        {
            latestPoints = points ?? new List<TelemetryPoint>();
            if (powerChart != null) powerChart.SetPoints(latestPoints);
            if (temperatureChart != null) temperatureChart.SetPoints(latestPoints);
            if (trendChart != null) trendChart.SetPoints(latestPoints);
        }

        private Grid BuildRoot()
        {
            var root = new Grid
            {
                Background = B("#FFD5D9DD"),
                Focusable = true,
                FocusVisualStyle = null
            };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border sidebar = BuildSidebar();
            root.Children.Add(sidebar);

            Grid main = BuildMainArea();
            Grid.SetColumn(main, 1);
            root.Children.Add(main);
            return root;
        }

        private Border BuildSidebar()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });

            var top = new Grid { Margin = new Thickness(17, 14, 14, 8) };
            top.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            top.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var sidebarActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sidebarActions.Children.Add(IconButton("↻", "立即更新", delegate { owner.DashboardRefresh(); }));
            liveTime = new TextBlock
            {
                Text = "等待資料",
                Foreground = B("#FF667078"),
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sidebarActions.Children.Add(liveTime);
            top.Children.Add(sidebarActions);

            var brand = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            brand.Children.Add(new TextBlock
            {
                Text = "BATTERY PULSE",
                Foreground = B("#FFF7FBF9"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            brand.Children.Add(new TextBlock
            {
                Text = "ADVANCED",
                Foreground = B("#FF9FD7C5"),
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetRow(brand, 1);
            top.Children.Add(brand);
            panel.Children.Add(top);

            var nav = new ElasticNavPanel
            {
                Margin = new Thickness(9, 2, 9, 8),
                ClipToBounds = true,
                MaxHeight = 600,
                VerticalAlignment = VerticalAlignment.Top
            };
            string[] titles = { "總覽", "電源與 PD", "溫度", "電池健康", "30 分鐘趨勢", "每日資料", "智慧警示", "設定" };
            string[] notes = { "", "", "", "", "", "", "", "" };
            string[] icons = { "⌂", "⌁", "°", "▣", "⌇", "↓", "!", "⚙" };
            for (int i = 0; i < titles.Length; i++)
            {
                AdvancedNavVisual visual = CreateNavItem(i, icons[i], titles[i], notes[i]);
                navVisuals.Add(visual);
                nav.Children.Add(visual.Root);
            }
            Grid.SetRow(nav, 1);
            panel.Children.Add(nav);

            var footer = new Grid { Margin = new Thickness(18, 10, 15, 14) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var state = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            state.Children.Add(new TextBlock { Text = "目前狀態", Foreground = B("#FFB7C8C1"), FontSize = 9.5 });
            sidebarState = new TextBlock { Text = "等待資料", Foreground = B("#FFF4F8F6"), FontSize = 11.5, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 4, 0, 0) };
            state.Children.Add(sidebarState);
            footer.Children.Add(state);
            sidebarBattery = new TextBlock
            {
                Text = "--%",
                Foreground = B("#FFB9F0DC"),
                FontSize = 22,
                FontWeight = FontWeights.Light,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sidebarBattery, 1);
            footer.Children.Add(sidebarBattery);
            Grid.SetRow(footer, 2);
            panel.Children.Add(footer);

            return new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = B("#2FFFFFFF"),
                Background = B("#1C10211C"),
                Child = panel
            };
        }

        private Grid BuildMainArea()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(25, 12, 25, 5), Cursor = Cursors.Arrow };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pageTitle = new TextBlock
            {
                Text = "總覽",
                Foreground = B("#FFF8FBFA"),
                FontSize = 23,
                FontWeight = FontWeights.SemiBold
            };
            pageSubtitle = new TextBlock
            {
                Text = pageSubtitles[0],
                Foreground = B("#FFC1CEC9"),
                FontSize = 10.5,
                Margin = new Thickness(0, 5, 0, 0)
            };
            // 展開頁以功能標題為主，避免重複解釋目前頁面已經呈現的內容。
            pageSubtitle.Visibility = Visibility.Collapsed;
            heading.Children.Add(pageTitle);
            heading.Children.Add(pageSubtitle);
            header.Children.Add(heading);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(WindowControlButton("□", "最大化／還原", delegate { owner.ToggleAdvancedSize(); }, false));
            right.Children.Add(WindowControlButton("×", "返回頂端狀態列", delegate { owner.ReturnToWidget(); }, true));
            right.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            Grid.SetColumn(right, 1);
            header.Children.Add(right);
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed) owner.DragAdvancedWindow();
            };
            root.Children.Add(header);

            contentHost = new Grid
            {
                Margin = new Thickness(25, 5, 25, 22),
                Focusable = true,
                FocusVisualStyle = null
            };
            Grid.SetRow(contentHost, 1);
            root.Children.Add(contentHost);
            return root;
        }

        private Border BuildUpdateBanner()
        {
            updateBannerText = new TextBlock
            {
                Text = string.Empty,
                Foreground = B("#FF3D454B"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            updateBannerDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = BatteryWindow.Brush("#FFEB5757"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(updateBannerDot);
            content.Children.Add(updateBannerText);

            var banner = new Border
            {
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 22),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#33908A8A"),
                Background = B("#22FFFFFF"),
                Cursor = Cursors.Hand,
                Child = content
            };
            banner.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (updateInfo != null) UpdateService.OpenUrl(updateInfo.ReleaseUrl);
            };
            return banner;
        }

        private static string DisplayUpdateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            return url.Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/');
        }

        private void BuildPages()
        {
            pages.Add(BuildOverviewPage());
            pages.Add(BuildPowerPage());
            pages.Add(BuildTemperaturePage());
            pages.Add(BuildBatteryPage());
            pages.Add(BuildTrendPage());
            pages.Add(BuildDataPage());
            pages.Add(BuildAlertsPage());
            pages.Add(BuildSettingsPage());
        }

        private FrameworkElement BuildOverviewPage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("核心狀態"));
            var metricsRow = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            metricsRow.Children.Add(OverviewSummaryTile("目前狀態", "#FF67D9B7", out overviewStateValue, out overviewStateNote));
            metricsRow.Children.Add(OverviewSummaryTile("充電瓦數", "#FF6FC4F2", out overviewChargeValue, out overviewChargeNote));
            metricsRow.Children.Add(OverviewSummaryTile("充電預估", "#FF6FC4F2", out overviewChargeEtaValue, out overviewChargeEtaNote));
            metricsRow.Children.Add(OverviewSummaryTile("可用時間", "#FF8AC7A8", out overviewRuntimeValue, out overviewRuntimeNote));
            metricsRow.Children.Add(OverviewSummaryTile("電腦耗電", "#FF8AC7A8", out overviewSystemValue, out overviewSystemNote));
            metricsRow.Children.Add(OverviewSummaryTile("溫度", "#FFFFC66D", out overviewTemperatureValue, out overviewTemperatureNote));
            metricsRow.Children.Add(OverviewSummaryTile("使用率", "#FF8DB6E8", out overviewUsageValue, out overviewUsageNote));
            metricsRow.Children.Add(OverviewSummaryTile("記憶體", "#FF9DB7D8", out overviewMemoryValue, out overviewMemoryNote));
            metricsRow.Children.Add(OverviewSummaryTile("儲存空間", "#FF9DB7D8", out overviewStorageValue, out overviewStorageNote));
            metricsRow.Children.Add(OverviewSummaryTile("電池健康", "#FFC6A0FF", out overviewBatteryValue, out overviewBatteryNote));
            metricsRow.Children.Add(BuildOverviewLimitTile());
            body.Children.Add(metricsRow);
            updateBanner = BuildUpdateBanner();
            body.Children.Add(updateBanner);

            body.Children.Add(SectionLabel("需要注意"));
            body.Children.Add(AlertBand("目前需要注意", out overviewAlerts));
            return PageScroll(body);
        }

        private FrameworkElement BuildPowerPage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("供電狀態"));
            var row = MetricGrid(4);
            row.Children.Add(MetricTile("pd_status", "供電來源", "#FF67D9B7"));
            row.Children.Add(MetricTile("pd_input", "電腦耗電", "#FF6FC4F2"));
            row.Children.Add(MetricTile("pd_margin", "供電餘裕", "#FFFFC66D"));
            row.Children.Add(MetricTile("battery_flow", "電池流向", "#FFC6A0FF"));
            body.Children.Add(row);

            var split = TwoColumnGrid();
            Border detail = InformationBand("判讀結果", "#FF67D9B7", out pdDetailText);
            Border source = InformationBand("測量方式", "#FF6FC4F2", out powerSourceText);
            split.Children.Add(detail);
            Grid.SetColumn(source, 1);
            split.Children.Add(source);
            body.Children.Add(split);

            body.Children.Add(SectionLabel("功率變化"));
            powerChart = new TelemetryChart(TelemetryChartMode.Power) { Height = 310 };
            body.Children.Add(ChartBand(powerChart, false));
            return PageScroll(body);
        }

        private FrameworkElement BuildTemperaturePage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("目前溫度"));
            var row = MetricGrid(3);
            row.Children.Add(MetricTile("cpu", "CPU 溫度", "#FFFFC66D"));
            row.Children.Add(MetricTile("gpu", "NVIDIA 溫度", "#FFC6A0FF"));
            row.Children.Add(MetricTile("battery_temp", "電池溫度", "#FF67D9B7"));
            body.Children.Add(row);

            body.Children.Add(InformationBand("感測來源", "#FF6FC4F2", out temperatureSourceText));
            body.Children.Add(SectionLabel("30 分鐘溫度變化"));
            temperatureChart = new TelemetryChart(TelemetryChartMode.Temperature) { Height = 340 };
            body.Children.Add(ChartBand(temperatureChart, false));
            return PageScroll(body);
        }

        private FrameworkElement BuildBatteryPage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("健康狀態"));
            body.Children.Add(BuildBatteryStatusVisual());
            var row = MetricGrid(4);
            row.Children.Add(MetricTile("battery_health", "健康度", "#FF67D9B7"));
            row.Children.Add(MetricTile("full_capacity", "目前滿充容量", "#FF6FC4F2"));
            row.Children.Add(MetricTile("design_capacity", "設計容量", "#FFFFC66D"));
            row.Children.Add(MetricTile("cycles", "循環次數", "#FFC6A0FF"));
            body.Children.Add(row);

            var split = TwoColumnGrid();
            Border runtime = InformationBand("續航／充滿估算", "#FF67D9B7", out batteryRuntimeText);
            Border identity = InformationBand("電池資訊", "#FF6FC4F2", out batteryIdentityText);
            split.Children.Add(runtime);
            Grid.SetColumn(identity, 1);
            split.Children.Add(identity);
            body.Children.Add(split);
            body.Children.Add(InformationBand("充電上限／電池保護", "#FF8AC7A8", out batteryCareText));
            return PageScroll(body);
        }

        private Border BuildBatteryStatusVisual()
        {
            var panel = new Grid { Height = 136 };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var visualColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = B("#FF7B858D"),
                Margin = new Thickness(0, 5, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            heading.Children.Add(new TextBlock
            {
                Text = "電池使用狀態",
                Foreground = B("#FF3D454B"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            });
            visualColumn.Children.Add(heading);

            var batteryGraphic = new Grid
            {
                Width = 198,
                Height = 58,
                Margin = new Thickness(13, 14, 0, 0),
                ClipToBounds = false
            };
            var batteryFrame = new Border
            {
                Width = 180,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = B("#FF7B858D"),
                Background = B("#12FFFFFF"),
                Padding = new Thickness(4),
                ClipToBounds = true
            };
            var batteryTrack = new Grid { Width = 170, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };
            batteryStatusFill = new Border
            {
                Width = 0,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(5),
                Background = B("#FF7B858D")
            };
            batteryTrack.Children.Add(batteryStatusFill);
            batteryStatusTarget = new Border
            {
                Width = 2,
                Height = 42,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = B("#FF3D454B"),
                Visibility = Visibility.Collapsed
            };
            batteryTrack.Children.Add(batteryStatusTarget);
            batteryFrame.Child = batteryTrack;
            batteryGraphic.Children.Add(batteryFrame);
            batteryGraphic.Children.Add(new Border
            {
                Width = 8,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(181, 0, 0, 0),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                Background = B("#FF7B858D")
            });
            visualColumn.Children.Add(batteryGraphic);
            visualColumn.Children.Add(new TextBlock
            {
                Text = "填色為目前電量，深色刻線為充電上限",
                Foreground = B("#FF6D757D"),
                FontSize = 9.5,
                Margin = new Thickness(13, 5, 0, 0)
            });
            panel.Children.Add(visualColumn);

            var details = new Grid { Margin = new Thickness(10, 25, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            batteryStatusPercent = new TextBlock
            {
                Text = "--%",
                Foreground = B("#FF252A2F"),
                FontSize = 23,
                FontWeight = FontWeights.Light
            };
            details.Children.Add(batteryStatusPercent);
            batteryStatusState = new TextBlock
            {
                Text = "--",
                Foreground = B("#FF6D757D"),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 4)
            };
            Grid.SetColumn(batteryStatusState, 1);
            details.Children.Add(batteryStatusState);

            batteryStatusFlow = StatusDetailText("電池流向  --");
            Grid.SetRow(batteryStatusFlow, 1);
            details.Children.Add(batteryStatusFlow);
            batteryStatusLimit = StatusDetailText("充電上限  --");
            Grid.SetRow(batteryStatusLimit, 1);
            Grid.SetColumn(batteryStatusLimit, 1);
            details.Children.Add(batteryStatusLimit);
            batteryStatusHealth = StatusDetailText("健康度  --");
            Grid.SetRow(batteryStatusHealth, 2);
            details.Children.Add(batteryStatusHealth);
            panel.Children.Add(details);
            Grid.SetColumn(details, 1);

            return new Border
            {
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(0, 0, 0, 22),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#27FFFFFF"),
                Background = B("#13FFFFFF"),
                Child = panel
            };
        }

        private static TextBlock StatusDetailText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = B("#FF6D757D"),
                FontSize = 10.5,
                Margin = new Thickness(0, 9, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private void UpdateBatteryLimitControls(BatterySnapshot data)
        {
            if (data == null) return;
            var capabilities = new BatteryLimitCapabilities
            {
                Mode = string.Equals(data.ChargeLimitMode, BatteryLimitControlMode.Threshold.ToString(), StringComparison.Ordinal)
                    ? BatteryLimitControlMode.Threshold
                    : BatteryLimitControlMode.Unsupported,
                CanWrite = data.ChargeLimitCanWrite,
                ProviderName = data.ChargeLimitProvider,
                Source = data.ChargeLimitSource,
                Note = data.ChargeLimitStateNote,
                Thresholds = data.ChargeLimitOptions ?? new int[0],
                CurrentPercent = data.ChargeLimitPercent.HasValue
                    ? (int?)Math.Round(data.ChargeLimitPercent.Value)
                    : null,
                LastAppliedPercent = null
            };
            // Do not expose a switch until the current state is readable. A
            // write-capable endpoint without read-back is not enough to prove
            // which state the switch should represent.
            bool showControls = capabilities.Supported && capabilities.CanWrite && capabilities.CurrentPercent.HasValue;
            UpdateBatteryLimitOptions(overviewLimitOptions, capabilities);
            if (overviewLimitCustomRow != null)
                overviewLimitCustomRow.Visibility = Visibility.Collapsed;
            if (overviewLimitCard != null)
                overviewLimitCard.Height = showControls ? 156 : 104;
        }

        private void UpdateBatteryLimitOptions(Panel options, BatteryLimitCapabilities capabilities)
        {
            if (options == null) return;
            bool available = capabilities.Supported && capabilities.CanWrite && capabilities.CurrentPercent.HasValue;
            if (!available)
            {
                options.Children.Clear();
                limitToggle = null;
                limitToggleLabel = null;
                return;
            }

            const int targetPercent = 80;
            bool enabled = capabilities.CurrentPercent.Value < 100;
            if (limitToggle != null && limitToggleLabel != null && options.Children.Count > 0)
            {
                limitToggle.SetState(enabled, false);
                limitToggleLabel.Text = enabled ? "開啟 · 限制 80%" : "關閉 · 充滿";
                limitToggle.Root.IsHitTestVisible = limitApplyInProgress == 0;
                return;
            }

            options.Children.Clear();
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var toggle = new ToggleSwitch(enabled);
            toggle.Root.Margin = new Thickness(0, 0, 9, 0);
            limitToggle = toggle;
            row.Children.Add(toggle.Root);
            var stateLabel = new TextBlock
            {
                Text = enabled ? "開啟 · 限制 80%" : "關閉 · 充滿",
                Foreground = B("#FF6D757D"),
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            limitToggleLabel = stateLabel;
            row.Children.Add(stateLabel);
            options.Children.Add(row);

            toggle.Changed += delegate(bool value)
            {
                // Keep the old visual state until the hardware read-back
                // succeeds. The green feedback is the success acknowledgement.
                toggle.SetState(!value, false);
                stateLabel.Text = !value ? "開啟 · 限制 80%" : "關閉 · 充滿";
                toggle.Root.IsHitTestVisible = false;
                ApplyBatteryLimit(value ? targetPercent : 100);
            };
        }

        private void ApplyBatteryLimit(int percent)
        {
            if (Interlocked.CompareExchange(ref limitApplyInProgress, 1, 0) != 0)
            {
                if (limitToggle != null) limitToggle.Root.IsHitTestVisible = true;
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                BatteryLimitApplyResult result = BatteryLimitController.Apply(percent);
                try
                {
                    owner.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Interlocked.Exchange(ref limitApplyInProgress, 0);
                        if (!result.Success)
                        {
                            if (limitToggle != null) limitToggle.Root.IsHitTestVisible = true;
                            RefreshFromLatest();
                            owner.DashboardRefresh();
                            MessageBox.Show(owner, result.Message, "Battery Pulse", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        settings.BatteryLimitPercent = percent;
                        settings.BatteryLimitHasApplied = true;
                        settings.Save();
                        if (limitToggle != null) limitToggle.Root.IsHitTestVisible = true;
                        ShowLimitSuccessFeedback();
                        owner.DashboardRefresh();
                    }), DispatcherPriority.Background);
                }
                catch
                {
                    Interlocked.Exchange(ref limitApplyInProgress, 0);
                }
            });
        }

        private void UpdateOverviewLimitCard(BatterySnapshot data)
        {
            if (overviewLimitValue == null || data == null) return;
            if (!data.ChargeLimitSupported || !data.ChargeLimitCanWrite)
            {
                overviewLimitValue.Text = data.ChargeLimitPercent.HasValue
                    ? FormatChargeLimitState(data.ChargeLimitPercent)
                    : "--";
                overviewLimitNote.Text = data.ChargeLimitPercent.HasValue
                    ? "僅讀取 · " + TextOrUnknown(data.ChargeLimitProvider)
                    : "--";
                return;
            }

            overviewLimitValue.Text = data.ChargeLimitPercent.HasValue
                ? FormatChargeLimitState(data.ChargeLimitPercent)
                : "--";
            overviewLimitNote.Text = data.ChargeLimitPercent.HasValue
                ? (data.ChargeLimitStateNote == "控制介面已確認" ? data.ChargeLimitStateNote : "目前讀值") + " · " + TextOrUnknown(data.ChargeLimitProvider)
                : "--";
        }

        private void ShowLimitSuccessFeedback()
        {
            if (overviewLimitCard == null) return;
            if (limitSuccessTimer != null) limitSuccessTimer.Stop();

            limitSuccessActive = true;
            limitSuccessBrush = new LinearGradientBrush(
                Color.FromArgb(170, 92, 200, 167),
                Color.FromArgb(45, 92, 200, 167),
                new Point(0, 0),
                new Point(1, 1))
            {
                Opacity = 0
            };
            overviewLimitCard.Background = limitSuccessBrush;
            limitSuccessBrush.BeginAnimation(Brush.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            limitSuccessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            limitSuccessTimer.Tick += delegate
            {
                limitSuccessTimer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                fade.Completed += delegate
                {
                    limitSuccessActive = false;
                    if (overviewLimitCard != null)
                        overviewLimitCard.Background = B("#17FFFFFF");
                };
                limitSuccessBrush.BeginAnimation(Brush.OpacityProperty, fade);
            };
            limitSuccessTimer.Start();
        }

        private void UpdateBatteryLimitCareText(BatterySnapshot data)
        {
            if (batteryCareText == null || data == null) return;
            if (!data.ChargeLimitSupported || !data.ChargeLimitCanWrite)
            {
                batteryCareText.Text = "未偵測到可控制的充電上限介面。請使用 ASUS／筆電原廠工具設定，BatteryPulse 不會自行猜測或改寫。";
                return;
            }

            string state = data.ChargeLimitPercent.HasValue
                ? FormatChargeLimitState(data.ChargeLimitPercent)
                : "--";
            string options = data.ChargeLimitOptions != null && data.ChargeLimitOptions.Length > 0
                ? "可用方案：" + string.Join("／", data.ChargeLimitOptions.Select(option => option >= 100 ? "100%／關閉" : option.ToString(CultureInfo.InvariantCulture) + "%").ToArray())
                : "可用方案由韌體回報";
            batteryCareText.Text = state + " · " + TextOrUnknown(data.ChargeLimitProvider) + "\n" + options;
        }

        private FrameworkElement BuildTrendPage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("最近 30 分鐘"));
            trendChart = new TelemetryChart(TelemetryChartMode.All) { Height = 480 };
            body.Children.Add(ChartBand(trendChart, true));
            return PageScroll(body);
        }

        private FrameworkElement BuildDataPage()
        {
            StackPanel body = PageBody();
            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(SectionLabel("每日資料"));
            TextBlock retention = new TextBlock
            {
                Text = "系統內保留 7 天",
                Foreground = B("#FFBBD0C7"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(retention, 1);
            header.Children.Add(retention);
            body.Children.Add(header);
            dailyList = new StackPanel();
            dailyList.Children.Add(EmptyState("正在整理每日資料…"));
            body.Children.Add(dailyList);
            return PageScroll(body);
        }

        private FrameworkElement BuildAlertsPage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("警示基準"));
            var row = MetricGrid(3);
            row.Children.Add(MetricTile("cpu_warn", "CPU 警示", "#FFFFC66D"));
            row.Children.Add(MetricTile("gpu_warn", "GPU 警示", "#FFC6A0FF"));
            row.Children.Add(MetricTile("retention", "資料保留", "#FF67D9B7"));
            body.Children.Add(row);
            body.Children.Add(SectionLabel("目前警示"));
            alertsList = new StackPanel();
            alertsList.Children.Add(EmptyState("目前沒有需要處理的警示"));
            body.Children.Add(alertsList);
            return PageScroll(body);
        }

        private FrameworkElement BuildSettingsPage()
        {
            StackPanel body = PageBody();
            body.Children.Add(SectionLabel("供電與溫度"));
            pdStepper = new NumericStepper(settings.PdWatts, 20, 240, 5, " W");
            pdStepper.ValueChanged += delegate(double value) { settings.PdWatts = value; settings.Save(); RefreshFromLatest(); };
            body.Children.Add(SettingRow("USB-PD 參考功率", "僅在未取得 PD 協議功率時作比較，不代表實際輸入", pdStepper.Root));
            cpuStepper = new NumericStepper(settings.CpuWarnC, 60, 100, 1, " °C");
            cpuStepper.ValueChanged += delegate(double value) { settings.CpuWarnC = value; settings.Save(); RefreshFromLatest(); };
            body.Children.Add(SettingRow("CPU 警示溫度", "持續超過此溫度才會列入智慧警示", cpuStepper.Root));
            gpuStepper = new NumericStepper(settings.GpuWarnC, 60, 100, 1, " °C");
            gpuStepper.ValueChanged += delegate(double value) { settings.GpuWarnC = value; settings.Save(); RefreshFromLatest(); };
            body.Children.Add(SettingRow("GPU 警示溫度", "僅使用 NVIDIA 獨顯核心溫度", gpuStepper.Root));

            body.Children.Add(SectionLabel("程式偏好"));
            alertsToggle = new ToggleSwitch(settings.AlertsEnabled);
            alertsToggle.Changed += delegate(bool value) { settings.AlertsEnabled = value; settings.Save(); RefreshFromLatest(); };
            body.Children.Add(SettingRow("智慧警示", "關閉後仍顯示數值，但不產生狀態警示", alertsToggle.Root));
            shadowToggle = new ToggleSwitch(settings.TextShadow);
            shadowToggle.Changed += delegate(bool value) { owner.DashboardSetTextShadow(value); };
            body.Children.Add(SettingRow("文字陰影", "提高桌布明暗變化時的可讀性", shadowToggle.Root));
            topmostToggle = new ToggleSwitch(owner.Topmost);
            topmostToggle.Changed += delegate(bool value) { owner.DashboardSetTopmost(value); };
            body.Children.Add(SettingRow("永遠置頂", "預設關閉，避免覆蓋其他工作視窗", topmostToggle.Root));
            startupToggle = new ToggleSwitch(StartupManager.IsEnabled());
            startupToggle.Changed += delegate(bool value) { owner.DashboardSetStartup(value); };
            body.Children.Add(SettingRow("開機啟動", "登入 Windows 後自動啟動小工具", startupToggle.Root));

            body.Children.Add(SectionLabel("頂端狀態列"));
            body.Children.Add(new TextBlock
            {
                Text = "只顯示有讀值的項目；可用上下按鈕調整順序。展開進階頁時頂端列仍會保留。",
                Foreground = B("#FF6D757D"),
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, -3, 0, 9)
            });
            topBarItemsPanel = new StackPanel();
            body.Children.Add(topBarItemsPanel);
            RefreshTopBarItemsPanel();

            body.Children.Add(SectionLabel("資料"));
            body.Children.Add(SettingRow("歷史資料", "每天一份 CSV，超過七天自動清理", ValuePill("7 天")));
            body.Children.Add(SettingRow("耗能累計", "重置今日與本月的累積瓦時", ActionButton("重置", delegate
            {
                if (MessageBox.Show(owner, "確定重置今日與本月耗能嗎？", "Battery Pulse", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    owner.DashboardResetEnergy();
            })));
            return PageScroll(body);
        }

        private void UpdateOverviewCards(BatterySnapshot data)
        {
            if (overviewChargeValue != null)
            {
                bool hasCharge = data.IsCharging && data.Watts.HasValue && data.Watts.Value > 0;
                overviewChargeValue.Text = hasCharge ? FormatValue(data.Watts, "0.0", " W") : "--";
                overviewChargeNote.Text = hasCharge ? "電池吸收" : "--";
            }
            if (overviewSystemValue != null)
            {
                overviewSystemValue.Text = FormatValue(data.SystemWatts, "0.0", " W");
                overviewSystemNote.Text = data.SystemWatts.HasValue ? data.SystemWattsSource : "--";
            }
            UpdateForecastCards(data);
            if (overviewTemperatureValue != null)
            {
                bool hasCpu = data.CpuTempC.HasValue;
                bool hasGpu = data.GpuTempC.HasValue;
                overviewTemperatureValue.Text = FormatTemperaturePair(data.CpuTempC, data.GpuTempC);
                overviewTemperatureNote.Text = (hasCpu || hasGpu) ? TemperatureOverviewState(data) : "--";
            }
            if (overviewUsageValue != null)
            {
                bool hasCpu = data.CpuUsagePercent.HasValue;
                bool hasGpu = data.GpuUsagePercent.HasValue;
                overviewUsageValue.Text = FormatUsagePair(data.CpuUsagePercent, data.GpuUsagePercent);
                overviewUsageNote.Text = (hasCpu || hasGpu) ? "CPU／GPU 即時" : "--";
            }
            if (overviewMemoryValue != null)
            {
                overviewMemoryValue.Text = FormatPercent(data.MemoryUsedPercent);
                overviewMemoryNote.Text = data.MemoryUsedMib.HasValue && data.MemoryTotalMib.HasValue
                    ? FormatMemory(data.MemoryUsedMib.Value, data.MemoryTotalMib.Value)
                    : "--";
            }
            if (overviewStorageValue != null)
            {
                overviewStorageValue.Text = FormatPercent(data.StorageUsedPercent);
                overviewStorageNote.Text = data.StorageUsedGiB.HasValue && data.StorageTotalGiB.HasValue
                    ? FormatStorage(data.StorageUsedGiB.Value, data.StorageFreeGiB, data.StorageTotalGiB.Value)
                    : "--";
            }
        }

        private void UpdateForecastCards(BatterySnapshot data)
        {
            if (overviewChargeEtaValue != null)
            {
                if (data.ChargeForecastState == "已充滿" || data.ChargeForecastState == "已達上限")
                {
                    overviewChargeEtaValue.Text = data.ChargeForecastState;
                    overviewChargeEtaNote.Text = ForecastTargetLabel(data);
                }
                else if (data.ChargeForecastState == "供電不足")
                {
                    overviewChargeEtaValue.Text = "無法充電";
                    overviewChargeEtaNote.Text = "外接電源下電池正在放電";
                }
                else if (data.ChargeEtaSeconds.HasValue && data.ChargeEtaSeconds.Value > 0)
                {
                    overviewChargeEtaValue.Text = "約 " + FormatDuration(TimeSpan.FromSeconds(data.ChargeEtaSeconds.Value));
                    overviewChargeEtaNote.Text = ForecastTargetLabel(data) + " · 保守估算";
                }
                else
                {
                    overviewChargeEtaValue.Text = "--";
                    overviewChargeEtaNote.Text = data.ChargeForecastState == "未接電" ? "未接電" : "--";
                }
            }

            if (overviewRuntimeValue != null)
            {
                if (data.RuntimeEtaSeconds.HasValue && data.RuntimeEtaSeconds.Value > 0)
                {
                    overviewRuntimeValue.Text = "約 " + FormatDuration(TimeSpan.FromSeconds(data.RuntimeEtaSeconds.Value));
                    overviewRuntimeNote.Text = data.IsAcLine ? "拔電後保守估算" : "依目前耗電保守估算";
                }
                else if (data.IsAcLine && data.ChargeForecastState != "供電不足")
                {
                    overviewRuntimeValue.Text = "外接電源";
                    overviewRuntimeNote.Text = "拔電後資料不足";
                }
                else
                {
                    overviewRuntimeValue.Text = "--";
                    overviewRuntimeNote.Text = "--";
                }
            }
        }

        private void UpdatePowerSafe(BatterySnapshot data)
        {
            double? computerWatts = data.SystemWatts.HasValue && data.SystemWatts.Value > 0
                ? data.SystemWatts
                : (double?)null;
            double? chargeWatts = data.IsCharging && data.Watts.HasValue && data.Watts.Value > 0
                ? data.Watts
                : (double?)null;
            double? estimatedLoad = computerWatts;
            if (computerWatts.HasValue && chargeWatts.HasValue)
                estimatedLoad = computerWatts.Value + chargeWatts.Value;

            double? ratedWatts = KnownAdapterWatts(data);
            string source = PowerSourceLabel(data);
            string sourceNote = data.IsAcLine
                ? (source == "--" ? "外接電源已連接 · 類型未取得" : "外接電源已連接")
                : "電池供電中";
            bool supplement = data.IsAcLine && data.Watts.HasValue && data.Watts.Value > 1 &&
                string.Equals(data.BatteryPowerMode, "放電", StringComparison.OrdinalIgnoreCase);

            string status;
            string note;
            if (!data.IsAcLine)
            {
                status = "電池供電";
                note = "目前未連接外部供電";
            }
            else if (supplement)
            {
                status = "電池仍在放電";
                note = "外接電源下仍由電池補足負載";
            }
            else if (ratedWatts.HasValue && estimatedLoad.HasValue)
            {
                double ratio = estimatedLoad.Value / ratedWatts.Value;
                if (ratio <= 0.75)
                {
                    status = "供電充足";
                    note = "額定 " + ratedWatts.Value.ToString("0", CultureInfo.InvariantCulture) + " W";
                }
                else if (ratio <= 0.92)
                {
                    status = "接近額定功率";
                    note = "高負載時充電速度可能下降";
                }
                else
                {
                    status = "接近或超過額定功率";
                    note = "請確認供電器與負載";
                }
            }
            else
            {
                status = "供電來源已連接";
                note = "未取得充電器額定功率";
            }

            SetMetric("pd_status", source, status);
            SetMetric("pd_input", FormatValue(computerWatts, "0.0", " W"),
                computerWatts.HasValue ? data.SystemWattsSource : "--");

            string margin = ratedWatts.HasValue && estimatedLoad.HasValue
                ? FormatSigned(ratedWatts.Value - estimatedLoad.Value, " W")
                : "--";
            string marginNote = ratedWatts.HasValue
                ? "額定 " + ratedWatts.Value.ToString("0", CultureInfo.InvariantCulture) + " W"
                : (data.ChargerType == "USB-PD"
                    ? "協議功率未取得 · 參考值不納入計算"
                    : "額定功率未取得");
            SetMetric("pd_margin", margin, marginNote);

            string flow = data.Watts.HasValue && data.Watts.Value > 0
                ? (string.IsNullOrEmpty(data.BatteryPowerMode) ? "電池" : data.BatteryPowerMode) + " " + data.Watts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W"
                : "--";
            SetMetric("battery_flow", flow, data.IsAcLine ? "外接電源中" : "電池供電中");

            string detail = status + "\n" + note;
            if (computerWatts.HasValue)
                detail += "\n電腦耗電 " + computerWatts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W";
            if (chargeWatts.HasValue)
                detail += " · 電池吸收 " + chargeWatts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W";
            detail += ratedWatts.HasValue
                ? "\n額定功率 " + ratedWatts.Value.ToString("0", CultureInfo.InvariantCulture) + " W"
                : "\n額定功率 --";
            if (data.ChargerType == "USB-PD" && !ratedWatts.HasValue)
                detail += "\nUSB-PD 參考值 " + settings.PdWatts.ToString("0", CultureInfo.InvariantCulture) + " W，未視為實際功率";
            pdDetailText.Text = detail;

            string chargerSource = string.IsNullOrWhiteSpace(data.AdapterPowerSource)
                ? data.ChargerTypeSource
                : data.AdapterPowerSource;
            if (string.IsNullOrWhiteSpace(chargerSource) || chargerSource == "尚未取得來源") chargerSource = "--";
            powerSourceText.Text = "供電來源：" + source + " · " + chargerSource + "\n" +
                "電腦耗電：" + (computerWatts.HasValue ? data.SystemWattsSource : "--") + "\n" +
                "電池流向：Windows BatteryStatus / ChargeRate\n" +
                "額定功率：" + (ratedWatts.HasValue ? "硬體回報" : "--，未取得");
        }

        private static string PowerSourceLabel(BatterySnapshot data)
        {
            if (data == null || !data.IsAcLine) return "電池";
            if (string.Equals(data.ChargerType, "USB-PD", StringComparison.OrdinalIgnoreCase)) return "USB-PD";
            if (string.Equals(data.ChargerType, "原廠充電器", StringComparison.OrdinalIgnoreCase)) return "原廠 AC";
            return "--";
        }

        private static double? KnownAdapterWatts(BatterySnapshot data)
        {
            if (data == null) return null;
            if (data.AdapterRatedWatts.HasValue && data.AdapterRatedWatts.Value > 0)
                return data.AdapterRatedWatts.Value;
            if (data.PdNegotiatedWatts.HasValue && data.PdNegotiatedWatts.Value > 0)
                return data.PdNegotiatedWatts.Value;
            return null;
        }

        private void UpdateBattery(BatterySnapshot data)
        {
            double? health = null;
            if (data.DesignCapacityMwh.HasValue && data.FullChargeCapacityMwh.HasValue && data.DesignCapacityMwh.Value > 0)
                health = data.FullChargeCapacityMwh.Value / data.DesignCapacityMwh.Value * 100.0;
            if (overviewBatteryValue != null)
            {
                overviewBatteryValue.Text = FormatPercent(health);
                overviewBatteryNote.Text = health.HasValue ? BatteryHealthState(health.Value) : "健康度未取得";
            }
            SetMetric("battery_health", FormatPercent(health), health.HasValue ? BatteryHealthState(health.Value) : "硬體未提供完整容量");
            SetMetric("full_capacity", FormatCapacity(data.FullChargeCapacityMwh), "Windows WMI 回報");
            SetMetric("design_capacity", FormatCapacity(data.DesignCapacityMwh), "原廠設計值");
            SetMetric("cycles", data.CycleCount.HasValue ? data.CycleCount.Value.ToString("0", CultureInfo.InvariantCulture) + " 次" : "--", data.CycleCount.HasValue ? "Windows WMI 回報" : "硬體未提供");

            UpdateBatteryStatusVisual(data, health);

            batteryIdentityText.Text = "名稱：" + TextOrUnknown(data.BatteryName) + "\n製造商：" + TextOrUnknown(data.BatteryManufacturer) + "\n電壓：" + (data.VoltageMv.HasValue ? (data.VoltageMv.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V" : "硬體未提供");
            batteryRuntimeText.Text = RuntimeEstimate(data);
        }

        private void UpdateBatteryStatusVisual(BatterySnapshot data, double? health)
        {
            if (data == null || batteryStatusFill == null) return;

            double percent = data.Percent.HasValue ? Math.Max(0, Math.Min(100, data.Percent.Value)) : 0;
            batteryStatusFill.Width = 170 * percent / 100.0;
            batteryStatusPercent.Text = FormatPercent(data.Percent);
            batteryStatusState.Text = data.IsCharging ? "充電中" : (data.IsAcLine ? "外接電源" : "電池供電");

            bool hasFlow = data.Watts.HasValue && data.Watts.Value > 0;
            if (!hasFlow)
            {
                batteryStatusFlow.Text = "電池流向  --";
            }
            else
            {
                string flow = string.Equals(data.BatteryPowerMode, "放電", StringComparison.OrdinalIgnoreCase)
                    ? "放電 "
                    : (string.Equals(data.BatteryPowerMode, "充電", StringComparison.OrdinalIgnoreCase) ? "吸收 " : "流向 ");
                batteryStatusFlow.Text = "電池流向  " + flow + data.Watts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W";
            }

            if (data.ChargeLimitPercent.HasValue)
            {
                double limit = Math.Max(0, Math.Min(100, data.ChargeLimitPercent.Value));
                batteryStatusLimit.Text = limit >= 100
                    ? "充電上限  關閉"
                    : "充電上限  " + limit.ToString("0", CultureInfo.InvariantCulture) + "%";
                batteryStatusTarget.Visibility = Visibility.Visible;
                batteryStatusTarget.Margin = new Thickness(Math.Max(0, Math.Min(168, 168 * limit / 100.0 - 1)), 0, 0, 0);
            }
            else
            {
                batteryStatusLimit.Text = "充電上限  --";
                batteryStatusTarget.Visibility = Visibility.Collapsed;
            }

            batteryStatusHealth.Text = "健康度  " + (health.HasValue ? health.Value.ToString("0", CultureInfo.InvariantCulture) + "%" : "--");
        }

        private void UpdateTemperatureSources(BatterySnapshot data)
        {
            string cpuSource = string.IsNullOrWhiteSpace(data.CpuTempSource) ? "沒有可用感測器" : data.CpuTempSource;
            bool acpi = cpuSource.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0;
            string cpuAccuracy = acpi ? "ACPI 可能是系統熱區，僅供趨勢參考" : "處理器感測器讀值";
            string gpuSource = string.IsNullOrWhiteSpace(data.GpuTempSource) ? "沒有核心溫度；目前狀態為「" + data.GpuStatus + "」" : data.GpuTempSource;
            temperatureSourceText.Text = "CPU：" + cpuSource + "\n" + cpuAccuracy + "\n\nNVIDIA：" + gpuSource + "\n獨顯待機或重新啟用時，每 60 秒自動重新掃描感測器。";
        }

        private void UpdateAlerts(BatterySnapshot data, IList<TelemetryPoint> points)
        {
            SetMetric("cpu_warn", settings.CpuWarnC.ToString("0", CultureInfo.InvariantCulture) + " °C", "持續超過才提醒");
            SetMetric("gpu_warn", settings.GpuWarnC.ToString("0", CultureInfo.InvariantCulture) + " °C", "NVIDIA GPU Core");
            SetMetric("retention", "7 天", "每日一份 CSV");

            var alerts = new List<DashboardAlert>();
            if (settings.AlertsEnabled)
            {
                if (IsSustained(points, true, settings.CpuWarnC)) alerts.Add(new DashboardAlert("CPU 溫度持續偏高", "已連續接近或超過 " + settings.CpuWarnC.ToString("0", CultureInfo.InvariantCulture) + " °C", "#FFFFC66D"));
                if (IsSustained(points, false, settings.GpuWarnC)) alerts.Add(new DashboardAlert("NVIDIA 溫度持續偏高", "已連續接近或超過 " + settings.GpuWarnC.ToString("0", CultureInfo.InvariantCulture) + " °C", "#FFC6A0FF"));
                if (data.IsAcLine && data.Watts.HasValue && data.Watts.Value > 1 && string.Equals(data.BatteryPowerMode, "放電", StringComparison.OrdinalIgnoreCase))
                    alerts.Add(new DashboardAlert("外接電源時電池仍在放電", "目前負載可能高於充電器可供功率", "#FFFF8A80"));
                if (data.Percent.HasValue && data.Percent.Value <= 15 && !data.IsCharging)
                    alerts.Add(new DashboardAlert("電量偏低", "目前剩餘 " + data.Percent.Value.ToString("0", CultureInfo.InvariantCulture) + "%", "#FFFF8A80"));
                if (data.DesignCapacityMwh.HasValue && data.FullChargeCapacityMwh.HasValue && data.DesignCapacityMwh.Value > 0)
                {
                    double health = data.FullChargeCapacityMwh.Value / data.DesignCapacityMwh.Value * 100.0;
                    if (health < 80) alerts.Add(new DashboardAlert("電池健康度低於 80%", "目前約 " + health.ToString("0", CultureInfo.InvariantCulture) + "%", "#FFFFC66D"));
                }
            }
            RenderAlerts(overviewAlerts, alerts, true);
            RenderAlerts(alertsList, alerts, false);
        }

        private void RenderAlerts(StackPanel panel, IList<DashboardAlert> alerts, bool compact)
        {
            if (panel == null) return;
            panel.Children.Clear();
            if (alerts.Count == 0)
            {
                panel.Children.Add(EmptyState(settings.AlertsEnabled ? "目前沒有需要處理的警示" : "智慧警示已關閉"));
                return;
            }
            foreach (DashboardAlert alert in alerts)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, compact ? 7 : 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = B(alert.Color), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = alert.Title, Foreground = B("#FFF4F8F6"), FontSize = compact ? 10.5 : 12, FontWeight = FontWeights.Medium });
                text.Children.Add(new TextBlock { Text = alert.Note, Foreground = B("#FFB8C8C1"), FontSize = compact ? 9 : 10, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                panel.Children.Add(row);
            }
        }

        private void SelectPage(int index, bool animate)
        {
            if (index < 0 || index >= pages.Count) return;
            currentPage = index;
            pageTitle.Text = pageTitles[index];
            pageSubtitle.Text = pageSubtitles[index];
            contentHost.Children.Clear();
            FrameworkElement page = pages[index];
            contentHost.Children.Add(page);
            bool useMotion = animate && SystemParameters.ClientAreaAnimation;
            page.Opacity = useMotion ? 0 : 1;
            page.RenderTransform = new TranslateTransform(0, useMotion ? 10 : 0);
            if (useMotion)
            {
                page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            UpdateNavSelection();
            if (index == 5) LoadDailyRows();
            if (index == 7) UpdateSettingsControls();
        }

        private void UpdateNavSelection()
        {
            for (int i = 0; i < navVisuals.Count; i++) navVisuals[i].SetSelected(i == currentPage);
        }

        private AdvancedNavVisual CreateNavItem(int index, string icon, string title, string note)
        {
            var visual = new AdvancedNavVisual(icon, title, note);
            visual.Root.MouseLeftButtonUp += delegate { SelectPage(index, true); };
            visual.Root.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                SelectPage(index, true);
            };
            return visual;
        }

        private void LoadDailyRows()
        {
            if (dailyList == null || loadingDays) return;
            loadingDays = true;
            dailyList.Children.Clear();
            dailyList.Children.Add(EmptyState("正在整理每日資料…"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                IList<DailyTelemetrySummary> summaries = store.GetDailySummaries();
                try
                {
                    Root.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        loadingDays = false;
                        lastDaysLoaded = DateTime.Now;
                        RenderDailyRows(summaries);
                    }));
                }
                catch { loadingDays = false; }
            });
        }

        private void RenderDailyRows(IList<DailyTelemetrySummary> summaries)
        {
            dailyList.Children.Clear();
            if (summaries == null || summaries.Count == 0)
            {
                dailyList.Children.Add(EmptyState("尚無歷史資料"));
                return;
            }
            foreach (DailyTelemetrySummary summary in summaries)
            {
                var row = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 9),
                    Height = 74,
                    Background = B("#14FFFFFF")
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
                row.Children.Add(DailyCell(summary.Date.ToString("MM 月 dd 日", CultureInfo.InvariantCulture), summary.Date.ToString("dddd", new CultureInfo("zh-TW")), 0));
                row.Children.Add(DailyCell(FormatValue(summary.AverageSystemWatts, "0.0", " W"), "平均耗電", 1));
                row.Children.Add(DailyCell(FormatDailyTemperatures(summary.MaxCpuTempC, summary.MaxGpuTempC), "CPU／GPU 最高", 2));
                row.Children.Add(DailyCell(FormatEnergy(summary.EnergyWh), summary.Samples.ToString(CultureInfo.InvariantCulture) + " 筆", 3));
                FrameworkElement download = ActionButton("↓ 下載", delegate { ExportDay(summary); });
                download.HorizontalAlignment = HorizontalAlignment.Right;
                download.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(download, 4);
                row.Children.Add(download);
                dailyList.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = B("#26FFFFFF"),
                    Child = row
                });
            }
        }

        private void ExportDay(DailyTelemetrySummary summary)
        {
            var dialog = new SaveFileDialog
            {
                Title = "下載每日資料",
                Filter = "CSV 資料檔 (*.csv)|*.csv",
                FileName = "BatteryPulse_" + summary.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) dialog.InitialDirectory = downloads;
            bool? result = dialog.ShowDialog(owner);
            if (result != true) return;
            if (!store.Export(summary.FilePath, dialog.FileName))
                MessageBox.Show(owner, "資料下載失敗，請確認目的資料夾是否可寫入。", "Battery Pulse", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateSettingsControls()
        {
            if (pdStepper != null) pdStepper.SetValue(settings.PdWatts, false);
            if (cpuStepper != null) cpuStepper.SetValue(settings.CpuWarnC, false);
            if (gpuStepper != null) gpuStepper.SetValue(settings.GpuWarnC, false);
            if (alertsToggle != null) alertsToggle.SetState(settings.AlertsEnabled, false);
            if (shadowToggle != null) shadowToggle.SetState(settings.TextShadow, false);
            if (topmostToggle != null) topmostToggle.SetState(owner.Topmost, false);
            if (startupToggle != null) startupToggle.SetState(StartupManager.IsEnabled(), false);
            RefreshTopBarItemsPanel();
        }

        private void RefreshTopBarItemsPanel()
        {
            if (topBarItemsPanel == null) return;
            topBarItemsPanel.Children.Clear();
            foreach (string id in settings.GetTopBarItems())
            {
                string itemId = id;
                var row = new Grid
                {
                    Height = 42,
                    Margin = new Thickness(0, 0, 0, 5),
                    Background = B("#10FFFFFF")
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock
                {
                    Text = AppSettings.TopBarItemLabel(itemId),
                    Foreground = B("#FF252A2F"),
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 8, 0)
                });

                var toggle = new ToggleSwitch(settings.IsTopBarItemEnabled(itemId));
                toggle.Changed += delegate(bool value)
                {
                    settings.SetTopBarItemEnabled(itemId, value);
                    settings.Save();
                };
                Grid.SetColumn(toggle.Root, 1);
                row.Children.Add(toggle.Root);

                var order = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 8, 0) };
                Border up = ActionButton("↑", delegate { MoveTopBarItem(itemId, -1); });
                Border down = ActionButton("↓", delegate { MoveTopBarItem(itemId, 1); });
                up.Width = 28;
                down.Width = 28;
                up.Margin = new Thickness(0, 0, 4, 0);
                order.Children.Add(up);
                order.Children.Add(down);
                Grid.SetColumn(order, 2);
                row.Children.Add(order);
                topBarItemsPanel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = B("#16FFFFFF"),
                    Child = row
                });
            }
        }

        private void MoveTopBarItem(string id, int direction)
        {
            List<string> items = settings.GetTopBarItems();
            int index = items.IndexOf(id);
            int next = index + direction;
            if (index < 0 || next < 0 || next >= items.Count) return;
            string moved = items[index];
            items[index] = items[next];
            items[next] = moved;
            settings.TopBarItems = string.Join(",", items);
            settings.Save();
            RefreshTopBarItemsPanel();
        }

        private void RefreshFromLatest()
        {
            if (latest != null) Update(latest, latestPoints);
        }

        private Border MetricTile(string key, string label, string accent)
        {
            var root = new Border
            {
                Width = 230,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(16, 14, 16, 13),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#2BFFFFFF"),
                Background = B("#17FFFFFF")
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelText = new TextBlock { Text = label, Foreground = B("#FFBECBC6"), FontSize = 10.5 };
            grid.Children.Add(labelText);
            var value = new TextBlock
            {
                Text = "--",
                Foreground = B("#FFF8FBFA"),
                FontSize = 24,
                FontWeight = FontWeights.Light,
                Margin = new Thickness(0, 9, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(value, 1);
            grid.Children.Add(value);
            var note = new TextBlock
            {
                Text = "等待資料",
                Foreground = B("#FFB5BDC4"),
                FontSize = 9.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(note, 2);
            grid.Children.Add(note);
            root.Child = grid;
            RegisterMetric(key, new MetricView(value, note));
            root.MouseEnter += delegate { root.Background = B("#22FFFFFF"); };
            root.MouseLeave += delegate { root.Background = B("#17FFFFFF"); };
            return root;
        }

        private Border BuildOverviewLimitTile()
        {
            var panel = new StackPanel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = B("#FF8AC7A8"),
                Margin = new Thickness(0, 5, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            heading.Children.Add(new TextBlock
            {
                Text = "充電上限",
                Foreground = B("#FF6D757D"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(heading);

            overviewLimitValue = new TextBlock
            {
                Text = "--",
                Foreground = B("#FF252A2F"),
                FontSize = 20,
                FontWeight = FontWeights.Light,
                Margin = new Thickness(0, 10, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(overviewLimitValue);

            overviewLimitNote = new TextBlock
            {
                Text = "--",
                Foreground = B("#FF6D757D"),
                FontSize = 9.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(overviewLimitNote);

            overviewLimitOptions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(overviewLimitOptions);

            overviewLimitCustomRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 9, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var customStepper = new NumericStepper(settings.BatteryLimitPercent, 40, 100, 1, "%");
            overviewLimitCustomRow.Children.Add(customStepper.Root);
            Border applyCustom = ActionButton("套用自訂", delegate
            {
                ApplyBatteryLimit((int)Math.Round(customStepper.Value));
            });
            applyCustom.Margin = new Thickness(6, 0, 0, 0);
            overviewLimitCustomRow.Children.Add(applyCustom);
            panel.Children.Add(overviewLimitCustomRow);

            overviewLimitCard = new Border
            {
                Width = 310,
                Height = 104,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(16, 14, 16, 13),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#2BFFFFFF"),
                Background = B("#17FFFFFF"),
                Child = panel
            };
            overviewLimitCard.MouseEnter += delegate
            {
                if (!limitSuccessActive) overviewLimitCard.Background = B("#22FFFFFF");
            };
            overviewLimitCard.MouseLeave += delegate
            {
                if (!limitSuccessActive) overviewLimitCard.Background = B("#17FFFFFF");
            };
            return overviewLimitCard;
        }

        private static Border OverviewSummaryTile(string title, string accent, out TextBlock value, out TextBlock note)
        {
            var panel = new StackPanel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = B(accent),
                Margin = new Thickness(0, 5, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            heading.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = B("#FF6D757D"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(heading);

            value = new TextBlock
            {
                Text = "--",
                Foreground = B("#FF252A2F"),
                FontSize = 20,
                FontWeight = FontWeights.Light,
                Margin = new Thickness(0, 10, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(value);

            note = new TextBlock
            {
                Text = "尚未取得",
                Foreground = B("#FF6D757D"),
                FontSize = 9.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(note);

            var root = new Border
            {
                Width = 220,
                Height = 108,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(16, 14, 16, 13),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#2BFFFFFF"),
                Background = B("#17FFFFFF"),
                Child = panel
            };
            root.MouseEnter += delegate { root.Background = B("#22FFFFFF"); };
            root.MouseLeave += delegate { root.Background = B("#17FFFFFF"); };
            return root;
        }

        private void RegisterMetric(string key, MetricView view)
        {
            List<MetricView> list;
            if (!metrics.TryGetValue(key, out list))
            {
                list = new List<MetricView>();
                metrics[key] = list;
            }
            list.Add(view);
        }

        private void SetMetric(string key, string value, string note)
        {
            List<MetricView> list;
            if (!metrics.TryGetValue(key, out list)) return;
            foreach (MetricView view in list)
            {
                view.Value.Text = value;
                view.Note.Text = note;
            }
        }

        private static StackPanel PageBody()
        {
            return new StackPanel { Margin = new Thickness(0, 0, 9, 18) };
        }

        private static ScrollViewer PageScroll(FrameworkElement content)
        {
            var scroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly
            };
            scroll.Resources.Add(typeof(ScrollBar), SlimScrollBarStyle());
            return scroll;
        }

        private static Style SlimScrollBarStyle()
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(ScrollBar.WidthProperty, 10.0));
            style.Setters.Add(new Setter(ScrollBar.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(ScrollBar.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ScrollBar.TemplateProperty, SlimScrollBarTemplate()));
            return style;
        }

        private static ControlTemplate SlimScrollBarTemplate()
        {
            return (ControlTemplate)XamlReader.Parse(
                @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                    TargetType='{x:Type ScrollBar}'>
                    <Grid Background='Transparent'>
                        <Track x:Name='PART_Track'
                               Orientation='{TemplateBinding Orientation}'
                               Minimum='{TemplateBinding Minimum}'
                               Maximum='{TemplateBinding Maximum}'
                               Value='{TemplateBinding Value}'
                               ViewportSize='{TemplateBinding ViewportSize}'
                               IsDirectionReversed='True'>
                            <Track.Thumb>
                                <Thumb Width='6' Opacity='0.82'>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType='{x:Type Thumb}'>
                                            <Border Width='6' HorizontalAlignment='Center'
                                                    Background='#FF8F969D' CornerRadius='4'/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>");
        }

        private static TextBlock SectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = B("#FFE8F0EC"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private static UniformGrid MetricGrid(int columns)
        {
            return new UniformGrid { Columns = columns, Rows = 1, Margin = new Thickness(0, 0, 0, 22) };
        }

        private static Grid TwoColumnGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static Border InformationBand(string title, string accent, out TextBlock body)
        {
            var panel = new StackPanel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = B(accent), Margin = new Thickness(0, 5, 8, 0), VerticalAlignment = VerticalAlignment.Top });
            heading.Children.Add(new TextBlock { Text = title, Foreground = B("#FFF2F7F5"), FontSize = 11.5, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(heading);
            body = new TextBlock
            {
                Text = "等待資料",
                Foreground = B("#FFC3D0CB"),
                FontSize = 10.5,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(13, 11, 0, 0)
            };
            panel.Children.Add(body);
            return new Border
            {
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 10, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#27FFFFFF"),
                Background = B("#13FFFFFF"),
                MinHeight = 128,
                Child = panel
            };
        }

        private static Border AlertBand(string title, out StackPanel list)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, Foreground = B("#FFF2F7F5"), FontSize = 11.5, FontWeight = FontWeights.SemiBold });
            list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(list);
            return new Border
            {
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(0, 0, 0, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#27FFFFFF"),
                Background = B("#13FFFFFF"),
                MinHeight = 76,
                Child = panel
            };
        }

        private static Border ChartBand(TelemetryChart chart, bool showFullLegend)
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var legend = new StackPanel { Orientation = Orientation.Horizontal };
            if (showFullLegend || chart.Mode == TelemetryChartMode.Power)
            {
                legend.Children.Add(Legend("電腦耗電", "#FF475569", DashStyles.Solid));
                legend.Children.Add(Legend("電池功率", "#FFD97706", DashStyles.Solid));
            }
            if (showFullLegend || chart.Mode == TelemetryChartMode.Temperature)
            {
                legend.Children.Add(Legend("CPU", "#FF0878B9", DashStyles.Dash));
                legend.Children.Add(Legend("NVIDIA", "#FF7C3AED", DashStyles.Dot));
            }
            panel.Children.Add(legend);
            Grid.SetRow(chart, 1);
            panel.Children.Add(chart);
            return new Border
            {
                Padding = new Thickness(15, 12, 15, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#27FFFFFF"),
                Background = B("#12FFFFFF"),
                Child = panel
            };
        }

        private static FrameworkElement Legend(string text, string color, DashStyle dashStyle)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            row.Children.Add(new LegendMark(color, dashStyle));
            row.Children.Add(new TextBlock { Text = text, Foreground = B("#FFBFCBC6"), FontSize = 9.5, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private static Border SettingRow(string title, string note, FrameworkElement control)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, Foreground = B("#FFF3F7F5"), FontSize = 11.5, FontWeight = FontWeights.Medium });
            text.Children.Add(new TextBlock { Text = note, Foreground = B("#FFABBBB4"), FontSize = 9.5, Margin = new Thickness(0, 4, 0, 0) });
            grid.Children.Add(text);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return new Border
            {
                Padding = new Thickness(15, 13, 15, 13),
                Margin = new Thickness(0, 0, 0, 7),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#21FFFFFF"),
                Background = B("#10FFFFFF"),
                Child = grid
            };
        }

        private static Border ValuePill(string text)
        {
            return new Border
            {
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(8),
                Background = B("#20FFFFFF"),
                Child = new TextBlock { Text = text, Foreground = B("#FFF2F7F5"), FontSize = 10.5, FontWeight = FontWeights.Medium }
            };
        }

        private static Border ActionButton(string text, Action action)
        {
            var button = new Border
            {
                Padding = new Thickness(12, 7, 12, 7),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#34FFFFFF"),
                Background = B("#1FFFFFFF"),
                Focusable = true,
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = B("#FFF1F7F4"), FontSize = 10.5, FontWeight = FontWeights.Medium }
            };
            button.MouseEnter += delegate { button.Background = B("#32FFFFFF"); };
            button.MouseLeave += delegate { button.Background = B("#1FFFFFFF"); };
            button.GotKeyboardFocus += delegate { button.BorderBrush = B("#BFFFFFFF"); };
            button.LostKeyboardFocus += delegate { button.BorderBrush = B("#34FFFFFF"); };
            button.MouseLeftButtonUp += delegate { if (action != null) action(); };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                if (action != null) action();
            };
            return button;
        }

        private static Border IconButton(string glyph, string tooltip, Action action)
        {
            var button = new Border
            {
                Width = 31,
                Height = 31,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = B("#2FFFFFFF"),
                Background = B("#18FFFFFF"),
                Focusable = true,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = new TextBlock { Text = glyph, Foreground = B("#FFEAF2EE"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            button.MouseEnter += delegate { button.Background = B("#30FFFFFF"); };
            button.MouseLeave += delegate { button.Background = B("#18FFFFFF"); };
            button.GotKeyboardFocus += delegate { button.BorderBrush = B("#BFFFFFFF"); };
            button.LostKeyboardFocus += delegate { button.BorderBrush = B("#2FFFFFFF"); };
            button.MouseLeftButtonUp += delegate { if (action != null) action(); };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                if (action != null) action();
            };
            return button;
        }

        private static Border WindowControlButton(string glyph, string tooltip, Action action, bool closeButton)
        {
            var label = new TextBlock
            {
                Text = glyph,
                Foreground = B("#FF667078"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var target = new Border
            {
                Width = 34,
                Height = 28,
                Margin = new Thickness(2, 0, 0, 0),
                Focusable = true,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Child = label
            };
            target.MouseEnter += delegate
            {
                target.Background = B(closeButton ? "#32D85B5B" : "#20FFFFFF");
                label.Foreground = B(closeButton ? "#FFFFFFFF" : "#FF30383E");
            };
            target.MouseLeave += delegate
            {
                target.Background = Brushes.Transparent;
                label.Foreground = B("#FF667078");
            };
            target.MouseLeftButtonUp += delegate { if (action != null) action(); };
            target.GotKeyboardFocus += delegate { target.BorderBrush = B("#80667078"); };
            target.LostKeyboardFocus += delegate { target.BorderBrush = Brushes.Transparent; };
            target.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                if (action != null) action();
            };
            return target;
        }

        private static FrameworkElement EmptyState(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = B("#FFB5C5BE"),
                FontSize = 10.5,
                Padding = new Thickness(0, 12, 0, 12)
            };
        }

        private static FrameworkElement DailyCell(string value, string note, int column)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
            panel.Children.Add(new TextBlock { Text = value, Foreground = B("#FFF1F6F3"), FontSize = 11.5, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(new TextBlock { Text = note, Foreground = B("#FFADBCB5"), FontSize = 9, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(panel, column);
            return panel;
        }

        private static bool IsSustained(IList<TelemetryPoint> points, bool cpu, double threshold)
        {
            DateTime cutoff = DateTime.Now.AddSeconds(-20);
            IList<TelemetryPoint> recent = points.Where(delegate(TelemetryPoint p) { return p.At >= cutoff; }).ToList();
            if (recent.Count < 4) return false;
            int available = 0;
            int high = 0;
            foreach (TelemetryPoint point in recent)
            {
                double? value = cpu ? point.CpuTempC : point.GpuTempC;
                if (!value.HasValue) continue;
                available++;
                if (value.Value >= threshold) high++;
            }
            return available >= 4 && high >= Math.Max(3, (int)Math.Ceiling(available * 0.7));
        }

        private static string RuntimeEstimate(BatterySnapshot data)
        {
            if (data.RuntimeEtaSeconds.HasValue && data.RuntimeEtaSeconds.Value > 0)
                return (data.IsAcLine ? "拔電後保守估算剩餘 " : "保守估算剩餘 ") + FormatDuration(TimeSpan.FromSeconds(data.RuntimeEtaSeconds.Value)) + "。\n依最近一段時間的淨耗電與電池容量估算。";
            if (data.ChargeForecastState == "供電不足")
                return "目前無法估算充滿時間。\n外接電源下電池正在放電。";
            if (data.ChargeEtaSeconds.HasValue && data.ChargeEtaSeconds.Value > 0)
                return "保守估算約 " + FormatDuration(TimeSpan.FromSeconds(data.ChargeEtaSeconds.Value)) + " 充至 " + ForecastTargetLabel(data) + "。\n已納入電池淨流入與接近滿電時的降速。";
            if (data.ChargeForecastState == "已充滿" || data.ChargeForecastState == "已達上限")
                return data.ChargeForecastState + "。";
            return "目前資料不足，無法可靠估算續航或充滿時間。";
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalMinutes < 1) return "少於 1 分鐘";
            if (value.TotalHours < 1) return Math.Round(value.TotalMinutes).ToString("0", CultureInfo.InvariantCulture) + " 分鐘";
            int hours = Math.Max(0, (int)Math.Floor(value.TotalHours));
            int minutes = Math.Max(0, value.Minutes);
            return hours.ToString(CultureInfo.InvariantCulture) + " 小時 " + minutes.ToString(CultureInfo.InvariantCulture) + " 分鐘";
        }

        private static string ForecastTargetLabel(BatterySnapshot data)
        {
            if (data != null && data.ChargeLimitPercent.HasValue && data.ChargeLimitPercent.Value < 100)
                return "充至 " + data.ChargeLimitPercent.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
            return "充至 100%";
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0", CultureInfo.InvariantCulture) + "%" : "--";
        }

        private static string FormatChargeLimitState(double? value)
        {
            if (!value.HasValue) return "--";
            return value.Value >= 100 ? "關閉" : value.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatTemperature(double? value)
        {
            return value.HasValue ? value.Value.ToString("0", CultureInfo.InvariantCulture) + " °C" : "--";
        }

        private static string FormatTemperaturePair(double? cpu, double? gpu)
        {
            var values = new List<string>();
            if (cpu.HasValue) values.Add("CPU " + cpu.Value.ToString("0", CultureInfo.InvariantCulture) + "°C");
            if (gpu.HasValue) values.Add("GPU " + gpu.Value.ToString("0", CultureInfo.InvariantCulture) + "°C");
            return values.Count == 0 ? "--" : string.Join(" · ", values);
        }

        private static string FormatUsagePair(double? cpu, double? gpu)
        {
            var values = new List<string>();
            if (cpu.HasValue) values.Add("CPU " + cpu.Value.ToString("0", CultureInfo.InvariantCulture) + "%");
            if (gpu.HasValue) values.Add("GPU " + gpu.Value.ToString("0", CultureInfo.InvariantCulture) + "%");
            return values.Count == 0 ? "--" : string.Join(" · ", values);
        }

        private string TemperatureOverviewState(BatterySnapshot data)
        {
            bool hasCpu = data.CpuTempC.HasValue;
            bool hasGpu = data.GpuTempC.HasValue;
            if (!hasCpu && !hasGpu) return "--";
            if ((hasCpu && data.CpuTempC.Value >= settings.CpuWarnC) ||
                (hasGpu && data.GpuTempC.Value >= settings.GpuWarnC)) return "高溫注意";
            if ((hasCpu && data.CpuTempC.Value >= settings.CpuWarnC - 10) ||
                (hasGpu && data.GpuTempC.Value >= settings.GpuWarnC - 10)) return "接近提醒";
            return "正常";
        }

        private static string FormatCapacity(double? value)
        {
            return value.HasValue ? (value.Value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " Wh" : "--";
        }

        private static string FormatMemory(double usedMib, double totalMib)
        {
            if (totalMib <= 0) return "Windows 記憶體資料";
            return (usedMib / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " / " +
                (totalMib / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
        }

        private static string FormatStorage(double usedGiB, double? freeGiB, double totalGiB)
        {
            string used = usedGiB.ToString("0.0", CultureInfo.InvariantCulture) + " / " + totalGiB.ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
            return freeGiB.HasValue ? used + " · 可用 " + freeGiB.Value.ToString("0.0", CultureInfo.InvariantCulture) + " GiB" : used;
        }

        private static string FormatValue(double? value, string format, string suffix)
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) + suffix : "--";
        }

        private static string FormatSigned(double value, string suffix)
        {
            return (value >= 0 ? "+" : string.Empty) + value.ToString("0.0", CultureInfo.InvariantCulture) + suffix;
        }

        private static string FormatEnergy(double wh)
        {
            return wh >= 1000 ? (wh / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " kWh" : wh.ToString("0.0", CultureInfo.InvariantCulture) + " Wh";
        }

        private static string FormatDailyTemperatures(double? cpu, double? gpu)
        {
            string cpuText = cpu.HasValue ? cpu.Value.ToString("0", CultureInfo.InvariantCulture) + "°" : "--";
            string gpuText = gpu.HasValue ? gpu.Value.ToString("0", CultureInfo.InvariantCulture) + "°" : "--";
            return "C " + cpuText + " · G " + gpuText;
        }

        private static string TemperatureState(double? value, double warn)
        {
            if (!value.HasValue) return "--";
            if (value.Value >= warn) return "高於警示門檻";
            if (value.Value >= warn - 10) return "接近警示門檻";
            return "目前在設定範圍內";
        }

        private static string BatteryHealthState(double health)
        {
            if (health >= 90) return "健康";
            if (health >= 80) return "正常耗損";
            return "建議留意容量衰退";
        }

        private static string TextOrUnknown(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "硬體未提供" : value;
        }

        private static SolidColorBrush B(string value)
        {
            return DashboardTheme.Brush(value);
        }

        private sealed class LegendMark : FrameworkElement
        {
            private readonly Brush brush;
            private readonly DashStyle dashStyle;

            public LegendMark(string color, DashStyle style)
            {
                Width = 18;
                Height = 10;
                Margin = new Thickness(0, 0, 6, 0);
                VerticalAlignment = VerticalAlignment.Center;
                brush = BatteryWindow.Brush(color);
                dashStyle = style;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                var pen = new Pen(brush, 2.2)
                {
                    DashStyle = dashStyle,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                dc.DrawLine(pen, new Point(0, 5), new Point(18, 5));
            }
        }

        private sealed class MetricView
        {
            public readonly TextBlock Value;
            public readonly TextBlock Note;
            public MetricView(TextBlock value, TextBlock note) { Value = value; Note = note; }
        }

        private sealed class DashboardAlert
        {
            public readonly string Title;
            public readonly string Note;
            public readonly string Color;
            public DashboardAlert(string title, string note, string color) { Title = title; Note = note; Color = color; }
        }
    }

    internal static class DashboardTheme
    {
        public static SolidColorBrush Brush(string value)
        {
            return BatteryWindow.Brush(Color(value));
        }

        public static string Color(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            if (value == "#1C10211C") return "#FFD5D9DD";
            if (value == "#54737E79") return "#FF8D949B";

            if (value == "#FF00CA4E" || value == "#FF5CC8A7" || value == "#FF67D9B7" ||
                value == "#FF77D7B8" || value == "#FF9FD7C5" || value == "#FFB9F0DC" ||
                value == "#FFD0E5DC" || value == "#FF8AC7A8" || value == "#FF9DB7D8" ||
                value == "#FF6FC4F2" || value == "#FFFFC66D" || value == "#FFC6A0FF" ||
                value == "#FFFF8A80") return "#FF8D949B";

            if (value == "#FFABBBB4" || value == "#FFADBCB5" || value == "#FFB5C4BD" ||
                value == "#FFB5C5BE" || value == "#FFB7C5BF" || value == "#FFB7C6C0" ||
                value == "#FFB7C8C1" || value == "#FFB8C8C1" || value == "#FFBBD0C7" ||
                value == "#FFBECBC6" || value == "#FFBFCBC6" || value == "#FFC1CEC8" ||
                value == "#FFC1CEC9" || value == "#FFC3D0CB") return "#FF6D757D";

            if (value == "#FFD7E3DE" || value == "#FFE3ECE8" || value == "#FFE8F0EC" ||
                value == "#FFEAF2EE" || value == "#FFF1F6F3" || value == "#FFF1F7F4" ||
                value == "#FFF2F7F5" || value == "#FFF3F7F5" || value == "#FFF4F8F6" ||
                value == "#FFF7FBF9" || value == "#FFF8FBFA" || value == "#FFFFFFFF") return "#FF252A2F";

            return value;
        }
    }

    public sealed class AdvancedNavVisual
    {
        public Border Root { get; private set; }
        private readonly Border indicator;
        private readonly TextBlock title;
        private readonly TextBlock note;

        public AdvancedNavVisual(string icon, string titleText, string noteText)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), ClipToBounds = true };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            indicator = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Brushes.Transparent, Margin = new Thickness(0, 8, 0, 8) };
            grid.Children.Add(indicator);
            var iconText = new TextBlock
            {
                Text = icon,
                Foreground = DashboardTheme.Brush("#FFD7E3DE"),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconText, 1);
            grid.Children.Add(iconText);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 6, 0) };
            title = new TextBlock { Text = titleText, Foreground = DashboardTheme.Brush("#FFE3ECE8"), FontSize = 12, FontWeight = FontWeights.Medium };
            note = new TextBlock { Text = noteText, Foreground = DashboardTheme.Brush("#FFB5C4BD"), FontSize = 8.5, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
            labels.Children.Add(title);
            labels.Children.Add(note);
            Grid.SetColumn(labels, 2);
            grid.Children.Add(labels);
            Root = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Focusable = true,
                Cursor = Cursors.Hand,
                Child = grid
            };
            Root.MouseEnter += delegate { if (indicator.Background == Brushes.Transparent) Root.Background = DashboardTheme.Brush("#13FFFFFF"); };
            Root.MouseLeave += delegate { if (indicator.Background == Brushes.Transparent) Root.Background = Brushes.Transparent; };
            Root.GotKeyboardFocus += delegate { Root.BorderBrush = DashboardTheme.Brush("#AFFFFFFF"); };
            Root.LostKeyboardFocus += delegate { Root.BorderBrush = Brushes.Transparent; };
        }

        public void SetSelected(bool selected)
        {
            indicator.Background = selected ? DashboardTheme.Brush("#FF77D7B8") : Brushes.Transparent;
            Root.Background = selected ? DashboardTheme.Brush("#24FFFFFF") : Brushes.Transparent;
            title.Foreground = selected ? DashboardTheme.Brush("#FFFFFFFF") : DashboardTheme.Brush("#FFE3ECE8");
            note.Foreground = selected ? DashboardTheme.Brush("#FFD0E5DC") : DashboardTheme.Brush("#FFB5C4BD");
        }
    }

    public sealed class ElasticNavPanel : Panel
    {
        private double[] weights = new double[0];
        private double[] targets = new double[0];
        private Rect[] arranged = new Rect[0];
        private int focusIndex = -1;

        public ElasticNavPanel()
        {
            Background = Brushes.Transparent;
            Loaded += delegate { CompositionTarget.Rendering += OnFrame; };
            Unloaded += delegate { CompositionTarget.Rendering -= OnFrame; };
            MouseMove += OnPanelMouseMove;
            MouseLeave += delegate { SetFocusIndex(-1); };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureArrays();
            double width = double.IsInfinity(availableSize.Width) ? 220 : availableSize.Width;
            double height = double.IsInfinity(availableSize.Height) ? InternalChildren.Count * 58 : availableSize.Height;
            foreach (UIElement child in InternalChildren) child.Measure(new Size(width, height));
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            EnsureArrays();
            double total = weights.Sum();
            if (total <= 0) total = Math.Max(1, InternalChildren.Count);
            double y = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                double height = i == InternalChildren.Count - 1 ? finalSize.Height - y : finalSize.Height * weights[i] / total;
                arranged[i] = new Rect(0, y, finalSize.Width, Math.Max(0, height));
                InternalChildren[i].Arrange(arranged[i]);
                y += height;
            }
            return finalSize;
        }

        private void OnPanelMouseMove(object sender, MouseEventArgs e)
        {
            Point point = e.GetPosition(this);
            int index = -1;
            for (int i = 0; i < arranged.Length; i++)
            {
                if (arranged[i].Contains(point)) { index = i; break; }
            }
            SetFocusIndex(index);
        }

        private void SetFocusIndex(int index)
        {
            if (focusIndex == index && targets.Length == InternalChildren.Count) return;
            focusIndex = index;
            EnsureArrays();
            for (int i = 0; i < targets.Length; i++)
            {
                if (index < 0) targets[i] = 1;
                else if (i == index) targets[i] = 1.62;
                else if (Math.Abs(i - index) == 1) targets[i] = 0.94;
                else targets[i] = 0.79;
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            EnsureArrays();
            bool changed = false;
            for (int i = 0; i < weights.Length; i++)
            {
                double next = SystemParameters.ClientAreaAnimation
                    ? weights[i] + (targets[i] - weights[i]) * 0.19
                    : targets[i];
                if (Math.Abs(next - weights[i]) > 0.001) changed = true;
                weights[i] = next;
            }
            if (changed) InvalidateArrange();
        }

        private void EnsureArrays()
        {
            int count = InternalChildren.Count;
            if (weights.Length == count) return;
            weights = Enumerable.Repeat(1.0, count).ToArray();
            targets = Enumerable.Repeat(1.0, count).ToArray();
            arranged = new Rect[count];
            focusIndex = -1;
        }
    }

    public sealed class ToggleSwitch
    {
        public Border Root { get; private set; }
        public event Action<bool> Changed;
        private readonly Border thumb;
        private bool isOn;

        public ToggleSwitch(bool initial)
        {
            thumb = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Root = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(1),
                Focusable = true,
                Cursor = Cursors.Hand,
                Child = thumb
            };
            Root.MouseLeftButtonUp += delegate { SetState(!isOn, true); };
            Root.GotKeyboardFocus += delegate { Root.BorderBrush = DashboardTheme.Brush("#BFFFFFFF"); };
            Root.LostKeyboardFocus += delegate { Root.BorderBrush = Brushes.Transparent; };
            Root.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                SetState(!isOn, true);
            };
            SetState(initial, false);
        }

        public void SetState(bool value, bool notify)
        {
            isOn = value;
            Root.Background = new SolidColorBrush(value ? Color.FromRgb(105, 114, 122) : Color.FromRgb(190, 197, 202));
            Root.BorderBrush = new SolidColorBrush(value ? Color.FromRgb(105, 114, 122) : Color.FromRgb(170, 178, 184));
            if (notify && Changed != null) Changed(value);
        }
    }

    public sealed class NumericStepper
    {
        public Grid Root { get; private set; }
        public event Action<double> ValueChanged;
        public double Value { get { return value; } }
        private readonly TextBlock valueText;
        private readonly double minimum;
        private readonly double maximum;
        private readonly double step;
        private readonly string suffix;
        private double value;

        public NumericStepper(double initial, double min, double max, double stepValue, string valueSuffix)
        {
            minimum = min;
            maximum = max;
            step = stepValue;
            suffix = valueSuffix;
            Root = new Grid { Width = 132, Height = 32 };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            Root.Children.Add(StepButton("−", delegate { SetValue(value - step, true); }, 0));
            valueText = new TextBlock
            {
                Foreground = DashboardTheme.Brush("#FFF4F8F6"),
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 1);
            Root.Children.Add(valueText);
            Root.Children.Add(StepButton("+", delegate { SetValue(value + step, true); }, 2));
            SetValue(initial, false);
        }

        public void SetValue(double newValue, bool notify)
        {
            value = Math.Max(minimum, Math.Min(maximum, newValue));
            valueText.Text = value.ToString("0", CultureInfo.InvariantCulture) + suffix;
            if (notify && ValueChanged != null) ValueChanged(value);
        }

        private static Border StepButton(string glyph, Action action, int column)
        {
            var button = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                BorderBrush = DashboardTheme.Brush("#31FFFFFF"),
                Background = DashboardTheme.Brush("#18FFFFFF"),
                Focusable = true,
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = glyph, Foreground = DashboardTheme.Brush("#FFF2F7F5"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            button.MouseEnter += delegate { button.Background = DashboardTheme.Brush("#30FFFFFF"); };
            button.MouseLeave += delegate { button.Background = DashboardTheme.Brush("#18FFFFFF"); };
            button.GotKeyboardFocus += delegate { button.BorderBrush = DashboardTheme.Brush("#BFFFFFFF"); };
            button.LostKeyboardFocus += delegate { button.BorderBrush = DashboardTheme.Brush("#31FFFFFF"); };
            button.MouseLeftButtonUp += delegate { if (action != null) action(); };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                if (action != null) action();
            };
            Grid.SetColumn(button, column);
            return button;
        }
    }

    public enum TelemetryChartMode { All, Power, Temperature }

    public sealed class TelemetryChart : FrameworkElement
    {
        public TelemetryChartMode Mode { get; private set; }
        private IList<TelemetryPoint> points = new List<TelemetryPoint>();

        public TelemetryChart(TelemetryChartMode mode)
        {
            Mode = mode;
            SnapsToDevicePixels = true;
            MinHeight = 180;
        }

        public void SetPoints(IList<TelemetryPoint> values)
        {
            points = values == null ? new List<TelemetryPoint>() : values.ToList();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Rect plot = new Rect(42, 8, Math.Max(1, ActualWidth - 56), Math.Max(1, ActualHeight - 34));
            Pen gridPen = new Pen(B("#1FFFFFFF"), 1);
            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Top + plot.Height * i / 4.0;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }
            for (int i = 0; i <= 6; i++)
            {
                double x = plot.Left + plot.Width * i / 6.0;
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }

            DateTime end = DateTime.Now;
            DateTime start = end.AddMinutes(-30);
            IList<TelemetryPoint> visible = points.Where(delegate(TelemetryPoint p) { return p.At >= start && p.At <= end.AddSeconds(2); }).ToList();
            if (visible.Count == 0)
            {
                DrawText(dc, "收集資料中", new Point(plot.Left + 10, plot.Top + 12), 10, B("#FFC1CEC8"));
                DrawTimeLabels(dc, plot);
                return;
            }

            double maxPower = 100;
            foreach (TelemetryPoint p in visible)
            {
                if (p.SystemWatts.HasValue) maxPower = Math.Max(maxPower, p.SystemWatts.Value);
                if (p.BatteryWatts.HasValue) maxPower = Math.Max(maxPower, p.BatteryWatts.Value);
            }
            maxPower = Math.Ceiling(maxPower / 20.0) * 20.0;

            dc.PushClip(new RectangleGeometry(plot));
            if (Mode == TelemetryChartMode.All || Mode == TelemetryChartMode.Power)
            {
                DrawSeries(dc, visible, start, end, plot, delegate(TelemetryPoint p) { return p.SystemWatts; }, 0, maxPower, "#FF475569", 2.4, DashStyles.Solid);
                DrawSeries(dc, visible, start, end, plot, delegate(TelemetryPoint p) { return p.BatteryWatts; }, 0, maxPower, "#FFD97706", 2.4, DashStyles.Solid);
            }
            if (Mode == TelemetryChartMode.All || Mode == TelemetryChartMode.Temperature)
            {
                DrawSeries(dc, visible, start, end, plot, delegate(TelemetryPoint p) { return p.CpuTempC; }, 20, 100, "#FF0878B9", 2.2, DashStyles.Dash);
                DrawSeries(dc, visible, start, end, plot, delegate(TelemetryPoint p) { return p.GpuTempC; }, 20, 100, "#FF7C3AED", 2.2, DashStyles.Dot);
            }
            dc.Pop();

            DrawText(dc, Mode == TelemetryChartMode.Temperature ? "100°C" : maxPower.ToString("0", CultureInfo.InvariantCulture), new Point(2, plot.Top - 2), 9, B("#FFC1CEC8"));
            DrawText(dc, Mode == TelemetryChartMode.Temperature ? "20°C" : "0", new Point(14, plot.Bottom - 10), 9, B("#FFC1CEC8"));
            DrawTimeLabels(dc, plot);
        }

        private static void DrawSeries(DrawingContext dc, IList<TelemetryPoint> values, DateTime start, DateTime end, Rect plot, Func<TelemetryPoint, double?> selector, double minimum, double maximum, string color, double thickness, DashStyle dashStyle)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                bool open = false;
                DateTime previous = DateTime.MinValue;
                foreach (TelemetryPoint point in values)
                {
                    double? value = selector(point);
                    if (!value.HasValue) { open = false; continue; }
                    double xRatio = (point.At - start).TotalSeconds / (end - start).TotalSeconds;
                    double yRatio = (value.Value - minimum) / Math.Max(1, maximum - minimum);
                    xRatio = Math.Max(0, Math.Min(1, xRatio));
                    yRatio = Math.Max(0, Math.Min(1, yRatio));
                    Point mapped = new Point(plot.Left + plot.Width * xRatio, plot.Bottom - plot.Height * yRatio);
                    if (!open || previous == DateTime.MinValue || (point.At - previous).TotalSeconds > 15)
                    {
                        context.BeginFigure(mapped, false, false);
                        open = true;
                    }
                    else context.LineTo(mapped, true, false);
                    previous = point.At;
                }
            }
            geometry.Freeze();
            Pen pen = new Pen(BatteryWindow.Brush(color), thickness)
            {
                DashStyle = dashStyle,
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private static void DrawTimeLabels(DrawingContext dc, Rect plot)
        {
            DrawText(dc, "-30m", new Point(plot.Left, plot.Bottom + 7), 8.5, B("#FFB7C5BF"));
            DrawText(dc, "-20m", new Point(plot.Left + plot.Width / 3 - 10, plot.Bottom + 7), 8.5, B("#FFB7C5BF"));
            DrawText(dc, "-10m", new Point(plot.Left + plot.Width * 2 / 3 - 10, plot.Bottom + 7), 8.5, B("#FFB7C5BF"));
            DrawText(dc, "現在", new Point(plot.Right - 20, plot.Bottom + 7), 8.5, B("#FFB7C5BF"));
        }

        private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush)
        {
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-TW"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
            dc.DrawText(formatted, point);
        }

        private static SolidColorBrush B(string value)
        {
            return DashboardTheme.Brush(value);
        }
    }
}
