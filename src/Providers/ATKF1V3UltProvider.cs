// Adapted from the verified ATK A9 Air+ HID implementation.
// Device identity is presented as ATK F1 V3 Ultimate+.
// The HID protocol and identifiers are intentionally unchanged.
using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// ATK F1 V3 Ultimate+ 2.4G receiver.
    ///
    /// Receiver: VID 0x373B, PID 0x1278, product string
    /// "Wireless mouse 8k dongle-L", manufacturer "Compx".
    /// Vendor collection: usage page 0xFF02, input/output report length 17.
    ///
    /// Battery protocol retained from the previously verified implementation:
    /// query frame: 17 bytes; report ID 0x08; command 0x04.
    /// response: byte 6 = percentage, byte 7 = charging flag,
    /// bytes 8..9 = millivolts (big endian).
    /// </summary>
    public class ATKF1V3UltProvider : IDeviceProvider
    {
        private const ushort AtkVid = 0x373B;
        private const ushort F1V3UltimatePlusPid = 0x1278;
        private const ushort VendorPage = 0xFF02;

        private const int FrameSize = 17;
        private const byte ReportId = 0x08;
        private const byte CmdBattery = 0x04;
        private const byte FrameSum = 0x4D;

        private const int PercentIndex = 6;
        private const int ChargingIndex = 7;
        private const int MillivoltIndex = 8;

        public string Name { get { return "ATK F1 V3 Ultimate+"; } }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            Dictionary<string, HidDeviceInfo> candidates =
                new Dictionary<string, HidDeviceInfo>();

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != AtkVid) continue;
                if (h.ProductId != F1V3UltimatePlusPid) continue;
                if (h.UsagePage != VendorPage) continue;
                if (h.InputReportLength != FrameSize) continue;
                if (h.OutputReportLength != FrameSize) continue;

                string key = h.VendorId.ToString("X4") + ":" +
                             h.ProductId.ToString("X4");
                if (!candidates.ContainsKey(key))
                    candidates.Add(key, h);
            }

            foreach (KeyValuePair<string, HidDeviceInfo> kv in candidates)
            {
                HidDeviceInfo h = kv.Value;

                DeviceInfo d = new DeviceInfo();
                d.Id = "atk-f1-v3-ult-" + kv.Key;
                d.Brand = "ATK";
                d.Model = "F1 V3 Ultimate+";
                d.Name = "ATK F1 V3 Ultimate+";
                d.ShortModel = "F1 V3 Ultimate+";
                d.Kind = DeviceKind.Mouse;
                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;
                d.HidPath = h.Path;
                d.ProviderName = Name;
                found.Add(d);
            }

            return found;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading r = new DeviceReading();
            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;

            try
            {
                byte[] cmd = NewQuery();

                using (HidSession s = new HidSession(device.HidPath, FrameSize, FrameSize))
                {
                    s.Flush(30);

                    byte[] resp = null;

                    for (int attempt = 0; attempt < 3 && resp == null; attempt++)
                    {
                        if (attempt > 0)
                            System.Threading.Thread.Sleep(120);

                        if (!s.SetOutputReport(cmd))
                        {
                            r.Error = "发送失败（设备被占用？）";
                            return r;
                        }

                        resp = WaitFor(s, ReportId, CmdBattery, 700);
                    }

                    if (resp == null)
                    {
                        r.Sleeping = true;
                        r.Error = "设备休眠";
                        return r;
                    }

                    r.Raw = Hex(resp);

                    int level = resp[PercentIndex];
                    if (level > 100)
                    {
                        r.Online = true;
                        r.Percent = null;
                        r.Error = "读数异常";
                        return r;
                    }

                    int millivolts =
                        (resp[MillivoltIndex] << 8) |
                        resp[MillivoltIndex + 1];

                    r.Percent = level;
                    r.Level = BatteryMath.FromPercent(level);
                    r.Charging = resp[ChargingIndex] == 0x01;
                    r.Online = true;
                    r.Detail = "2.4G ";
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }

            return r;
        }

        private static byte[] NewQuery()
        {
            byte[] cmd = new byte[FrameSize];
            cmd[0] = ReportId;
            cmd[1] = CmdBattery;
            cmd[FrameSize - 1] = Checksum(cmd);
            return cmd;
        }

        private static byte Checksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 1; i < frame.Length - 1; i++)
                sum += frame[i];

            return (byte)((FrameSum - sum) & 0xFF);
        }

        private static byte[] WaitFor(
            HidSession s, byte rid, byte cmd, int timeoutMs)
        {
            for (int i = 0; i < 12; i++)
            {
                byte[] p = s.Read(Math.Max(60, timeoutMs / 4));
                if (p == null)
                    return null;

                if (p.Length > MillivoltIndex + 1 &&
                    p[0] == rid && p[1] == cmd)
                    return p;
            }

            return null;
        }

        private static string Hex(byte[] b)
        {
            if (b == null)
                return "";

            System.Text.StringBuilder sb =
                new System.Text.StringBuilder();

            for (int i = 0; i < b.Length; i++)
            {
                sb.Append(b[i].ToString("X2"));
                sb.Append(' ');
            }

            return sb.ToString().Trim();
        }
    }
}
