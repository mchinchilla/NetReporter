using System.Reflection;

namespace NetReporter.Designer.Services;

/// <summary>
/// Sirve los templates de muestra embebidos como recursos del Designer (read-only).
/// Cada sample mapea a un par (yaml, json) por nombre lógico de recurso.
/// </summary>
public sealed class SampleStore
{
    private static readonly Assembly Asm = typeof(SampleStore).Assembly;

    private static readonly IReadOnlyDictionary<string, (string YamlResource, string JsonResource)> Catalog =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["clientes"]         = ("Samples.clientes.yaml",         "Samples.clientes.json"),
            ["invoice-laser"]    = ("Samples.invoice-laser.yaml",    "Samples.invoice.json"),
            ["invoice-paperoll"] = ("Samples.invoice-paperoll.yaml", "Samples.invoice.json")
        };

    public IReadOnlyList<string> List() => Catalog.Keys.ToArray();

    public (string Yaml, string Json) Load(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Catalog.TryGetValue(name, out var entry))
            throw new FileNotFoundException($"Sample '{name}' no existe.");

        return (ReadResource(entry.YamlResource), ReadResource(entry.JsonResource));
    }

    private static string ReadResource(string logicalName)
    {
        using var stream = Asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Recurso embebido '{logicalName}' no encontrado en {Asm.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
