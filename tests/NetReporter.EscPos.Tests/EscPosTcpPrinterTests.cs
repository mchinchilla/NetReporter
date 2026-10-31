using System.Net;
using System.Net.Sockets;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos.Tests;

public sealed class EscPosTcpPrinterTests
{
    private static async Task<(TcpListener Listener, Task<byte[]> Received)> FakePrinter()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ms = new MemoryStream();
            await client.GetStream().CopyToAsync(ms);
            return ms.ToArray();
        });
        await Task.Yield();
        return (listener, received);
    }

    [Fact]
    public async Task SendAsync_DeliversEveryByte_AndClosesCleanly()
    {
        var (listener, received) = await FakePrinter();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var data = new byte[200_000];
            Random.Shared.NextBytes(data);

            var printer = new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = "127.0.0.1", Port = port });
            await printer.SendAsync(data);

            var got = await received.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(data, got);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task ConnectionRefused_BecomesPrinterException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // nadie escucha en ese puerto

        var printer = new EscPosTcpPrinter("127.0.0.1", port);
        var ex = await Assert.ThrowsAsync<EscPosPrinterException>(() => printer.SendAsync(new byte[] { 0x1B, 0x40 }));
        Assert.Contains($"127.0.0.1:{port}", ex.Message, StringComparison.Ordinal);
        Assert.IsType<SocketException>(ex.InnerException);
    }

    [Fact]
    public async Task CancelledToken_PropagatesAsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var printer = new EscPosTcpPrinter("127.0.0.1", 9100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => printer.SendAsync(new byte[1], cts.Token));
    }

    [Fact]
    public async Task ConnectTimeout_BecomesPrinterException()
    {
        // 10.255.255.1 no enruta: el intento termina por timeout (o por error de red inmediato); nunca cuelga.
        var printer = new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = "10.255.255.1", Port = 9100, ConnectTimeout = TimeSpan.FromMilliseconds(300) });
        var ex = await Assert.ThrowsAsync<EscPosPrinterException>(() => printer.SendAsync(new byte[1]).WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Contains("10.255.255.1:9100", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_AreValidated()
    {
        Assert.Throws<ArgumentException>(() => new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = "printer", Port = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = "printer", ConnectTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = "printer", SendTimeout = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentNullException>(() => new EscPosTcpPrinter(null!));
    }

    private sealed class CapturingTransport : IEscPosTransport
    {
        public byte[]? Sent;
        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Sent = data.ToArray();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PrintAsync_RendersThenSends()
    {
        var page = PageSetup.Roll80 with { Size = new Size(PageSetup.Roll80.Size.Width, 20) };
        var list = new RlRenderList(page, [new RenderPage(1, [])]);
        var transport = new CapturingTransport();

        await transport.PrintAsync(list);

        Assert.NotNull(transport.Sent);
        Assert.Equal([0x1B, 0x40], transport.Sent![..2]);
        Assert.Equal(new EscPosRenderer().Render(list), transport.Sent);
    }
}
