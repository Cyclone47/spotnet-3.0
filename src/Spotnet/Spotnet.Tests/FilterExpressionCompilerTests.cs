using System;
using System.Collections.Generic;
using System.Xml;
using Spotnet.DAL;
using Spotnet.Properties;
using Xunit;

namespace Spotnet.Tests;

public class FilterExpressionCompilerTests
{
	[Fact]
	public void CompileParameterizesNumericAndStringLiterals()
	{
		ParameterizedSql result = FilterExpressionCompiler.Compile("cat = 5 AND subject LIKE '%x''y%'");

		Assert.Equal("cat = @filter0 AND subject LIKE @filter1", result.CommandText);
		Assert.Equal(2, result.Values.Count);
		Assert.Equal(5L, result.Values[0].Value);
		Assert.Equal("%x'y%", result.Values[1].Value);
	}

	[Theory]
	[InlineData("subject LIKE '%safe%'; DELETE FROM spots")]
	[InlineData("subject LIKE '%safe%' -- comment")]
	[InlineData("unknownColumn = 1")]
	[InlineData("cat = 1) OR (cat = 2")]
	public void CompileRejectsSyntaxOutsideTheFilterLanguage(string expression)
	{
		Assert.Throws<FormatException>(() => FilterExpressionCompiler.Compile(expression));
	}

	[Fact]
	public void EveryBundledAdvancedFilterIsAccepted()
	{
		foreach (string xml in new[] { Resources.FiltersAdvanced, Resources.FiltersAdvanced_en })
		{
			XmlDocument document = new XmlDocument { XmlResolver = null };
			document.LoadXml(xml);
			foreach (XmlElement filter in document.SelectNodes("//Filter"))
			{
				List<string> expressions = new List<string>();
				if (filter.HasAttribute("Query"))
				{
					expressions.Add(filter.GetAttribute("Query"));
				}
				if (!string.IsNullOrWhiteSpace(filter.InnerText))
				{
					expressions.Add(filter.InnerText.Trim());
				}
				foreach (string expression in expressions)
				{
					string resolved = expression.Replace("[SN:DATE]", "1700000000").Replace("[SN:NEW]", "42");
					Exception error = Record.Exception(() => FilterExpressionCompiler.Compile(resolved));
					Assert.True(error == null, expression + " failed: " + error?.Message);
				}
			}
		}
	}
}
