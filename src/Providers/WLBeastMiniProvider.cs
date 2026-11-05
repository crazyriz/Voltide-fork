
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Voltide.Core;
using Voltide.Interop;


namespace Voltide.Providers
{
    public sealed class WLBeastMiniProvider : IDeviceProvider
    {
        private const ushort VID = 0x36A7;
        private const ushort PID = 0xA885;
        private const int FeatureLength = 65;

        public string Name
        {
            get
            {
                return "WLmouse BEAST MINI 8K";
            }
        }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            return catalog
                .Where(d =>
                    d.VendorId == VID &&
                    d.ProductId == PID &&
                    d.UsagePage == 0xFFFF &&
                    d.Usage == 0x0000 &&
                    d.FeatureReportLength == FeatureLength &&
                    d.Path != null &&
                    d.Path.IndexOf(
                        "mi_02",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(d => new DeviceInfo
                {
                    Id = "36A7:A885:WLmouseMini",
                    Brand = "WL",
                    Model = "BEAST MINI 8K",
                    Name = "WLmouse BEAST MINI 8K",
                    ShortModel = "BEAST MINI 8K",
                    Kind = DeviceKind.Mouse,
                    VendorId = d.VendorId,
                    ProductId = d.ProductId,
                    HidPath = d.Path,
                    ProviderName = Name
                })
                .ToList();
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading result = new DeviceReading();

            result.UpdatedAt = DateTime.Now;
            result.Online = false;
            result.Level = BatteryLevel.Unknown;
            result.Sleeping = false;

            try
            {
                using (SafeFileHandle handle = CreateFile(
                    device.HidPath,
                    0xC0000000,
                    0x00000003,
                    IntPtr.Zero,
                    3,
                    0,
                    IntPtr.Zero))
                {
                    if (handle == null || handle.IsInvalid)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "無法開啟 mi_02 Feature Report 介面");
                    }

                    // Report ID = 0
                    // 官方 WebHID payload:
                    // [2] = 0x02
                    // [3] = 0x02
                    // [5] = 0x83
                    //
                    // Windows HID buffer 包含 Report ID，
                    // 因此所有 payload index 都需要 +1。

                    byte[] command = new byte[FeatureLength];

                    command[0] = 0x00;
                    command[3] = 0x02;
                    command[4] = 0x02;
                    command[6] = 0x83;

                    if (!HidD_SetFeature(
                        handle,
                        command,
                        (uint)command.Length))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "電量查詢指令傳送失敗");
                    }

                    Thread.Sleep(100);

                    byte[] response = new byte[FeatureLength];
                    response[0] = 0x00;

                    if (!HidD_GetFeature(
                        handle,
                        response,
                        (uint)response.Length))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "讀取 Feature Report 失敗");
                    }

                    result.Online = true;
                    



                    // 官方 JS 回覆格式 A
if (response[2] == 0xA1 &&
    response[5] == 0x02 &&
    response[7] == 0x83)
{
    result.Percent = response[9];
    result.Level = BatteryMath.FromPercent(response[9]);

    result.Detail = "2.4G";
}
// 官方 JS 回覆格式 B
else if (response[1] == 0xA1 &&
         response[4] == 0x02 &&
         response[6] == 0x83)
{
    result.Percent = response[8];
    result.Level = BatteryMath.FromPercent(response[8]);

    result.Detail = "2.4G";
}
else
{
    result.Detail = "WLmouse 電量回覆格式不符";
}
                    
                }
            }
            catch (Exception ex)
            {
                result.Online = false;
                result.Error = ex.Message;
                result.Detail = "WLmouse Feature Report 通訊失敗";
            }

            return result;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Auto,
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
            byte[] reportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetFeature(
            SafeFileHandle device,
            byte[] reportBuffer,
            uint reportBufferLength);
    }
}
