using System.Net;
using System.Net.Sockets;

namespace NetReporter.EscPos.Tests;

/// <summary>Consulta de estado DLE EOT contra una impresora falsa que responde los bytes que se le indiquen.</summary>
public sealed class EscPosStatusTests
{
    /// <summary>Impresora falsa: por cada <c>DLE EOT n</c> responde el byte configurado (o nada, si <c>answers</c> es null).</summary>
    private static TcpListener FakePrinter(byte[]? answers)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var cmd = new byte[3];
            for (var i = 0; i < 3; i++)
            {
                var read = 0;
                while (read < 3)
                {
                    var n = await stream.ReadAsync(cmd.AsMemory(read, 3 - read));
                    if (n == 0) return;
                    read += n;
                }
                if (cmd[0] != 0x10 || cmd[1] != 0x04) return;
                if (answers is null) { await Task.Delay(5000); return; }
                await stream.WriteAsync(new[] { answers[i] });
            }
        });
        return listener;
    }

    private static EscPosTcpPrinter PrinterFor(TcpListener listener, TimeSpan? statusTimeout = null) =>
        new(new EscPosTcpPrinterOptions
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            StatusTimeout = statusTimeout ?? TimeSpan.FromSeconds(2),
        });

    [Fact]
    public async Task Ready_WhenOnlineCoverClosedAndPaper()
    {
        var listener = FakePrinter([0x16, 0x12, 0x12]);   // lo que respondió una impresora real en línea
        try
        {
            var status = await PrinterFor(listener).QueryStatusAsync();
            Assert.True(status.Online);
            Assert.False(status.CoverOpen);
            Assert.False(status.PaperEnd);
            Assert.False(status.PaperNearEnd);
            Assert.False(status.Error);
            Assert.True(status.DrawerPinHigh);
            Assert.True(status.ReadyToPrint);
            Assert.Equal("ready", status.Code);
        }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData(0x1E, 0x12, 0x12, "offline")]
    [InlineData(0x16, 0x16, 0x12, "cover-open")]
    [InlineData(0x16, 0x12, 0x72, "paper-end")]
    [InlineData(0x16, 0x52, 0x12, "error")]
    public async Task NotReady_ReportsTheCause(byte printer, byte offline, byte paper, string code)
    {
        var listener = FakePrinter([printer, offline, paper]);
        try
        {
            var status = await PrinterFor(listener).QueryStatusAsync();
            Assert.False(status.ReadyToPrint);
            Assert.Equal(code, status.Code);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void FromBytes_DecodesNearEnd()
    {
        var status = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x1E);
        Assert.True(status.PaperNearEnd);
        Assert.False(status.PaperEnd);
        Assert.True(status.ReadyToPrint);
    }

    [Fact]
    public async Task NoAnswer_BecomesPrinterException_AfterStatusTimeout()
    {
        var listener = FakePrinter(null);
        try
        {
            var printer = PrinterFor(listener, TimeSpan.FromMilliseconds(300));
            var ex = await Assert.ThrowsAsync<EscPosPrinterException>(() => printer.QueryStatusAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains("DLE EOT", ex.Message, StringComparison.Ordinal);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task CancelledToken_PropagatesAsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var printer = new EscPosTcpPrinter("127.0.0.1", 9100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => printer.QueryStatusAsync(cts.Token));
    }

    [Fact]
    public void StatusTimeout_IsValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EscPosTcpPrinter(new EscPosTcpPrinterOptions { Host = "printer", StatusTimeout = TimeSpan.Zero }));
    }
}
