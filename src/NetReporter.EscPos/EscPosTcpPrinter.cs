using System.Net.Sockets;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos;

/// <summary>Destino de los bytes ESC/POS: red, USB, puerto serie, spooler… Implementaciones propias son bienvenidas.</summary>
public interface IEscPosTransport
{
    /// <summary>Envía los bytes completos; lanza <see cref="EscPosPrinterException"/> si la impresora no los acepta.</summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

/// <summary>Transporte que además sabe preguntar el estado de la impresora (<c>DLE EOT</c>) antes de imprimir.</summary>
public interface IEscPosStatusSource
{
    /// <summary>Lanza <see cref="EscPosPrinterException"/> si la impresora no responde (algunos adaptadores de red no reenvían el estado).</summary>
    Task<EscPosPrinterStatus> QueryStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Estado en tiempo real de una impresora Epson-compatible (<c>DLE EOT 1</c>, <c>2</c> y <c>4</c>).</summary>
public readonly record struct EscPosPrinterStatus(
    bool Online,
    bool CoverOpen,
    bool PaperEnd,
    bool PaperNearEnd,
    bool Error,
    bool FeedButtonPressed,
    bool DrawerPinHigh)
{
    /// <summary>En línea, tapa cerrada, con papel y sin error.</summary>
    public bool ReadyToPrint => Online && !CoverOpen && !PaperEnd && !Error;

    /// <summary>Código estable para la interfaz: <c>ready</c>, <c>offline</c>, <c>cover-open</c>, <c>paper-end</c> o <c>error</c>.</summary>
    public string Code => !Online ? "offline" : CoverOpen ? "cover-open" : PaperEnd ? "paper-end" : Error ? "error" : "ready";

    /// <summary>Decodifica los tres bytes de respuesta (impresora, causa de offline, sensor de papel).</summary>
    public static EscPosPrinterStatus FromBytes(byte printer, byte offlineCause, byte paperSensor) => new(
        Online: (printer & 0x08) == 0,
        CoverOpen: (offlineCause & 0x04) != 0,
        PaperEnd: (paperSensor & 0x60) != 0,
        PaperNearEnd: (paperSensor & 0x0C) != 0,
        Error: (offlineCause & 0x40) != 0,
        FeedButtonPressed: (offlineCause & 0x08) != 0,
        DrawerPinHigh: (printer & 0x04) != 0);
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

    /// <summary>Espera máxima de cada byte de estado en <see cref="EscPosTcpPrinter.QueryStatusAsync"/>.</summary>
    public TimeSpan StatusTimeout { get; set; } = TimeSpan.FromSeconds(2);

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
        if (StatusTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StatusTimeout), StatusTimeout, "StatusTimeout debe ser mayor que cero.");
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
public sealed class EscPosTcpPrinter : IEscPosTransport, IEscPosStatusSource
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

    private string Target => $"{Options.Host}:{Options.Port}";

    private async Task<TcpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            client.NoDelay = Options.NoDelay;
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(Options.ConnectTimeout);
            await client.ConnectAsync(Options.Host, Options.Port, connect.Token).ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new EscPosPrinterException($"La impresora {Target} no respondió al conectar en {Options.ConnectTimeout.TotalSeconds:0.#} s.");
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new EscPosPrinterException($"No se pudo conectar con la impresora {Target} ({ex.SocketErrorCode}).", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Pregunta <c>DLE EOT 1</c>, <c>2</c> y <c>4</c> por una conexión propia. Útil antes de imprimir para
    /// avisar "sin papel" o "tapa abierta" en vez de mandar un ticket que nunca sale.
    /// </summary>
    public async Task<EscPosPrinterStatus> QueryStatusAsync(CancellationToken cancellationToken = default)
    {
        Options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        using var client = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        var answers = new byte[3];
        byte[] kinds = [1, 2, 4];
        for (var i = 0; i < answers.Length; i++)
        {
            using var status = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            status.CancelAfter(Options.StatusTimeout);
            try
            {
                await stream.WriteAsync(new byte[] { 0x10, 0x04, kinds[i] }, status.Token).ConfigureAwait(false);
                var answer = new byte[1];
                var read = await stream.ReadAsync(answer, status.Token).ConfigureAwait(false);
                if (read == 0) throw new EscPosPrinterException($"La impresora {Target} cerró la conexión al consultar su estado.");
                answers[i] = answer[0];
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new EscPosPrinterException($"La impresora {Target} no respondió al estado (DLE EOT) en {Options.StatusTimeout.TotalSeconds:0.#} s; su adaptador de red quizá no lo reenvía.");
            }
            catch (IOException ex) when (ex is not EscPosPrinterException)
            {
                throw new EscPosPrinterException($"Error consultando el estado de la impresora {Target}: {ex.Message}", ex);
            }
            catch (SocketException ex)
            {
                throw new EscPosPrinterException($"Error consultando el estado de la impresora {Target} ({ex.SocketErrorCode}).", ex);
            }
        }
        return EscPosPrinterStatus.FromBytes(answers[0], answers[1], answers[2]);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var target = Target;

        using var client = await ConnectAsync(cancellationToken).ConfigureAwait(false);

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
