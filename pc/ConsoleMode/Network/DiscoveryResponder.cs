using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using ConsoleMode.Core;

namespace ConsoleMode.Network;

/// <summary>
/// Responde a los broadcast UDP de la app móvil ("CONSOLEMODE_DISCOVER_V1") con los datos de conexión,
/// para que el móvil pueda encontrar el PC sin escanear el QR.
/// </summary>
public sealed class DiscoveryResponder : IDisposable
{
    public const string DiscoverMessage = "CONSOLEMODE_DISCOVER_V1";

    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;

    public DiscoveryResponder(AppSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, AppSettings.DiscoveryPort));
        _ = LoopAsync(_udp, _cts.Token);
    }

    private async Task LoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(ct);
                if (Encoding.UTF8.GetString(received.Buffer).Trim() != DiscoverMessage)
                {
                    continue;
                }

                var reply = new JsonObject
                {
                    ["type"] = "consolemode",
                    ["version"] = RemoteServer.ProtocolVersion,
                    ["pcId"] = _settings.PcId,
                    ["pcName"] = Environment.MachineName,
                    ["port"] = _settings.Port,
                };
                var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString());
                await udp.SendAsync(bytes, received.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // p.ej. ICMP "port unreachable" de un envío anterior: seguimos escuchando.
            }
            catch (Exception ex)
            {
                Log.Error("Error en el descubrimiento UDP", ex);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
    }
}

/// <summary>Tarjeta de red a la que el móvil envía el paquete mágico de Wake-on-LAN.</summary>
public sealed record WakeTarget(string Mac, string Broadcast);

public static class NetworkInfo
{
    /// <summary>IPv4 locales utilizables, primero las de interfaces con puerta de enlace (la LAN "real").</summary>
    public static IReadOnlyList<string> GetLocalAddresses()
    {
        var result = new List<(string Ip, bool HasGateway)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                            !g.Address.Equals(IPAddress.Any));
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr.Address))
                {
                    continue;
                }
                var ip = addr.Address.ToString();
                if (ip.StartsWith("169.254."))
                {
                    continue;
                }
                result.Add((ip, hasGateway));
            }
        }

        return result.OrderByDescending(r => r.HasGateway).Select(r => r.Ip).Distinct().ToList();
    }

    /// <summary>
    /// MAC y dirección de broadcast de cada tarjeta de red física conectada, para que el móvil pueda encender el PC
    /// con Wake-on-LAN. Primero las de cable y las que tienen puerta de enlace.
    /// </summary>
    public static IReadOnlyList<WakeTarget> GetWakeTargets()
    {
        var result = new List<(WakeTarget Target, bool Wired, bool HasGateway)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or
                    NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Wireless80211) ||
                VirtualAdapter.IsMatch(nic.Description))
            {
                continue;
            }

            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length != 6 || mac.All(b => b == 0))
            {
                continue;
            }

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                            !g.Address.Equals(IPAddress.Any));
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr.Address) ||
                    addr.Address.ToString().StartsWith("169.254."))
                {
                    continue;
                }
                var ip = addr.Address.GetAddressBytes();
                var mask = addr.IPv4Mask.GetAddressBytes();
                var broadcast = new IPAddress(ip.Select((b, i) => (byte)(b | ~mask[i])).ToArray());
                result.Add((new WakeTarget(string.Join(":", mac.Select(b => b.ToString("X2"))), broadcast.ToString()),
                    nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211, hasGateway));
                break;
            }
        }

        return result.OrderByDescending(r => r.Wired).ThenByDescending(r => r.HasGateway).Select(r => r.Target)
            .DistinctBy(t => t.Mac).ToList();
    }

    private static readonly System.Text.RegularExpressions.Regex VirtualAdapter = new(
        "virtual|hyper-v|vmware|virtualbox|vpn|tap-|wireguard|tailscale|zerotier|wan miniport|bluetooth",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Contenido del QR: consolemode://pair?h=ip1,ip2&amp;p=puerto&amp;id=pcId&amp;n=nombre</summary>
    public static string PairingUri(AppSettings settings)
    {
        var hosts = string.Join(",", GetLocalAddresses());
        return $"consolemode://pair?h={Uri.EscapeDataString(hosts)}&p={settings.Port}" +
               $"&id={Uri.EscapeDataString(settings.PcId)}&n={Uri.EscapeDataString(Environment.MachineName)}";
    }
}
