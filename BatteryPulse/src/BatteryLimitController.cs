using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace BatteryPulse
{
    public enum BatteryLimitControlMode
    {
        Unsupported,
        Toggle,
        Threshold
    }

    public sealed class BatteryLimitCapabilities
    {
        public BatteryLimitControlMode Mode = BatteryLimitControlMode.Unsupported;
        public bool CanWrite;
        public string ProviderName = "未偵測到";
        public string Source = "未偵測到可控制介面";
        public string Note = "此裝置沒有可由 BatteryPulse 控制的充電上限介面。";
        public int[] Thresholds = new int[0];
        public int? CurrentPercent;
        public int? LastAppliedPercent;

        public bool Supported
        {
            get { return Mode != BatteryLimitControlMode.Unsupported; }
        }
    }

    public sealed class BatteryLimitApplyResult
    {
        public bool Success;
        public string Message;
        public int? AppliedPercent;
    }

    /// <summary>
    /// Vendor-specific battery protection adapter.
    /// The ASUS endpoint and ATKACPI packet layout are public protocol facts;
    /// this adapter is an original implementation and does not bundle G-Helper code.
    /// </summary>
    public static class BatteryLimitController
    {
        private const string AsusDevicePath = "\\\\.\\ATKACPI";
        private const uint AsusControlCode = 0x0022240C;
        private const uint AsusDsts = 0x53545344;
        private const uint AsusDevs = 0x53564544;
        private const uint AsusBatteryLimit = 0x00120057;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private static readonly object Sync = new object();
        private static BatteryLimitCapabilities cachedCapabilities;
        private static DateTime cachedAt = DateTime.MinValue;
        private static int? lastAppliedPercent;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr device,
            uint controlCode,
            byte[] input,
            uint inputSize,
            byte[] output,
            uint outputSize,
            ref uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static BatteryLimitCapabilities GetCapabilities()
        {
            lock (Sync)
            {
                if (cachedCapabilities != null && (DateTime.UtcNow - cachedAt).TotalSeconds < 10)
                    return Copy(cachedCapabilities);

                BatteryLimitCapabilities result = ProbeAsusAcpi();
                if (!result.Supported)
                    result = ProbeAsusWmi();

                result.LastAppliedPercent = lastAppliedPercent;
                cachedCapabilities = result;
                cachedAt = DateTime.UtcNow;
                return Copy(result);
            }
        }

        public static void Enrich(BatterySnapshot data)
        {
            if (data == null) return;
            BatteryLimitCapabilities capabilities = GetCapabilities();
            data.ChargeLimitSupported = capabilities.Supported;
            data.ChargeLimitCanWrite = capabilities.CanWrite;
            data.ChargeLimitMode = capabilities.Mode.ToString();
            data.ChargeLimitProvider = capabilities.ProviderName;
            data.ChargeLimitSource = capabilities.Source;
            data.ChargeLimitOptions = capabilities.Thresholds ?? new int[0];
            data.ChargeLimitPercent = capabilities.CurrentPercent ?? capabilities.LastAppliedPercent;
            data.ChargeLimitIsLastApplied = !capabilities.CurrentPercent.HasValue && capabilities.LastAppliedPercent.HasValue;
            data.ChargeLimitStateNote = capabilities.CurrentPercent.HasValue
                ? "韌體回報目前方案"
                : capabilities.LastAppliedPercent.HasValue
                    ? "本程式上次套用方案"
                    : capabilities.Note;
        }

        public static void RestoreLastApplied(int? percent)
        {
            lock (Sync)
            {
                lastAppliedPercent = percent.HasValue && percent.Value >= 40 && percent.Value <= 100 ? percent : null;
                if (cachedCapabilities != null)
                    cachedCapabilities.LastAppliedPercent = lastAppliedPercent;
            }
        }

        public static BatteryLimitApplyResult Apply(int percent)
        {
            if (percent < 40 || percent > 100)
                return Failure("請輸入 40% 到 100% 之間的上限。");

            lock (Sync)
            {
                BatteryLimitCapabilities capabilities = GetCapabilities();
                if (!capabilities.Supported || !capabilities.CanWrite)
                    return Failure("此裝置沒有可寫入的充電上限介面。");

                bool success = false;
                string provider = capabilities.ProviderName;
                if (provider == "ASUS ACPI")
                    success = TrySetAsusAcpi(percent);
                else if (provider == "ASUS WMI")
                    success = TrySetAsusWmi(percent);

                if (!success)
                    return Failure("韌體拒絕這個方案，或 ASUS 控制介面目前不可用；未變更設定。");

                lastAppliedPercent = percent;
                cachedCapabilities.LastAppliedPercent = percent;
                cachedCapabilities.CurrentPercent = null;
                return new BatteryLimitApplyResult
                {
                    Success = true,
                    AppliedPercent = percent,
                    Message = percent >= 100 ? "已套用 100%，充電限制已關閉。" : "已套用 " + percent + "% 充電上限。"
                };
            }
        }

        private static BatteryLimitCapabilities ProbeAsusAcpi()
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = CreateFile(
                    AsusDevicePath,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileAttributeNormal,
                    IntPtr.Zero);

                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return Unsupported();

                int raw;
                if (!TryCallAsus(handle, AsusDsts, BuildDeviceArguments(AsusBatteryLimit), out raw))
                    return Unsupported();

                int value = raw - 65536;
                return new BatteryLimitCapabilities
                {
                    Mode = BatteryLimitControlMode.Threshold,
                    CanWrite = true,
                    ProviderName = "ASUS ACPI",
                    Source = "ASUS ATKACPI BatteryLimit",
                    Note = "可用方案：60／80／100%；也可嘗試自訂 40%～100%。",
                    Thresholds = new[] { 60, 80, 100 },
                    CurrentPercent = value >= 40 && value <= 100 ? (int?)value : null
                };
            }
            catch
            {
                return Unsupported();
            }
            finally
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1)) CloseHandle(handle);
            }
        }

        private static BatteryLimitCapabilities ProbeAsusWmi()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM AsusAtkWmi_WMNB"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        if (item == null) continue;
                        ManagementBaseObject input = item.GetMethodParameters("DSTS");
                        input["Device_ID"] = AsusBatteryLimit;
                        ManagementBaseObject output = item.InvokeMethod("DSTS", input, null);
                        if (output == null) continue;
                        object statusObject = output["device_status"] ?? output["Device_Status"] ?? output["ReturnValue"];
                        if (statusObject == null) continue;
                        int raw = Convert.ToInt32(statusObject);
                        if (raw == 0) continue;
                        int value = raw - 65536;
                        return new BatteryLimitCapabilities
                        {
                            Mode = BatteryLimitControlMode.Threshold,
                            CanWrite = true,
                            ProviderName = "ASUS WMI",
                            Source = "ASUS AsusAtkWmi_WMNB",
                            Note = "可用方案：60／80／100%；實際可接受值由韌體確認。",
                            Thresholds = new[] { 60, 80, 100 },
                            CurrentPercent = value >= 40 && value <= 100 ? (int?)value : null
                        };
                    }
                }
            }
            catch { }
            return Unsupported();
        }

        private static bool TrySetAsusAcpi(int percent)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = CreateFile(
                    AsusDevicePath,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileAttributeNormal,
                    IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;

                int result;
                if (!TryCallAsus(handle, AsusDevs, BuildDeviceArguments(AsusBatteryLimit, (uint)percent), out result))
                    return false;
                return result == 1;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1)) CloseHandle(handle);
            }
        }

        private static bool TrySetAsusWmi(int percent)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM AsusAtkWmi_WMNB"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        ManagementBaseObject input = item.GetMethodParameters("DEVS");
                        input["Device_ID"] = AsusBatteryLimit;
                        input["Control_status"] = (uint)percent;
                        ManagementBaseObject output = item.InvokeMethod("DEVS", input, null);
                        if (output == null) continue;
                        object result = output["result"] ?? output["Result"] ?? output["ReturnValue"];
                        return result != null && Convert.ToInt32(result) == 1;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryCallAsus(IntPtr handle, uint method, byte[] arguments, out int result)
        {
            result = 0;
            byte[] input = new byte[8 + arguments.Length];
            byte[] output = new byte[16];
            BitConverter.GetBytes(method).CopyTo(input, 0);
            BitConverter.GetBytes((uint)arguments.Length).CopyTo(input, 4);
            Array.Copy(arguments, 0, input, 8, arguments.Length);
            uint bytesReturned = 0;
            if (!DeviceIoControl(handle, AsusControlCode, input, (uint)input.Length, output, (uint)output.Length, ref bytesReturned, IntPtr.Zero))
                return false;
            if (bytesReturned < 4) return false;
            result = BitConverter.ToInt32(output, 0);
            return true;
        }

        private static byte[] BuildDeviceArguments(uint deviceId)
        {
            return BuildDeviceArguments(deviceId, 0);
        }

        private static byte[] BuildDeviceArguments(uint deviceId, uint status)
        {
            byte[] arguments = new byte[8];
            BitConverter.GetBytes(deviceId).CopyTo(arguments, 0);
            BitConverter.GetBytes(status).CopyTo(arguments, 4);
            return arguments;
        }

        private static BatteryLimitCapabilities Unsupported()
        {
            return new BatteryLimitCapabilities();
        }

        private static BatteryLimitApplyResult Failure(string message)
        {
            return new BatteryLimitApplyResult { Success = false, Message = message };
        }

        private static BatteryLimitCapabilities Copy(BatteryLimitCapabilities source)
        {
            var copy = new BatteryLimitCapabilities
            {
                Mode = source.Mode,
                CanWrite = source.CanWrite,
                ProviderName = source.ProviderName,
                Source = source.Source,
                Note = source.Note,
                CurrentPercent = source.CurrentPercent,
                LastAppliedPercent = source.LastAppliedPercent,
                Thresholds = source.Thresholds == null ? new int[0] : (int[])source.Thresholds.Clone()
            };
            return copy;
        }
    }
}
