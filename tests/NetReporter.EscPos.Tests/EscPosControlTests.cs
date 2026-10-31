using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos.Tests;

public sealed class EscPosControlTests
{
    private static RlRenderList Roll80(int pages = 1)
    {
        var page = PageSetup.Roll80 with { Size = new Size(PageSetup.Roll80.Size.Width, 30) };
        return new RlRenderList(page, Enumerable.Range(1, pages).Select(n => new RenderPage(n,
            [new DrawRectangleCommand(new Rect(page.Margins.Left, 5, 50, 20), Color.Black, null)])).ToList());
    }

    [Fact]
    public void OpenDrawer_IsEscP_WithPulseInTwoMsSteps()
    {
        Assert.Equal([0x1B, 0x70, 0x00, 25, 250], EscPosControl.OpenDrawer());
        Assert.Equal([0x1B, 0x70, 0x01, 50, 100], EscPosControl.OpenDrawer(1, 100, 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscPosControl.OpenDrawer(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => EscPosControl.OpenDrawer(0, 600));
    }

    [Fact]
    public void FeedAndCut_MatchTheRendererCommands()
    {
        Assert.Equal([0x1B, 0x64, 3], EscPosControl.Feed(3));
        Assert.Empty(EscPosControl.Feed(0));
        Assert.Equal([0x1D, 0x56, 0x01], EscPosControl.Cut(EscPosCut.Partial));
        Assert.Equal([0x1D, 0x56, 0x00], EscPosControl.Cut(EscPosCut.Full));
        Assert.Empty(EscPosControl.Cut(EscPosCut.None));
        Assert.Equal([0x1B, 0x40], EscPosControl.Initialize());
    }

    [Fact]
    public async Task OpenDrawerAsync_SendsOnlyThePulse()
    {
        var transport = new CapturingTransport();
        await transport.OpenDrawerAsync(pin: 1);
        Assert.Equal(EscPosControl.OpenDrawer(1), transport.Sent);
    }

    [Fact]
    public void Copies_RepeatEveryPageWithItsCut_AndKickTheDrawerOnce()
    {
        var list = Roll80(pages: 2);
        var one = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { OpenCashDrawer = true }).Render(list));
        var three = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { OpenCashDrawer = true, Copies = 3 }).Render(list));

        Assert.Equal(one.Height * 3, three.Height);
        Assert.Equal(one.Cuts.Count * 3, three.Cuts.Count);
        Assert.Equal(one.FeedLines.Count * 3, three.FeedLines.Count);
        Assert.Single(three.DrawerPins);
        // Cada copia es idéntica a la primera, fila a fila.
        for (var y = 0; y < one.Height; y++)
        {
            Assert.Equal(one.Rows[y], three.Rows[y + one.Height]);
            Assert.Equal(one.Rows[y], three.Rows[y + 2 * one.Height]);
        }
    }

    [Fact]
    public void Copies_SingleCopyBytes_AreUnchanged()
    {
        var list = Roll80();
        var plain = new EscPosRenderer().Render(list);
        var explicitOne = new EscPosRenderer(new EscPosRendererOptions { Copies = 1 }).Render(list);
        Assert.Equal(plain, explicitOne);
        var two = new EscPosRenderer(new EscPosRendererOptions { Copies = 2 }).Render(list);
        // ESC @ una vez, luego dos veces el ticket completo (raster + alimentación + corte).
        Assert.Equal(2 + 2 * (plain.Length - 2), two.Length);
    }

    [Fact]
    public void Copies_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { Copies = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { Copies = 11 }));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.9", true)]
    [InlineData("172.32.0.9", false)]
    [InlineData("192.168.99.81", true)]
    [InlineData(" 192.168.99.81 ", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::ffff:192.168.1.1", true)]
    [InlineData("printer.local", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PrivateAddressPolicy_AllowsOnlyPrivateIpLiterals(string? host, bool allowed)
    {
        Assert.Equal(allowed, EscPosPrinterPolicy.IsPrivateAddress(host, 9100));
        Assert.False(EscPosPrinterPolicy.IsPrivateAddress(host, 0));
        Assert.False(EscPosPrinterPolicy.IsPrivateAddress(host, 70000));
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
}
