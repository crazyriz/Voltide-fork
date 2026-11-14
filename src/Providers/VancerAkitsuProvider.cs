
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.IO;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// Vancer Akitsu Mouse battery provider.
    /// Based on the official Vancer Web Hub getBatPer() implementation.
    /// VID 373E / PID 000A, MI_02, UsagePage FFFF, Usage 0000.
    /// </summary>
    public sealed class VancerAkitsuProvider : IDeviceProvider
    {
        private const int VendorId = 0x373E;
        private const int ProductId = 0x000A;
        private const int UsagePage = 0xFFFF;
        private const int Usage = 0x0000;
        private const int FeatureLength = 65;
        private const int ReportId = 0x00;

        public string Name
        {
            get { return "Vancer Akitsu"; }
        }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            var result = new List<DeviceInfo>();

            if (catalog == null)
                return result;

            foreach (HidDeviceInfo d in catalog)
            {
                if (d == null)
                    continue;

                if (d.VendorId != VendorId || d.ProductId != ProductId)
                    continue;

                if (d.UsagePage != UsagePage || d.Usage != Usage)
                    continue;

                if (d.FeatureReportLength < FeatureLength)
                    continue;

                if (string.IsNullOrEmpty(d.Path) ||
                    d.Path.IndexOf("&MI_02",
                        StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                result.Add(new DeviceInfo
                {
                    Id = "373E:000A:AKITSU",
                    Brand = "Vancer",
                    Model = "Akitsu Mouse",
                    Name = "Vancer Akitsu Mouse",
                    ShortModel = "Akitsu",
                    VendorId = VendorId,
                    ProductId = ProductId,
                    HidPath = d.Path,
                    ProviderName = Name
                });
            }

            return result;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            var reading = new DeviceReading
            {
                UpdatedAt = DateTime.Now,
                Online = false,
                Detail = "等待 HID 連線",
                Error = null
            };

            if (device == null || string.IsNullOrEmpty(device.HidPath))
            {
                reading.Error = "找不到 Vancer Akitsu HID 路徑";
                reading.Detail = "HID 裝置不存在";
                return reading;
            }

            IntPtr handle = IntPtr.Zero;

            try
            {
                handle = HidNative.CreateFile(
                    device.HidPath,
                    HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                    HidNative.FILE_SHARE_RW,
                    IntPtr.Zero,
                    HidNative.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle == new IntPtr(-1) || handle == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "無法開啟 Vancer Akitsu MI_02 HID 裝置");

                // WebHID data[2]=02, data[3]=02, data[5]=83.
                // Windows HID buffer includes Report ID at byte 0.
                byte[] tx = new byte[FeatureLength];

                tx[0] = ReportId;
                tx[1] = 0x00;
                tx[2] = 0x00;
                tx[3] = 0x02;
                tx[4] = 0x02;
                tx[5] = 0x00;
                tx[6] = 0x83;

                if (!HidNative.HidD_SetFeature(handle, tx, tx.Length))
                    throw new InvalidOperationException(
                        "HidD_SetFeature 電量查詢指令傳送失敗");

                Thread.Sleep(100);

                byte[] rx = new byte[FeatureLength];
                rx[0] = ReportId;

                if (!HidNative.HidD_GetFeature(handle, rx, rx.Length))
                    throw new InvalidOperationException(
                        "HidD_GetFeature 讀取電量回覆失敗");

                reading.Raw = ToHex(rx);

                

                int percent;
                bool charging;

                if (TryParseWebHubReply(rx, out percent, out charging))
                {
                    if (percent < 0 || percent > 100)
                        throw new InvalidOperationException(
                            "Vancer 回覆的電量超出 0–100：" +
                            percent.ToString(CultureInfo.InvariantCulture));

                    reading.Online = true;
                    reading.Percent = percent;
                    reading.Charging = charging;
                    reading.UpdatedAt = DateTime.Now;
                    reading.Detail = "HID 連線成功 · 電量 " +
                        percent.ToString(CultureInfo.InvariantCulture) + "%" +
                        (charging ? " · 充電中" : "");
                    reading.Error = null;
                }
                else
                {
                    reading.Online = true;
                    reading.Detail = "HID 連線成功 · 尚未辨識電量回覆";
                    reading.Error = "官方 Web Hub 回覆格式不符；RX=" +
                        ToHex(rx);
                }
            }
            catch (Exception ex)
            {
                reading.Online = false;
                reading.Error = ex.Message;
                reading.Detail = "HID 讀取失敗";
            }
            finally
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    HidNative.CloseHandle(handle);
            }

            return reading;
        }

        private static bool TryParseWebHubReply(
            byte[] report,
            out int percent,
            out bool charging)
        {
            percent = 0;
            charging = false;

            if (report == null || report.Length < FeatureLength)
                return false;

            // Official format A.
            // Windows offsets include Report ID at byte 0.
            if (report[2] == 0xA1 &&
                report[5] == 0x02 &&
                report[7] == 0x83)
            {
                charging = report[8] == 0x01;
                percent = report[9];
                return true;
            }

            // Official format B.
            // Akitsu confirmed response:
            // 00 A1 00 02 02 00 83 00 3C ...
            if (report[1] == 0xA1 &&
                report[4] == 0x02 &&
                report[6] == 0x83)
            {
                charging = report[7] == 0x01;
                percent = report[8];
                return true;
            }

            return false;
        }

        private static string ToHex(byte[] data)
        {
            if (data == null)
                return string.Empty;

            return BitConverter.ToString(data).Replace("-", " ");
        }
    }
}
