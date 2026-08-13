using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace BatteryPulse
{
    /// <summary>
    /// Reads lightweight performance counters without adding another WMI polling loop.
    /// </summary>
    internal sealed class PerformanceReader
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;

            public long ToInt64()
            {
                return ((long)High << 32) | Low;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class MemoryStatusEx
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx status);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

        private long? previousIdle;
        private long? previousTotal;
        private DateTime lastProcessEnergyRead = DateTime.MinValue;
        private readonly Dictionary<int, ProcessCpuSample> previousProcesses = new Dictionary<int, ProcessCpuSample>();
        private readonly Queue<Dictionary<string, double>> processActivitySamples = new Queue<Dictionary<string, double>>();
        private List<EnergyProcessSnapshot> lastEnergyRanking = new List<EnergyProcessSnapshot>();
        private string lastEnergyRankingSource = "尚未取得足夠樣本";

        private sealed class ProcessCpuSample
        {
            public string Name;
            public long CpuTicks;
        }

        public void Read(BatterySnapshot data)
        {
            if (data == null) return;
            ReadMemory(data);
            ReadStorage(data);
            ReadCpuUsage(data);
            ReadProcessEnergy(data);
            ReadChargeEta(data);
        }

        private static void ReadMemory(BatterySnapshot data)
        {
            try
            {
                var status = new MemoryStatusEx();
                if (!GlobalMemoryStatusEx(status)) return;

                data.MemoryUsedPercent = Math.Max(0, Math.Min(100, (double)status.MemoryLoad));
                data.MemoryTotalMib = status.TotalPhys / 1024.0 / 1024.0;
                data.MemoryUsedMib = (status.TotalPhys - status.AvailPhys) / 1024.0 / 1024.0;
                data.MemorySource = "Windows GlobalMemoryStatusEx";
            }
            catch { }
        }

        private static void ReadStorage(BatterySnapshot data)
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                data.StorageVolumes.Clear();
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || drive.TotalSize <= 0) continue;
                    if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.Ram) continue;

                    double totalGiB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                    double freeGiB = Math.Max(0, drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0);
                    double usedGiB = Math.Max(0, totalGiB - freeGiB);
                    data.StorageVolumes.Add(new StorageVolumeSnapshot
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        TotalGiB = totalGiB,
                        UsedGiB = usedGiB,
                        FreeGiB = freeGiB,
                        UsedPercent = Math.Max(0, Math.Min(100, usedGiB / totalGiB * 100.0))
                    });

                    if (!string.IsNullOrWhiteSpace(root) && string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase))
                    {
                        data.StorageTotalGiB = totalGiB;
                        data.StorageFreeGiB = freeGiB;
                        data.StorageUsedGiB = usedGiB;
                        data.StorageUsedPercent = Math.Max(0, Math.Min(100, usedGiB / totalGiB * 100.0));
                    }
                }

                if (data.StorageVolumes.Count > 0)
                {
                    data.StorageVolumes.Sort(delegate(StorageVolumeSnapshot left, StorageVolumeSnapshot right)
                    {
                        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                    });
                    data.StorageSource = "Windows DriveInfo " + string.Join(", ", data.StorageVolumes.Select(volume => volume.Name).ToArray());
                }
            }
            catch { }
        }

        private void ReadCpuUsage(BatterySnapshot data)
        {
            try
            {
                FileTime idle;
                FileTime kernel;
                FileTime user;
                if (!GetSystemTimes(out idle, out kernel, out user)) return;

                long idleValue = idle.ToInt64();
                long totalValue = kernel.ToInt64() + user.ToInt64();
                if (previousIdle.HasValue && previousTotal.HasValue)
                {
                    long idleDelta = idleValue - previousIdle.Value;
                    long totalDelta = totalValue - previousTotal.Value;
                    if (totalDelta > 0)
                    {
                        double usage = (1.0 - Math.Max(0, idleDelta) / (double)totalDelta) * 100.0;
                        data.CpuUsagePercent = Math.Max(0, Math.Min(100, usage));
                        data.CpuUsageSource = "Windows GetSystemTimes";
                    }
                }

                previousIdle = idleValue;
                previousTotal = totalValue;
            }
            catch { }
        }

        private void ReadProcessEnergy(BatterySnapshot data)
        {
            if (lastEnergyRanking.Count > 0)
            {
                data.EnergyRanking = lastEnergyRanking.Select(delegate(EnergyProcessSnapshot item)
                {
                    return new EnergyProcessSnapshot
                    {
                        Name = item.Name,
                        CpuUsagePercent = item.CpuUsagePercent
                    };
                }).ToList();
                data.EnergyRankingSource = lastEnergyRankingSource;
            }

            DateTime now = DateTime.UtcNow;
            if ((now - lastProcessEnergyRead).TotalSeconds < 1) return;
            double sampleSeconds = lastProcessEnergyRead == DateTime.MinValue
                ? 0
                : (now - lastProcessEnergyRead).TotalSeconds;
            lastProcessEnergyRead = now;

            if (sampleSeconds > 2.5)
                processActivitySamples.Clear();

            var currentProcesses = new Dictionary<int, ProcessCpuSample>();
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        string name = process.ProcessName;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        currentProcesses[process.Id] = new ProcessCpuSample
                        {
                            Name = name,
                            CpuTicks = process.TotalProcessorTime.Ticks
                        };
                    }
                    catch
                    {
                        // Access to short-lived or protected processes can fail.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                return;
            }

            if (previousProcesses.Count > 0 && sampleSeconds > 0)
            {
                var groupedUsage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<int, ProcessCpuSample> current in currentProcesses)
                {
                    ProcessCpuSample previous;
                    if (!previousProcesses.TryGetValue(current.Key, out previous)) continue;
                    long delta = current.Value.CpuTicks - previous.CpuTicks;
                    if (delta <= 0) continue;

                    // A process can use multiple cores, but its share of total CPU capacity is capped at 100%.
                    double usage = delta /
                        (TimeSpan.TicksPerSecond * sampleSeconds * Math.Max(1, Environment.ProcessorCount)) * 100.0;
                    usage = Math.Max(0, Math.Min(100, usage));
                    if (usage <= 0) continue;

                    double existing;
                    if (!groupedUsage.TryGetValue(current.Value.Name, out existing)) existing = 0;
                    groupedUsage[current.Value.Name] = existing + usage;
                }

                processActivitySamples.Enqueue(groupedUsage);
                while (processActivitySamples.Count > 5)
                    processActivitySamples.Dequeue();

                // Keep the first four samples out of the ranking so the displayed proportion always represents a full 5-second window.
                if (processActivitySamples.Count >= 5)
                {
                    var accumulated = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (Dictionary<string, double> sample in processActivitySamples)
                    {
                        foreach (KeyValuePair<string, double> item in sample)
                        {
                            double existing;
                            if (!accumulated.TryGetValue(item.Key, out existing)) existing = 0;
                            accumulated[item.Key] = existing + item.Value;
                        }
                    }

                    double totalActivity = accumulated.Values.Sum();
                    if (totalActivity > 0)
                    {
                        List<KeyValuePair<string, double>> topItems = accumulated
                            .OrderByDescending(delegate(KeyValuePair<string, double> item) { return item.Value; })
                            .Take(5)
                            .ToList();
                        List<EnergyProcessSnapshot> ranking = topItems
                            .Select(delegate(KeyValuePair<string, double> item)
                            {
                                return new EnergyProcessSnapshot
                                {
                                    Name = item.Key,
                                    CpuUsagePercent = item.Value / totalActivity * 100.0
                                };
                            })
                            .ToList();

                        double topShare = ranking.Sum(delegate(EnergyProcessSnapshot item) { return item.CpuUsagePercent; });
                        double otherShare = Math.Max(0, 100.0 - topShare);
                        if (otherShare >= 0.05)
                        {
                            ranking.Add(new EnergyProcessSnapshot
                            {
                                Name = "其他",
                                CpuUsagePercent = otherShare
                            });
                        }

                        lastEnergyRanking = ranking;
                        lastEnergyRankingSource = "Windows Process CPU time / 最近 5 秒每秒占用率累加；前五名按比例分配整機功耗，每 1 秒更新；GPU 不做程序級假分配";
                        data.EnergyRanking = ranking.Select(delegate(EnergyProcessSnapshot item)
                        {
                            return new EnergyProcessSnapshot
                            {
                                Name = item.Name,
                                CpuUsagePercent = item.CpuUsagePercent
                            };
                        }).ToList();
                        data.EnergyRankingSource = lastEnergyRankingSource;
                    }
                    else
                    {
                        lastEnergyRanking.Clear();
                        lastEnergyRankingSource = "目前沒有明顯的程序 CPU 活動";
                        data.EnergyRanking.Clear();
                        data.EnergyRankingSource = lastEnergyRankingSource;
                    }
                }
            }

            previousProcesses.Clear();
            foreach (KeyValuePair<int, ProcessCpuSample> item in currentProcesses)
                previousProcesses[item.Key] = item.Value;
        }

        private static void ReadChargeEta(BatterySnapshot data)
        {
            if (!data.IsCharging || !data.Percent.HasValue || !data.FullChargeCapacityMwh.HasValue ||
                !data.Watts.HasValue || data.Watts.Value <= 0 || data.Percent.Value >= 100 ||
                data.FullChargeCapacityMwh.Value <= 0) return;

            try
            {
                double remainingMwh = data.FullChargeCapacityMwh.Value *
                    Math.Max(0, 100.0 - data.Percent.Value) / 100.0;
                double seconds = remainingMwh / (data.Watts.Value * 1000.0) * 3600.0;
                if (seconds <= 0 || seconds > 48 * 3600) return;
                data.ChargeEtaSeconds = seconds;
                data.ChargeEtaSource = "Battery capacity / ChargeRate estimate";
            }
            catch { }
        }
    }
}
