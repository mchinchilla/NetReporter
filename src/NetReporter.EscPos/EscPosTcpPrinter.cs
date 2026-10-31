using System.Net.Sockets;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos;

/// <summary>Destino de los bytes ESC/POS: red, USB, puerto serie, spooler… Implementaciones propias son bienvenidas.</summary>
public interface IEscPosTransport
{
    /// <summary>Envía los bytes completos; lanza <see cref="EscPosPrinterException"/> si la impresora no los acepta.</summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

/// <summary>Conexión TCP en crudo (puerto 9100, "JetDirect"). Todo se configura en tiempo de ejecución.</summary>
public sealed class EscPosTcpPrinterOptions
{
    /// <summary>IP o nombre de la impresora.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 9100;

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Tiempo máximo para que la impresora acepte todos los bytes del ticket.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Desactiva el algoritmo de Nagle: el ticket sale en cuanto se escribe.</summary>
    public bool NoDelay { get; set; } = true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ArgumentException("Host es obligatorio (IP o nombre de la impresora).", nameof(Host));
        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port va de 1 a 65535 (9100 es el habitual).");
        if (ConnectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "ConnectTimeout debe ser mayor que cero.");
        if (SendTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SendTimeout), SendTimeout, "SendTimeout debe ser mayor que cero.");
    }
}

/// <summary>La impresora no respondió, rechazó la conexión o no aceptó los datos a tiempo.</summary>
public sealed class EscPosPrinterException : IOException
{
    public EscPosPrinterException(string message) : base(message) { }
    public EscPosPrinterException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Envía bytes ESC/POS a una impresora de red por TCP (puerto 9100 por defecto). Abre una conexión por
/// envío: las impresoras de tickets aceptan una sola conexión a la vez y la cierran al terminar.
/// </summary>
public sealed class EscPosTcpPrinter : IEscPosTransport
{
    public EscPosTcpPrinter(EscPosTcpPrinterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options;
    }

    public EscPosTcpPrinter(string host, int port = 9100) : this(new EscPosTcpPrinterOptions { Host = host, Port = port }) { }

    /// <summary>Se leen en cada envío, así que pueden ajustarse en tiempo de ejecución.</summary>
    public EscPosTcpPrinterOptions Options { get; }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var target = $"{Options.Host}:{Options.Port}";

        using var client = new TcpClient();
        client.NoDelay = Options.NoDelay;

        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(Options.ConnectTimeout);
            try
            {
                await client.ConnectAsync(Options.Host, Options.Port, connect.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new EscPosPrinterException($"La impresora {target} no respondió al conectar en {Options.ConnectTimeout.TotalSeconds:0.#} s.");
            }
            catch (SocketException ex)
            {
                throw new EscPosPrinterException($"No se pudo conectar con la impresora {target} ({ex.SocketErrorCode}).", ex);
            }
        }

        using (var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            send.CancelAfter(Options.SendTimeout);
            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(data, send.Token).ConfigureAwait(false);
                await stream.FlushAsync(send.Token).ConfigureAwait(false);
                // Cierre ordenado del lado de envío: la impresora recibe el último byte antes del FIN.
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new EscPosPrinterException($"La impresora {target} no aceptó los {data.Length} bytes en {Options.SendTimeout.TotalSeconds:0.#} s.");
            }
            catch (IOException ex) when (ex is not EscPosPrinterException)
            {
                throw new EscPosPrinterException($"Error enviando a la impresora {target}: {ex.Message}", ex);
            }
            catch (SocketException ex)
            {
                throw new EscPosPrinterException($"Error enviando a la impresora {target} ({ex.SocketErrorCode}).", ex);
            }
        }
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
}
