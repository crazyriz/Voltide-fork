
    
  
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// Experimental Luna33 TYPE33EL receiver provider.
    /// It detects the receiver and reads the confirmed HID Feature Report.
    /// The battery percentage byte is not yet known, so this version reports
    /// raw data rather than displaying a guessed percentage.
    /// </summary>
    public class Luna33EvoProvider : IDeviceProvider
    {
        private const ushort Luna33ELVid = 0x373E;
        private const ushort Luna33ELPid = 0x0084;

        // 0 = use the FeatureReportLength reported by Windows HID.
        // If the read fails, try 65 first, then 0.
        private const int FeatureReportLengthOverride = 0;

        // Interface 2 has 64 data bytes on the USB wire. Windows normally
        // includes the Report ID byte in FeatureReportByteLength (expected 65).
        private const int MinimumFeatureReportLength = 64;

        // Captured OEM command at 96%. It is confirmed traffic, but not yet
        // confirmed to be the battery query. Change only one value at a time.
        private const string CommandHex = "00 00 02 02 00 83";

        // false = test passive GET_FEATURE only.
        private const bool SendCommandBeforeRead = true;

        // Try 20, 50, 100, then 200 if the returned data looks stale/empty.
        private const int ReplyDelayMs = 50;

        private static readonly Dictionary<string, int> ReportLengths =
            new Dictionary<string, int>();

        private static readonly object ReportLengthsLock = new object();

        public string Name
        {
            get { return "Luna33 HID"; }
        }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            HidDeviceInfo selected = null;

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != Luna33ELVid ||    h.ProductId != Luna33ELPid)
                    continue;

                    // TYPE33EL 的 Interface 2 是 Vendor-defined Usage Page。
                    if (h.UsagePage != 0xFFFF)
                    continue;

                int requiredLength = FeatureReportLengthOverride > 0
                    ? FeatureReportLengthOverride
                    : MinimumFeatureReportLength;

                if (h.FeatureReportLength < requiredLength)
                    continue;

                // Interface 2 should be the HID collection with the Feature report.
                if (selected == null ||
                    h.FeatureReportLength > selected.FeatureReportLength)
                {
                    selected = h;
                }
            }

            if (selected == null)
                return found;

            lock (ReportLengthsLock)
            {
                ReportLengths[selected.Path] = selected.FeatureReportLength;
            }

            DeviceInfo d = new DeviceInfo();
            d.Id = "luna33EL-" + Luna33ELPid.ToString("X4");
            d.Brand = "LumaFury";
            d.Model = "TYPE33EL";
            d.Name = "LunaFury TYPE33Evo";
            d.ShortModel = "TYPE33EL";
            d.Kind = DeviceKind.Mouse;
            d.VendorId = selected.VendorId;
            d.ProductId = selected.ProductId;
            d.HidPath = selected.Path;
            d.ProviderName = Name;

            found.Add(d);
            return found;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading reading = new DeviceReading();
            reading.UpdatedAt = DateTime.Now;
            reading.Online = false;
            reading.Level = BatteryLevel.Unknown;

            int bufferLength = FeatureReportLengthOverride;
            if (bufferLength <= 0)
            {
                lock (ReportLengthsLock)
                {
                    if (ReportLengths.ContainsKey(device.HidPath))
                        bufferLength = ReportLengths[device.HidPath];
                }
            }

            // Fallback only if the catalog length was not retained.
            if (bufferLength <= 0)
                bufferLength = 65;

            IntPtr handle = OpenDevice(device.HidPath);
            if (handle == new IntPtr(-1))
            {
                reading.Error = "無法開啟 HID，Win32 error " +
                    Marshal.GetLastWin32Error();
                return reading;
            }

            try
            {
                byte[] command = ParseHex(CommandHex);

                if (SendCommandBeforeRead)
                {
                    byte[] setBuffer = BuildFeatureBuffer(bufferLength, command);
                    if (!HidNative.HidD_SetFeature(handle, setBuffer, setBuffer.Length))
                    {
                        reading.Error = "HidD_SetFeature 失敗，Win32 error " +
                            Marshal.GetLastWin32Error();
                        return reading;
                    }

                    Thread.Sleep(ReplyDelayMs);
                }

                byte[] getBuffer = new byte[bufferLength];
                getBuffer[0] = 0x00; // Report ID 0

                if (!HidNative.HidD_GetFeature(handle, getBuffer, getBuffer.Length))
                {
                    reading.Error = "HidD_GetFeature 失敗，Win32 error " +
                        Marshal.GetLastWin32Error();
                    return reading;
                }

                // Windows HID buffer[0] is Report ID. USB payload starts at [1].
                byte[] payload = new byte[getBuffer.Length - 1];
                Array.Copy(getBuffer, 1, payload, 0, payload.Length);

                reading.Online = true;
                reading.Sleeping = false;
                // 暫定協定：第 6 byte 為充電狀態，第 7 byte 為電量
int battery = payload[7];
int charging = payload[6];

if (battery <= 100)
{
    reading.Percent = battery;
    reading.Level = battery >= 100
        ? BatteryLevel.Full
        : battery >= 70
            ? BatteryLevel.High
            : battery >= 30
                ? BatteryLevel.Medium
                : BatteryLevel.Low;
}
else
{
    reading.Percent = null;
    reading.Level = BatteryLevel.Unknown;
}

reading.Charging = charging == 1;
reading.Detail = "2.4G";
            }
            catch (Exception ex)
            {
                reading.Error = ex.Message;
                return reading;
            }
            finally
            {
                HidNative.CloseHandle(handle);
            }
            return reading;
        }

        private static IntPtr OpenDevice(string path)
        {
            return HidNative.CreateFile(
            path,
            HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_RW,
            IntPtr.Zero,
            HidNative.OPEN_EXISTING,
            0,
            IntPtr.Zero);
        }

                private static byte[] BuildFeatureBuffer(int totalLength, byte[] payload)
        {
            byte[] buffer = new byte[totalLength];
            buffer[0] = 0x00; // HID Report ID
            int copyLength = Math.Min(payload.Length, buffer.Length - 1);
            Array.Copy(payload, 0, buffer, 1, copyLength);
            return buffer;
        }

        private static byte[] ParseHex(string value)
        {
            string[] parts = value.Split(
                new char[] { ' ', '\t', '\r', '\n', '-' },
                StringSplitOptions.RemoveEmptyEntries);

            byte[] result = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = Convert.ToByte(parts[i], 16);

            return result;
        }

        private static string Hex(byte[] data, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count && i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
