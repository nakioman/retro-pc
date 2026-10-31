using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RetroBox.Core;

public static class RetroBoxNetworkAddresses
{
    // IPv4 only: the address is read off the selector screen and typed into a browser, and a
    // link-local IPv6 address is neither memorable nor reachable without a zone id.
    public static IReadOnlyList<string> GetLanIPv4Addresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
