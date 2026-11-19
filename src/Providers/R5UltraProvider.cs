
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Voltide.Core;
using Voltide.Interop;
using System.IO;

namespace Voltide.Providers
{
    /// <summary>
    /// ATTACK SHARK R5 Ultra battery provider.
    ///
    /// VID: 373E
    /// PID: 0047
    /// Interface: MI_02
    /// Feature Report: 65 bytes
    ///
    /// TX:
    /// 00 00 00 02 02 00 83 ...
    ///
    /// RX:
    /// 00 A1 00 02 02 00 83 01 37 ...
    ///
    /// RX[7] = Charging
    /// RX[8] = Battery percentage
    /// </summary>
    public class R5UltraProvider : IDeviceProvider
    {
        public string Name
        {
            get { return "ATTACK SHARK R5 Ultra"; }
        }

        private const ushort VendorId = 0x373E;
        private const ushort ProductId = 0x0047;

        private const ushort UsagePage = 0xFFFF;
        private const ushort Usage = 0x0000;

        private const int FeatureLength = 65;
        private const int FeatureDelayMs = 100;

        private const byte ResponseHeader = 0xA1;
        private const byte CommandId = 0x02;
        private const byte CommandType = 0x83;

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;

        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;

        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto,
            SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_SetFeature(
            SafeFileHandle device,
            byte[] report,
            int reportLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetFeature(
            SafeFileHandle device,
            byte[] report,
            int reportLength);

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> result = new List<DeviceInfo>();

            if (catalog == null)
                return result;

            StringBuilder debug = new StringBuilder();

debug.AppendLine("=== R5 DISCOVER DEBUG ===");
debug.AppendLine("Catalog count: " +
    (catalog == null ? -1 : catalog.Count));

if (catalog != null)
{
    foreach (HidDeviceInfo d in catalog)
    {
        if (d.VendorId == VendorId &&
            d.ProductId == ProductId)
        {
            debug.AppendLine("--------------------------------");
            debug.AppendLine("VID: " + d.VendorId.ToString("X4"));
            debug.AppendLine("PID: " + d.ProductId.ToString("X4"));
            debug.AppendLine("UsagePage: " + d.UsagePage.ToString("X4"));
            debug.AppendLine("Usage: " + d.Usage.ToString("X4"));
            debug.AppendLine("InputLength: " + d.InputReportLength);
            debug.AppendLine("OutputLength: " + d.OutputReportLength);
            debug.AppendLine("FeatureLength: " + d.FeatureReportLength);
            debug.AppendLine("Path: " + d.Path);
        }
    }
}

try
{
    File.WriteAllText(
        Path.Combine(
            Path.GetTempPath(),
            "Voltide_R5_Discover.txt"),
        debug.ToString());
}
catch
{
}

if (catalog == null)
    return result;

foreach (HidDeviceInfo d in catalog)
{
    if (d.VendorId != VendorId ||
        d.ProductId != ProductId)
        continue;

    if (string.IsNullOrEmpty(d.Path) ||
        d.Path.IndexOf(
            "mi_02",
            StringComparison.OrdinalIgnoreCase) < 0)
        continue;

    DeviceInfo info = new DeviceInfo();

info.Id = "373E:0047";
info.Kind = DeviceKind.Mouse;
info.Brand = "ATTACK SHARK";
info.Model = "R5 Ultra";
info.Name = "ATTACK SHARK R5 Ultra";
info.ShortModel = "R5 Ultra";
info.VendorId = VendorId;
info.ProductId = ProductId;
info.HidPath = d.Path;
info.ProviderName = "R5UltraProvider";

    result.Add(info);
}
            return result;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading reading = new DeviceReading();

            reading.UpdatedAt = DateTime.UtcNow;
            reading.Online = false;
            reading.Sleeping = false;
            reading.Level = BatteryLevel.Unknown;
            reading.Charging = false;
            reading.Percent = null;
            reading.Error = null;

            if (device == null ||
                string.IsNullOrEmpty(device.HidPath))
            {
                reading.Error = "HID 裝置路徑無效";
                reading.Detail = reading.Error;
                return reading;
            }

            SafeFileHandle handle = null;

            try
            {
                handle = CreateFile(
                    device.HidPath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle == null || handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();

                    reading.Error = string.Format(
                        "無法開啟 HID 裝置 ({0})",
                        error);

                    return reading;
                }

                byte[] report = new byte[FeatureLength];

                // Report ID
                report[0] = 0x00;

                // Official Web Driver command.
                // Windows buffer includes Report ID at index 0.
                report[3] = CommandId;
                report[4] = CommandId;
                report[6] = CommandType;

                // Send Feature Report.
                if (!HidD_SetFeature(
                    handle,
                    report,
                    report.Length))
                {
                    int error = Marshal.GetLastWin32Error();

                    reading.Error = string.Format(
                        "HID Feature 傳送失敗 ({0})",
                        error);

                    return reading;
                }

                Thread.Sleep(FeatureDelayMs);

                // Receive Feature Report.
                if (!HidD_GetFeature(
                    handle,
                    report,
                    report.Length))
                {
                    int error = Marshal.GetLastWin32Error();

                    reading.Error = string.Format(
                        "HID Feature 接收失敗 ({0})",
                        error);

                    return reading;
                }

                reading.Raw = Hex(report, report.Length);

                                // Validate official response format:
                // 00 A1 00 02 02 00 83 01 37 ...
                bool valid =
                    report[1] == ResponseHeader &&
                    report[3] == CommandId &&
                    report[4] == CommandId &&
                    report[6] == CommandType;

                if (!valid)
                {
                    reading.Error =
                        "HID 回覆格式不正確: " +
                        Hex(report, 12);

                    return reading;
                }

                int chargingValue = report[7];
                int battery = report[8];

                if (battery > 100)
                {
                    reading.Error = string.Format(
                        "電量數值超出範圍: {0}",
                        battery);

                    return reading;
                }

                // Successful battery reading.
                reading.Online = true;
                reading.Sleeping = false;
                reading.Percent = battery;
                reading.Charging = chargingValue == 1;
                reading.Level = BatteryMath.FromPercent(battery);

                reading.Detail = string.Format(
                    "HID 連線成功 · 電量 {0}%{1}",
                    battery,
                    reading.Charging
                        ? " · 充電中"
                        : "");

                reading.Error = null;
                reading.UpdatedAt = DateTime.UtcNow;

                return reading;
            }
            catch (Exception ex)
            {
                reading.Error = "HID 讀取錯誤: " + ex.Message;
                return reading;
            }
            finally
            {
                if (!reading.Online &&
                    !string.IsNullOrEmpty(reading.Error))
                {
                    reading.Detail = reading.Error;
                }

                if (handle != null)
                    handle.Dispose();
            }
        }

        private static string Hex(byte[] data, int length)
        {
            if (data == null)
                return string.Empty;

            int count = Math.Min(data.Length, length);

            StringBuilder sb =
                new StringBuilder(count * 3);

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append(" ");

                sb.Append(data[i].ToString("X2"));
            }

            return sb.ToString();
        }
    }
}
