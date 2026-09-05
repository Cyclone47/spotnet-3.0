using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Spotnet.DAL;

/// <summary>
/// Validates Spotnet's user-editable filter mini-language and moves every literal into
/// a database parameter. It deliberately supports expressions, not arbitrary SQL.
/// </summary>
internal static class FilterExpressionCompiler
{
	private static readonly HashSet<string> AllowedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		// Boolean/comparison/query grammar used by the bundled advanced filters.
		"AND", "OR", "NOT", "LIKE", "MATCH", "IN", "SELECT", "FROM", "WHERE",
		"IS", "NULL", "BETWEEN", "ESCAPE",

		// Tables exposed to the filter language.
		"spots", "search",

		// Search/spots columns. Names are the only identifiers a filter may supply.
		"rowid", "key", "cat", "subcat", "extcat", "date", "filesize",
		"cats", "sender", "tag", "subject", "msgid", "modulus"
	};

	internal static ParameterizedSql Compile(string expression)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			return new ParameterizedSql(expression ?? string.Empty, Array.Empty<SqlValue>());
		}
		if (expression.IndexOf(';') >= 0 || expression.IndexOf("--", StringComparison.Ordinal) >= 0 ||
			expression.IndexOf("/*", StringComparison.Ordinal) >= 0 || expression.IndexOf("*/", StringComparison.Ordinal) >= 0)
		{
			throw new FormatException("Filter contains a statement separator or SQL comment.");
		}

		StringBuilder sql = new StringBuilder(expression.Length);
		List<SqlValue> values = new List<SqlValue>();
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
				long value = long.Parse(expression.Substring(start, index - start), CultureInfo.InvariantCulture);
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
				sql.Append(word);
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

			string op = ReadOperator(expression, ref index);
			if (op == null)
			{
				throw new FormatException("Filter contains unsupported syntax near: " + expression.Substring(index, Math.Min(12, expression.Length - index)));
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
		StringBuilder value = new StringBuilder();
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

	private static string ReadOperator(string expression, ref int index)
	{
		char current = expression[index];
		if (index + 1 < expression.Length)
		{
			string pair = expression.Substring(index, 2);
			if (pair == "!=" || pair == "<>" || pair == "<=" || pair == ">=")
			{
				index += 2;
				return pair;
			}
		}
		if (current == '=' || current == '<' || current == '>' || current == '+' ||
			current == '-' || current == '*' || current == '/' || current == '%')
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
		sql.Append(name);
	}
}

internal sealed class ParameterizedSql
{
	internal string CommandText { get; }

	internal IReadOnlyList<SqlValue> Values { get; }

	internal string CacheKey { get; }

	internal ParameterizedSql(string commandText, IEnumerable<SqlValue> values)
	{
		CommandText = commandText;
		Values = values.ToArray();
		CacheKey = commandText + "\u001f" + string.Join("\u001f", Values.Select((SqlValue value) => value.Name + "=" + Convert.ToString(value.Value, CultureInfo.InvariantCulture)));
	}
}

internal sealed class SqlValue
{
	internal string Name { get; }

	internal object Value { get; }

	internal SqlValue(string name, object value)
	{
		Name = name;
		Value = value;
	}
}
