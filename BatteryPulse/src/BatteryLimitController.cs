using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;

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
        private const string AsusChargeLimitRegistryPath = "SOFTWARE\\ASUS\\ASUS System Control Interface\\AsusOptimization\\ASUS Keyboard Hotkeys";
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
        private static int? confirmedWritePercent;

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
                if (!result.Supported)
                    result = ProbeAsusRegistry();

                // Some ASUS generations expose the current limit only through
                // the System Control Interface registry. If the ACPI/WMI write
                // was accepted in this process, prefer that hardware-confirmed
                // state over a stale registry value.
                if (confirmedWritePercent.HasValue && result.Supported && result.CanWrite)
                {
                    result.CurrentPercent = confirmedWritePercent;
                    result.Source = result.ProviderName + " DEVS 寫入確認";
                }
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
            // Only expose a value that the firmware/driver reported now.
            // A locally remembered setting is not proof that the limit is active.
            data.ChargeLimitPercent = capabilities.CurrentPercent;
            data.ChargeLimitIsLastApplied = false;
            data.ChargeLimitStateNote = capabilities.CurrentPercent.HasValue
                ? (capabilities.Source.IndexOf("寫入確認", StringComparison.Ordinal) >= 0 ? "控制介面已確認" : "目前讀值")
                : "--";
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

                // ASUS ACPI/WMI returns a success code for DEVS, but some models
                // do not expose the newly written 100% state through DSTS.
                // Sync the optional registry value when permissions allow it,
                // then use read-back when available without making it mandatory.
                bool registrySynced = TrySetAsusRegistryLimit(percent);
                bool readBackMatches = false;
                confirmedWritePercent = null;
                BatteryLimitCapabilities verified = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(150);
                    cachedCapabilities = null;
                    cachedAt = DateTime.MinValue;
                    verified = GetCapabilities();
                    if (verified.CurrentPercent.HasValue && verified.CurrentPercent.Value == percent)
                    {
                        readBackMatches = true;
                        break;
                    }
                }

                confirmedWritePercent = percent;
                if (verified == null) verified = capabilities;
                verified.CurrentPercent = percent;
                verified.Source = readBackMatches || registrySynced
                    ? verified.Source
                    : provider + " DEVS 寫入確認";
                verified.LastAppliedPercent = null;
                cachedCapabilities = verified;
                cachedAt = DateTime.UtcNow;

                // Keep the setting for backward-compatible preferences, but never
                // use it as a displayed current value.
                lastAppliedPercent = null;
                cachedCapabilities.LastAppliedPercent = null;
                return new BatteryLimitApplyResult
                {
                    Success = true,
                    AppliedPercent = percent,
                    Message = percent >= 100 ? "ASUS 控制介面已接受 100%，充電限制已關閉。" : "ASUS 控制介面已接受 " + percent + "% 充電上限。"
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

                int? current = DecodeLimitValue(raw);
                bool fromRegistry = !current.HasValue;
                if (fromRegistry)
                    current = ReadAsusRegistryLimit();
                return new BatteryLimitCapabilities
                {
                    Mode = BatteryLimitControlMode.Threshold,
                    CanWrite = true,
                    ProviderName = "ASUS ACPI",
                    Source = fromRegistry ? "ASUS System Control Interface 登錄設定" : "ASUS ATKACPI BatteryLimit",
                    Note = fromRegistry
                        ? "ACPI 已偵測到可控制介面；目前值採 ASUS System Control Interface 登錄設定。"
                        : "可用方案由 ASUS 韌體決定；目前讀值來自 ATKACPI。",
                    Thresholds = new[] { 60, 80, 100 },
                    CurrentPercent = current
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
                        int? current = DecodeLimitValue(raw);
                        bool fromRegistry = !current.HasValue;
                        if (fromRegistry)
                            current = ReadAsusRegistryLimit();
                        return new BatteryLimitCapabilities
                        {
                            Mode = BatteryLimitControlMode.Threshold,
                            CanWrite = true,
                            ProviderName = "ASUS WMI",
                            Source = fromRegistry ? "ASUS System Control Interface 登錄設定" : "ASUS AsusAtkWmi_WMNB",
                            Note = fromRegistry
                                ? "WMI 已偵測到可控制介面；目前值採 ASUS System Control Interface 登錄設定。"
                                : "可用方案由 ASUS 韌體決定；目前讀值來自 WMI。",
                            Thresholds = new[] { 60, 80, 100 },
                            CurrentPercent = current
                        };
                    }
                }
            }
            catch { }
            return Unsupported();
        }

        private static BatteryLimitCapabilities ProbeAsusRegistry()
        {
            int? current = ReadAsusRegistryLimit();
            if (!current.HasValue) return Unsupported();

            return new BatteryLimitCapabilities
            {
                Mode = BatteryLimitControlMode.Threshold,
                CanWrite = false,
                ProviderName = "ASUS System Control Interface",
                Source = "Windows Registry",
                Note = "目前值來自 ASUS System Control Interface；本程式未取得可寫入的 ACPI／WMI 介面。",
                CurrentPercent = current,
                Thresholds = new int[0]
            };
        }

        private static int? ReadAsusRegistryLimit()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(AsusChargeLimitRegistryPath, false))
                {
                    if (key == null) return null;
                    object raw = key.GetValue("ChargingRate", null);
                    if (raw == null) return null;
                    int value = Convert.ToInt32(raw);
                    return value >= 40 && value <= 100 ? (int?)value : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetAsusRegistryLimit(int percent)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(AsusChargeLimitRegistryPath, true))
                {
                    if (key == null) return false;
                    key.SetValue("ChargingRate", percent, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch
            {
                // Direct ACPI/WMI control can still succeed for non-elevated users.
                return false;
            }
        }

        private static int? DecodeLimitValue(int raw)
        {
            int[] candidates =
            {
                raw,
                raw - 65536,
                raw & 0xFFFF,
                (raw >> 16) & 0xFFFF,
                raw & 0xFF
            };

            foreach (int candidate in candidates)
            {
                if (candidate >= 40 && candidate <= 100)
                    return candidate;
            }

            return null;
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
