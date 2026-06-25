using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LibraryAdminPC.Utils;

public static class NetworkHelper
{
    public static List<string> GetLanIPv4Addresses()
    {
        var result = new List<(int score, string ip)>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

            // ให้คะแนน interface เพื่อเรียงลำดับ: Ethernet/Wi-Fi มาก่อน
            var score = ni.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet => 100,
                NetworkInterfaceType.GigabitEthernet => 95,
                NetworkInterfaceType.Wireless80211 => 90,
                _ => 10
            };

            var ipProps = ni.GetIPProperties();
            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue; // APIPA (ไม่ใช่ LAN จริงส่วนใหญ่)

                result.Add((score, ip));
            }
        }

        return result
            .OrderByDescending(x => x.score)
            .Select(x => x.ip)
            .Distinct()
            .ToList();
    }
}