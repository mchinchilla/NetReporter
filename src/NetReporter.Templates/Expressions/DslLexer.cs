using System.Globalization;
using System.Text;

namespace NetReporter.Templates.Expressions;

/// <summary>Error de sintaxis o de compilación de una expresión DSL. Incluye la posición (0-based).</summary>
public sealed class DslSyntaxException : FormatException
{
    public int Position { get; }
    public string Expression { get; }

    public DslSyntaxException(string message, string source, int position)
        : base($"{message} (posición {position} en '{source}')")
    {
        Expression = source;
        Position = position;
    }
}

internal enum TokenKind
{
    Number,
    String,
    Path,        // $.a.b[0] / $$.a / $.items[*].total
    Identifier,  // pageNumber, if, upper, true, false, null, and/or/not
    Hash,        // #group, #count
    Operator,    // + - * / % == != < <= > >= && || ! ?? ? :
    LParen,
    RParen,
    Comma,
    End
}

internal readonly record struct Token(TokenKind Kind, string Text, int Position, decimal Number = 0);

internal static class DslLexer
{
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            var start = i;

            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                var text = source[start..i];
                if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
                    throw new DslSyntaxException($"Número inválido '{text}'", source, start);
                tokens.Add(new Token(TokenKind.Number, text, start, n));
                continue;
            }

            if (c == '\'' || c == '"')
            {
                tokens.Add(new Token(TokenKind.String, ReadString(source, ref i), start));
                continue;
            }

            if (c == '$')
            {
                i++;
                if (i < source.Length && source[i] == '$') i++;
                while (i < source.Length)
                {
                    var p = source[i];
                    if (p == '.')
                    {
                        i++;
                        while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '-')) i++;
                    }
                    else if (p == '[')
                    {
                        var close = source.IndexOf(']', i);
                        if (close < 0) throw new DslSyntaxException("Path sin cerrar ']'", source, i);
                        i = close + 1;
                    }
                    else break;
                }
                tokens.Add(new Token(TokenKind.Path, source[start..i], start));
                continue;
            }

            if (c == '#')
            {
                i++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                if (i == start + 1) throw new DslSyntaxException("Se esperaba un nombre después de '#'", source, start);
                tokens.Add(new Token(TokenKind.Hash, source[start..i], start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Identifier, source[start..i], start));
                continue;
            }

            switch (c)
            {
                case '(': tokens.Add(new Token(TokenKind.LParen, "(", start)); i++; continue;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")", start)); i++; continue;
                case ',': tokens.Add(new Token(TokenKind.Comma, ",", start)); i++; continue;
            }

            var two = i + 1 < source.Length ? source.Substring(i, 2) : null;
            if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "??")
            {
                tokens.Add(new Token(TokenKind.Operator, two, start));
                i += 2;
                continue;
            }

            if ("+-*/%<>!?:".IndexOf(c) >= 0)
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString(), start));
                i++;
                continue;
            }

            if (c == '=')
                throw new DslSyntaxException("Operador '=' inválido; para comparar usa '=='", source, start);

            throw new DslSyntaxException($"Carácter inesperado '{c}'", source, start);
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, source.Length));
        return tokens;
    }

    private static string ReadString(string source, ref int i)
    {
        var quote = source[i];
        var start = i;
        i++;
        var sb = new StringBuilder();
        while (i < source.Length && source[i] != quote)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                i++;
                sb.Append(source[i] switch { 'n' => '\n', 't' => '\t', var other => other });
            }
            else
            {
                sb.Append(source[i]);
            }
            i++;
        }
        if (i >= source.Length) throw new DslSyntaxException("String sin cerrar", source, start);
        i++; // comilla de cierre
        return sb.ToString();
    }
}
