using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Spotnet.Mac.DAL;

/// <summary>
/// Validates Spotnet's user-editable filter mini-language and moves every literal into
/// a database parameter. Ported verbatim from the Windows client (Spotnet.DAL) so that
/// the bundled advanced filters compile to the same SQL on both platforms.
/// It deliberately supports expressions, not arbitrary SQL.
/// </summary>
public static class FilterExpressionCompiler
{
    private static readonly HashSet<string> AllowedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Boolean/comparison/query grammar used by the bundled advanced filters.
        "AND", "OR", "NOT", "LIKE", "MATCH", "IN", "SELECT", "FROM", "WHERE",
        "IS", "NULL", "BETWEEN", "ESCAPE",

        // Tables exposed to the filter language.
        "spots", "search",

        // Search/spots columns. Names are the only identifiers a filter may supply.
        // "docid" is the FTS4 spelling a handful of bundled filters still use; it is
        // rewritten to "rowid" below, because the search table is FTS5 here.
        "rowid", "docid", "key", "cat", "subcat", "extcat", "date", "filesize",
        "cats", "sender", "tag", "subject", "msgid", "modulus"
    };

    public static ParameterizedSql Compile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new ParameterizedSql(expression ?? string.Empty, Array.Empty<SqlValue>());
        }
        if (expression.Contains(';', StringComparison.Ordinal) || expression.Contains("--", StringComparison.Ordinal) ||
            expression.Contains("/*", StringComparison.Ordinal) || expression.Contains("*/", StringComparison.Ordinal))
        {
            throw new FormatException("Filter contains a statement separator or SQL comment.");
        }

        var sql = new StringBuilder(expression.Length);
        var values = new List<SqlValue>();
        int parentheses = 0;
        for (int index = 0; index < expression.Length;)
        {
            char current = expression[index];
            if (char.IsWhiteSpace(current))
            {
                sql.Append(current);
                index++;
                continue;
            }
            if (current == '\'')
            {
                string value = ReadStringLiteral(expression, ref index);
                AppendParameter(sql, values, value);
                continue;
            }
            if (char.IsDigit(current))
            {
                int start = index;
                while (index < expression.Length && char.IsDigit(expression[index]))
                {
                    index++;
                }
                long value = long.Parse(expression.AsSpan(start, index - start), CultureInfo.InvariantCulture);
                AppendParameter(sql, values, value);
                continue;
            }
            if (char.IsLetter(current) || current == '_')
            {
                int start = index;
                while (index < expression.Length && (char.IsLetterOrDigit(expression[index]) || expression[index] == '_'))
                {
                    index++;
                }
                string word = expression.Substring(start, index - start);
                if (!AllowedWords.Contains(word))
                {
                    throw new FormatException("Filter contains unsupported identifier or keyword: " + word);
                }
                AppendToken(sql, word.Equals("docid", StringComparison.OrdinalIgnoreCase) ? "rowid" : word);
                continue;
            }

            if (current == '(')
            {
                parentheses++;
                sql.Append(current);
                index++;
                continue;
            }
            if (current == ')')
            {
                if (--parentheses < 0)
                {
                    throw new FormatException("Filter contains an unmatched closing parenthesis.");
                }
                sql.Append(current);
                index++;
                continue;
            }
            if (current == ',')
            {
                sql.Append(current);
                index++;
                continue;
            }

            string? op = ReadOperator(expression, ref index);
            if (op == null)
            {
                throw new FormatException(string.Concat("Filter contains unsupported syntax near: ", expression.AsSpan(index, Math.Min(12, expression.Length - index))));
            }
            sql.Append(op);
        }

        if (parentheses != 0)
        {
            throw new FormatException("Filter contains unmatched parentheses.");
        }
        return new ParameterizedSql(sql.ToString(), values);
    }

    private static string ReadStringLiteral(string expression, ref int index)
    {
        var value = new StringBuilder();
        index++;
        while (index < expression.Length)
        {
            char current = expression[index++];
            if (current != '\'')
            {
                value.Append(current);
                continue;
            }
            if (index < expression.Length && expression[index] == '\'')
            {
                value.Append('\'');
                index++;
                continue;
            }
            return value.ToString();
        }
        throw new FormatException("Filter contains an unterminated string literal.");
    }

    private static string? ReadOperator(string expression, ref int index)
    {
        char current = expression[index];
        if (index + 1 < expression.Length)
        {
            string pair = expression.Substring(index, 2);
            if (pair is "!=" or "<>" or "<=" or ">=")
            {
                index += 2;
                return pair;
            }
        }
        if (current is '=' or '<' or '>' or '+' or '-' or '*' or '/' or '%')
        {
            index++;
            return current.ToString();
        }
        return null;
    }

    private static void AppendParameter(StringBuilder sql, ICollection<SqlValue> values, object value)
    {
        string name = "@filter" + values.Count.ToString(CultureInfo.InvariantCulture);
        values.Add(new SqlValue(name, value));
        AppendToken(sql, name);
    }

    /// <summary>
    /// Appends an identifier or parameter, inserting a space when the previous token
    /// would otherwise run into it. Some bundled filters omit the space around a
    /// keyword ("cats MATCH '5c2'AND tag LIKE ..."), which without this would compile
    /// to "@filter0AND" and fail to parse.
    /// </summary>
    private static void AppendToken(StringBuilder sql, string token)
    {
        if (sql.Length > 0)
        {
            char previous = sql[^1];
            if (char.IsLetterOrDigit(previous) || previous == '_')
            {
                sql.Append(' ');
            }
        }
        sql.Append(token);
    }
}

public sealed class ParameterizedSql
{
    public string CommandText { get; }

    public IReadOnlyList<SqlValue> Values { get; }

    public ParameterizedSql(string commandText, IEnumerable<SqlValue> values)
    {
        CommandText = commandText;
        Values = values.ToArray();
    }
}

public sealed class SqlValue
{
    public string Name { get; }

    public object Value { get; }

    public SqlValue(string name, object value)
    {
        Name = name;
        Value = value;
    }
}
