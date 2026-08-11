using System;
using System.IO;
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

        public void Read(BatterySnapshot data)
        {
            if (data == null) return;
            ReadMemory(data);
            ReadStorage(data);
            ReadCpuUsage(data);
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
                if (string.IsNullOrWhiteSpace(root)) return;
                var drive = new DriveInfo(root);
                if (!drive.IsReady || drive.TotalSize <= 0) return;

                double totalGiB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                double freeGiB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                data.StorageTotalGiB = totalGiB;
                data.StorageFreeGiB = Math.Max(0, freeGiB);
                data.StorageUsedGiB = Math.Max(0, totalGiB - freeGiB);
                data.StorageUsedPercent = Math.Max(0, Math.Min(100, data.StorageUsedGiB.Value / totalGiB * 100.0));
                data.StorageSource = "Windows DriveInfo " + root.TrimEnd('\\');
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
