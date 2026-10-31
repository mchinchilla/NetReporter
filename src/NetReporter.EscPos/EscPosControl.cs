using System.Net;
using System.Net.Sockets;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos;

/// <summary>Comandos ESC/POS sueltos, para mandar a la impresora sin un documento (abrir la gaveta, cortar, alimentar).</summary>
public static class EscPosControl
{
    /// <summary><c>ESC @</c>: deja la impresora en su estado por defecto.</summary>
    public static byte[] Initialize() => [0x1B, 0x40];

    /// <summary>
    /// <c>ESC p m t1 t2</c>: pulso en el conector de la gaveta (<paramref name="pin"/> 0 = pin 2, 1 = pin 5) de
    /// <paramref name="onMilliseconds"/> encendido y <paramref name="offMilliseconds"/> apagado (en pasos de 2 ms, máx. 510).
    /// </summary>
    public static byte[] OpenDrawer(int pin = 0, int onMilliseconds = 50, int offMilliseconds = 500)
    {
        if (pin is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(pin), pin, "pin es 0 (pin 2) o 1 (pin 5).");
        if (onMilliseconds is < 2 or > 510) throw new ArgumentOutOfRangeException(nameof(onMilliseconds), onMilliseconds, "onMilliseconds va de 2 a 510.");
        if (offMilliseconds is < 2 or > 510) throw new ArgumentOutOfRangeException(nameof(offMilliseconds), offMilliseconds, "offMilliseconds va de 2 a 510.");
        return [0x1B, 0x70, (byte)pin, (byte)(onMilliseconds / 2), (byte)(offMilliseconds / 2)];
    }

    /// <summary><c>ESC d n</c>: alimenta <paramref name="lines"/> líneas (0–255).</summary>
    public static byte[] Feed(int lines)
    {
        if (lines is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(lines), lines, "lines va de 0 a 255.");
        return lines == 0 ? [] : [0x1B, 0x64, (byte)lines];
    }

    /// <summary><c>GS V m</c>: corte total o parcial (vacío para <see cref="EscPosCut.None"/>).</summary>
    public static byte[] Cut(EscPosCut cut) => cut switch
    {
        EscPosCut.Full => [0x1D, 0x56, 0x00],
        EscPosCut.Partial => [0x1D, 0x56, 0x01],
        _ => [],
    };
}

/// <summary>
/// Qué direcciones de impresora acepta un servidor cuando las elige el cliente (un navegador, una caja):
/// evita que el servidor se convierta en un proxy TCP hacia cualquier destino.
/// </summary>
public static class EscPosPrinterPolicy
{
    /// <summary>
    /// Solo direcciones IP literales privadas (10/8, 172.16/12, 192.168/16, fc00::/7), de loopback o
    /// link-local (169.254/16, fe80::/10), en un puerto válido. Los nombres de host se rechazan: resolverlos
    /// abriría la puerta a destinos arbitrarios.
    /// </summary>
    public static bool IsPrivateAddress(string? host, int port)
    {
        if (port is < 1 or > 65535 || string.IsNullOrWhiteSpace(host) || !IPAddress.TryParse(host.Trim(), out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
    }
}

public static class EscPosTransportExtensions
{
    /// <summary>Renderiza el <see cref="RlRenderList"/> a ESC/POS y lo envía. Los errores de render (fuentes, etc.) saltan antes de tocar la impresora.</summary>
    public static Task PrintAsync(this IEscPosTransport transport, RlRenderList renderList, EscPosRenderer? renderer = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(renderList);
        var bytes = (renderer ?? new EscPosRenderer()).Render(renderList, cancellationToken);
        return transport.SendAsync(bytes, cancellationToken);
    }

    /// <summary>Abre la gaveta de dinero sin imprimir nada (<see cref="EscPosControl.OpenDrawer"/>).</summary>
    public static Task OpenDrawerAsync(this IEscPosTransport transport, int pin = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return transport.SendAsync(EscPosControl.OpenDrawer(pin), cancellationToken);
    }
}
