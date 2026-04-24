namespace NetReporter.Designer.Services;

/// <summary>
/// Persiste templates como pares de archivos: <c>&lt;name&gt;.yaml</c> + <c>&lt;name&gt;.data.json</c>.
/// La carpeta por defecto es <c>~/.netreporter/templates</c> (configurable vía
/// <c>TemplateStore:Path</c> en appsettings).
/// </summary>
public sealed class TemplateStore
{
    private readonly string _root;

    public TemplateStore(IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        _root = cfg["TemplateStore:Path"] ?? GetDefaultPath();
        Directory.CreateDirectory(_root);
    }

    private static string GetDefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".netreporter", "templates");
    }

    public string Root => _root;

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_root)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_root, "*.yaml")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public (string Yaml, string Json) Load(string name)
    {
        var clean = SanitizeName(name);
        var yamlPath = ResolvePath(clean, ".yaml");
        var jsonPath = ResolvePath(clean, ".data.json");

        if (!File.Exists(yamlPath))
            throw new FileNotFoundException($"Template '{clean}' no existe.");

        var yaml = File.ReadAllText(yamlPath);
        var json = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : "{}";
        return (yaml, json);
    }

    public void Save(string name, string yaml, string json)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(json);
        var clean = SanitizeName(name);
        File.WriteAllText(ResolvePath(clean, ".yaml"), yaml);
        File.WriteAllText(ResolvePath(clean, ".data.json"), json);
    }

    public void Delete(string name)
    {
        var clean = SanitizeName(name);
        var yamlPath = ResolvePath(clean, ".yaml");
        var jsonPath = ResolvePath(clean, ".data.json");
        if (File.Exists(yamlPath)) File.Delete(yamlPath);
        if (File.Exists(jsonPath)) File.Delete(jsonPath);
    }

    // === Seguridad ===

    private string ResolvePath(string cleanName, string extension)
    {
        var candidate = Path.GetFullPath(Path.Combine(_root, cleanName + extension));
        var rootFull = Path.GetFullPath(_root);
        // Defensa en profundidad: aunque el nombre ya está sanitizado, validamos que el
        // path resuelto quede dentro de la carpeta root.
        if (!candidate.StartsWith(rootFull, StringComparison.Ordinal))
            throw new InvalidOperationException("Path fuera de la carpeta de templates.");
        return candidate;
    }

    private static string SanitizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
            throw new ArgumentException("El nombre no puede estar vacío.");
        if (trimmed.Length > 64)
            throw new ArgumentException("El nombre supera 64 caracteres.");

        foreach (var c in trimmed)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != ' ')
                throw new ArgumentException(
                    $"Carácter inválido '{c}' en nombre. Usa letras, dígitos, espacios, '-' o '_'.");
        }
        return trimmed;
    }
}
