namespace InvoiceSample.Domain;

public sealed record Empresa(string Nombre, string Rtn, string Direccion, string Telefono);

public sealed record Cliente(string Nombre, string Rtn, string Direccion);

public sealed record Factura(
    string Numero,
    DateOnly Fecha,
    string Cai,
    DateOnly RangoHasta,
    Empresa Emisor,
    Cliente Cliente,
    IReadOnlyList<LineaFactura> Lineas);

public sealed record LineaFactura(
    string Codigo,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario)
{
    public decimal Subtotal => Cantidad * PrecioUnitario;
}
