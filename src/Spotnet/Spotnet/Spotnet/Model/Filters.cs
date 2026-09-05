using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model.Newznab;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Model;

internal class Filters
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public readonly FilterViewModel FiltersRoot;

	private static readonly List<string> DefaultFilterNames = new List<string> { "Aangepast", "Geavanceerd NL", "Advanced EN", "Eenvoudig NL", "Simple EN" };

	public bool DoNotResizeFilterImages;

	private static readonly object LockExpandedFile = new object();

	/// <summary>The FTS4 row identifier, as it appears in filters written before FTS5.</summary>
	private static readonly Regex LegacyDocId = new Regex(@"\bdocid\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static string FilterFolder
	{
		get
		{
			string text = Settings.Default.Filter;
			if (text.IsNullOrEmpty())
			{
				text = DefaultFilterNames[0];
				MainWindowVm.FilterSelectedName = text;
			}
			string text2 = System.IO.Path.Combine(AppHelper.FiltersFolder, text);
			if (!AppHelper.EnsureDirectoryExist(text2))
			{
				throw new Exception("Path cannot be created: " + text2);
			}
			return text2;
		}
	}

	private static string FiltersFilePath => System.IO.Path.Combine(FilterFolder, "filters.xml");

	private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

	internal FilterViewModel NewznabFilterRoot => FiltersRoot.Children.FirstOrDefault((FilterViewModel f) => f.Query.ToLower().Equals("newznab"));

	public event Action FiltersLoaded;

	internal Filters()
	{
		FiltersRoot = new FilterViewModel("ROOT", "");
	}

	internal static bool InitializeDefaultFilters()
	{
		if (!AppHelper.EnsureDirectoryExist(AppHelper.FiltersFolder))
		{
			AppHelper.Error(Words.FiltersCannotCreateDir + ": " + AppHelper.FiltersFolder);
			return false;
		}
		List<string> list = new List<string>();
		foreach (string defaultFilterName in DefaultFilterNames)
		{
			string text = System.IO.Path.Combine(AppHelper.FiltersFolder, defaultFilterName);
			list.Add(text);
			if (!AppHelper.EnsureDirectoryExist(text))
			{
				AppHelper.Error(Words.FiltersCannotCreateDir + ": " + text);
				return false;
			}
		}
		CultureInfo culture = UserLanguageHelper.Culture;
		try
		{
			string text2 = System.IO.Path.Combine(list[1], "filters.xml");
			if (!System.IO.File.Exists(text2))
			{
				Log.Debug("Restore filter " + text2);
				string filtersAdvanced = Resources.FiltersAdvanced;
				System.IO.File.WriteAllText(text2, filtersAdvanced);
			}
			text2 = System.IO.Path.Combine(list[2], "filters.xml");
			if (!System.IO.File.Exists(text2))
			{
				Log.Debug("Restore filter " + text2);
				string filtersAdvanced = Resources.FiltersAdvanced_en;
				System.IO.File.WriteAllText(text2, filtersAdvanced);
			}
			text2 = System.IO.Path.Combine(list[3], "filters.xml");
			if (!System.IO.File.Exists(text2))
			{
				Log.Debug("Restore filter " + text2);
				UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("nl");
				if (!AddFilters(GetSimpleFilters(), new FilterViewModel("ROOT", ""), bClean: true, saveToTheFile: true, text2))
				{
					return false;
				}
			}
			text2 = System.IO.Path.Combine(list[4], "filters.xml");
			if (!System.IO.File.Exists(text2))
			{
				Log.Debug("Restore filter " + text2);
				UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("en");
				if (!AddFilters(GetSimpleFilters(), new FilterViewModel("ROOT", ""), bClean: true, saveToTheFile: true, text2))
				{
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
		finally
		{
			UserLanguageHelper.Culture = culture;
		}
		return true;
	}

	internal FilterViewModel GetFilter(string id)
	{
		return GetFilter(id, FiltersRoot);
	}

	private static FilterViewModel GetFilter(string id, FilterViewModel root)
	{
		if (id.IsNullOrWhiteSpace() || root == null)
		{
			return null;
		}
		if (root.Children == null || !root.Children.Any())
		{
			return null;
		}
		FilterViewModel filterViewModel = root.Children.FirstOrDefault((FilterViewModel f) => f.Id != null && f.Id.Equals(id));
		if (filterViewModel != null)
		{
			return filterViewModel;
		}
		if (!root.Children.Any())
		{
			return null;
		}
		return root.Children.Select((FilterViewModel c) => GetFilter(id, c)).FirstOrDefault((FilterViewModel f) => f != null);
	}

	internal FilterViewModel GetFilterByName(string nameWithPath)
	{
		if (nameWithPath.IsNullOrWhiteSpace())
		{
			return null;
		}
		string[] array = nameWithPath.Split(new string[1] { "/" }, StringSplitOptions.RemoveEmptyEntries);
		FilterViewModel filterViewModel = FiltersRoot;
		string[] array2 = array;
		foreach (string name in array2)
		{
			filterViewModel = filterViewModel.Children.FirstOrDefault((FilterViewModel f) => f.Name.EqualsIgnoreCase(name));
			if (filterViewModel == null)
			{
				return null;
			}
		}
		return filterViewModel;
	}

	public bool AddFilter(string nameWithPath, string query, string image)
	{
		if (nameWithPath.IsNullOrWhiteSpace())
		{
			return false;
		}
		if (GetUnchangableFilterNamesList().Contains(Settings.Default.Filter))
		{
			SaveAs(DefaultFilterNames[0], force: true);
		}
		string[] array = nameWithPath.Split(new string[1] { "/" }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 1)
		{
			return AddFilters(new List<FilterViewModel>
			{
				new FilterViewModel(nameWithPath, query, image)
			}, FiltersRoot, bClean: false);
		}
		IEnumerable<string> values = array.Take(array.Length - 1);
		string nameWithPath2 = string.Join("/", values);
		FilterViewModel filterByName = GetFilterByName(nameWithPath2);
		if (filterByName == null)
		{
			AddFilter(nameWithPath2, "cat!=0", null);
			filterByName = GetFilterByName(nameWithPath2);
		}
		string name = array.Last();
		return AddFilters(new List<FilterViewModel>
		{
			new FilterViewModel(name, query, image)
		}, filterByName, bClean: false);
	}

	private static bool AddFilters(IEnumerable<FilterViewModel> filters, FilterViewModel rootNode, bool bClean, bool saveToTheFile = true, string filtersFilePath = null)
	{
		try
		{
			GetXmlDocument(out var xmlDocument, out var xmlElement, rootNode, bClean);
			List<FilterViewModel> list = new List<FilterViewModel>();
			foreach (FilterViewModel filter in filters)
			{
				if (GetFilter(filter.Id, rootNode) == null)
				{
					xmlElement.AppendChild(filter.CreateXmlElement(xmlDocument));
					list.Add(filter);
				}
			}
			if (saveToTheFile)
			{
				if (filtersFilePath == null)
				{
					filtersFilePath = FiltersFilePath;
				}
				if (System.IO.File.Exists(filtersFilePath))
				{
					System.IO.File.SetAttributes(filtersFilePath, FileAttributes.Normal);
				}
				xmlDocument.Save(filtersFilePath);
			}
			rootNode.ChildrenAddRange(list);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
		return true;
	}

	private static bool GetXmlDocument(out XmlDocument xmlDocument, out XmlElement xmlElement, FilterViewModel rootNode, bool cleanXml)
	{
		xmlDocument = new XmlDocument
		{
			XmlResolver = null
		};
		if (System.IO.File.Exists(FiltersFilePath) && !cleanXml)
		{
			xmlDocument.Load(FiltersFilePath);
			xmlElement = GetXmlElement(xmlDocument.DocumentElement, rootNode);
			if (xmlElement == null || xmlElement.ParentNode == null)
			{
				Log.Error("Failed to load path to {0}", rootNode.FullPathString);
				return false;
			}
		}
		else
		{
			XmlElement newChild = xmlDocument.CreateElement("Spotnet");
			xmlDocument.AppendChild(newChild);
			xmlElement = GetXmlElement(xmlDocument.DocumentElement, rootNode);
			rootNode.ChildrenClear();
		}
		return true;
	}

	internal bool UpdateFilterQuery(FilterViewModel filter, string query)
	{
		try
		{
			if (GetUnchangableFilterNamesList().Contains(Settings.Default.Filter))
			{
				SaveAs(DefaultFilterNames[0], force: true);
			}
			GetXmlDocument(out var xmlDocument, out var _, filter.Parent, cleanXml: false);
			GetXmlElement(xmlDocument.DocumentElement, filter).SetAttribute("Query", query);
			filter.Query = query;
			if (System.IO.File.Exists(FiltersFilePath))
			{
				System.IO.File.SetAttributes(FiltersFilePath, FileAttributes.Normal);
			}
			xmlDocument.Save(FiltersFilePath);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
		return true;
	}

	internal static string SimplifyQuery(string query)
	{
		if (query.IsNullOrEmpty())
		{
			return query;
		}
		string a = query.Replace(" ", "").Replace("(", "").Replace(")", "")
			.ToLower();
		if (string.Equals(a, "searchmatch'cats:1'") || string.Equals(a, "catsmatch'1'"))
		{
			return "cat = 1";
		}
		if (string.Equals(a, "searchmatch'cats:2'") || string.Equals(a, "catsmatch'2'"))
		{
			return "cat = 2";
		}
		if (string.Equals(a, "searchmatch'cats:3'") || string.Equals(a, "catsmatch'3'"))
		{
			return "cat = 3";
		}
		if (string.Equals(a, "searchmatch'cats:4'") || string.Equals(a, "catsmatch'4'"))
		{
			return "cat = 4";
		}
		if (string.Equals(a, "searchmatch'cats:5'") || string.Equals(a, "catsmatch'5'"))
		{
			return "cat = 5";
		}
		if (string.Equals(a, "searchmatch'cats:6'") || string.Equals(a, "catsmatch'6'"))
		{
			return "cat = 6";
		}
		if (string.Equals(a, "searchmatch'cats:9'") || string.Equals(a, "catsmatch'9'"))
		{
			return "cat = 9";
		}
		return query;
	}

	public bool ResetFiltersToSimple(bool saveToTheDisk)
	{
		try
		{
			List<FilterViewModel> simpleFilters = GetSimpleFilters();
			if (UserLanguageHelper.Language == "en")
			{
				MainWindowVm.FilterSelectedName = DefaultFilterNames[4];
			}
			else
			{
				MainWindowVm.FilterSelectedName = DefaultFilterNames[3];
			}
			if (saveToTheDisk && GetUnchangableFilterNamesList().Contains(Settings.Default.Filter))
			{
				SaveAs(DefaultFilterNames[0], force: true);
			}
			return AddFilters(simpleFilters, FiltersRoot, bClean: true, saveToTheDisk);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
	}

	private static List<FilterViewModel> GetSimpleFilters()
	{
		return new List<FilterViewModel>
		{
			new FilterViewModel(Words.New, "rowid > [SN:NEW]", "\\Images\\new2.ico"),
			new FilterViewModel(Words.Last24Hours, "date > ( [SN:DATE] - 86400 )", "\\Images\\today.ico"),
			new FilterViewModel(AppHelper.CatDesc(1, 0), "cat = 1", "\\Images\\video2.ico"),
			new FilterViewModel(AppHelper.CatDesc(6, 0), "cat = 6", "\\Images\\series2.ico"),
			new FilterViewModel(AppHelper.CatDesc(5, 0), "cat = 5", "\\Images\\books2.ico"),
			new FilterViewModel(AppHelper.CatDesc(2, 0), "cat = 2", "\\Images\\audio2.ico"),
			new FilterViewModel(AppHelper.CatDesc(3, 0), "cat = 3", "\\Images\\games2.ico"),
			new FilterViewModel(AppHelper.CatDesc(4, 0), "cat = 4", "\\Images\\applications2.ico"),
			new FilterViewModel(AppHelper.CatDesc(9, 0), "cat = 9", "\\Images\\x2.ico")
		};
	}

	public bool ResetFilters()
	{
		try
		{
			string contents = ((UserLanguageHelper.Language == "en") ? Resources.FiltersAdvanced_en : Resources.FiltersAdvanced);
			MainWindowVm.FilterSelectedName = ((UserLanguageHelper.Language == "en") ? DefaultFilterNames[2] : DefaultFilterNames[1]);
			System.IO.File.WriteAllText(FiltersFilePath, contents);
			LoadFilters();
			FiltersExpandedStateSaveAsync();
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
	}

	public bool LoadFilters()
	{
		try
		{
			if (!System.IO.File.Exists(FiltersFilePath) && !ResetFilters())
			{
				return false;
			}
			XmlDocument xmlDocument = new XmlDocument
			{
				XmlResolver = null
			};
			xmlDocument.Load(FiltersFilePath);
			XmlElement documentElement = xmlDocument.DocumentElement;
			if (documentElement == null || documentElement.ChildNodes.Count == 0)
			{
				if (!ResetFilters())
				{
					return false;
				}
				xmlDocument.Load(FiltersFilePath);
				documentElement = xmlDocument.DocumentElement;
			}
			if (documentElement == null)
			{
				throw new Exception("documentElement is null");
			}
			string attribute = documentElement.GetAttribute("ImageResize");
			DoNotResizeFilterImages = !attribute.IsNullOrEmpty() && attribute.ToLower().Equals("false");
			string attribute2 = documentElement.GetAttribute("Background");
			if (!attribute2.IsNullOrEmpty())
			{
				MainWindowVm.SetFiltersBackground(attribute2);
			}
			else
			{
				MainWindowVm.SetFiltersBackground(null);
			}
			LoadFiltersTo(FiltersRoot, documentElement);
			FiltersExpandedStateRestore();
			LoadPersistentFilters();
			this.FiltersLoaded?.Invoke();
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return ResetFiltersToSimple(saveToTheDisk: false);
		}
	}

	private void LoadPersistentFilters()
	{
		if (Settings.Default.ShowFavorites && !FiltersRoot.Children.Any((FilterViewModel f) => Favorites.IsFavoritesQuery(f.Query)))
		{
			FiltersRoot.ChildInsert(0, new FilterViewModel("", "favorites"));
		}
		FilterViewModel newznabFilterRoot = NewznabFilterRoot;
		if (newznabFilterRoot == null || NewznabFilterRoot.Children.Any())
		{
			return;
		}
		foreach (KeyValuePair<int, string> category in NewznabHelper.Categories)
		{
			AddFilter(newznabFilterRoot.FullPathString + "/" + category.Value, "cat=" + category.Key, "");
		}
	}

	private static void LoadFiltersTo(FilterViewModel filtersRoot, XmlElement root)
	{
		filtersRoot.ChildrenClear();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (XmlNode item in root)
		{
			if (!(item is XmlElement xmlElement) || !xmlElement.Name.Equals("Filter"))
			{
				continue;
			}
			string attribute = xmlElement.GetAttribute("Name");
			if (!hashSet.Add(attribute) || xmlElement.GetAttribute("Visible").EqualsIgnoreCase("false"))
			{
				continue;
			}
			string image = (xmlElement.GetAttribute("Image").IsNullOrEmpty() ? "" : xmlElement.GetAttribute("Image"));
			string imageSelected = (xmlElement.GetAttribute("SelectedImage").IsNullOrEmpty() ? "" : xmlElement.GetAttribute("SelectedImage"));
			FilterViewModel filterViewModel = new FilterViewModel(attribute, "", image, imageSelected);
			filtersRoot.ChildAdd(filterViewModel);
			if (xmlElement.ChildNodes.Count > 0 && xmlElement.FirstChild is XmlElement)
			{
				LoadFiltersTo(filterViewModel, xmlElement);
			}
			else
			{
				filterViewModel.Query = xmlElement.InnerText.Trim();
			}
			string attribute2 = xmlElement.GetAttribute("Query");
			if (!attribute2.IsNullOrWhiteSpace())
			{
				filterViewModel.Query = attribute2.Trim();
			}
			if (filterViewModel.Query.IsNullOrEmpty())
			{
				continue;
			}
			filterViewModel.Query = RewriteLegacyDocId(filterViewModel.Query);
			string text = filterViewModel.Query.ToLower();
			filterViewModel.Query = filterViewModel.Query.Replace("cat = 1 AND cats MATCH '1b4 OR 1d11'", "cat = 6");
			if (!text.Contains("scat =") && !text.Contains("topcat =") && !text.Contains("subcat in") && !text.Contains("subcat =") && !text.Contains("subcats like") && !text.Contains("subcats like"))
			{
				if (text.Contains("tag = '"))
				{
					filterViewModel.Query = filterViewModel.Query.Replace("tag = '", "tag MATCH '");
				}
				if (text.Contains("sender = '"))
				{
					filterViewModel.Query = filterViewModel.Query.Replace("sender = '", "sender MATCH '");
				}
			}
		}
	}

	/// <summary>
	/// Replaces FTS4's `docid` with FTS5's `rowid` in a stored filter query.
	/// </summary>
	/// <remarks>
	/// Every filter that narrows a category by subject or tag was written as
	/// `docid IN (SELECT docid FROM search ...)` - both the filters shipped with the app
	/// and whatever the user has saved since. FTS5 has no `docid`, and the filter
	/// compiler no longer accepts the name, so such a filter would fail outright.
	/// Rewriting on load rather than migrating filters.xml in place keeps the user's own
	/// file untouched, and repairs a hand-edited or restored one just the same.
	/// </remarks>
	private static string RewriteLegacyDocId(string query)
	{
		if (query.IsNullOrEmpty())
		{
			return query;
		}
		return LegacyDocId.Replace(query, "rowid");
	}

	public void RemoveFilter(string filterId)
	{
		try
		{
			FilterViewModel filter = GetFilter(filterId);
			XmlDocument xmlDocument = new XmlDocument
			{
				XmlResolver = null
			};
			if (filter == null || !System.IO.File.Exists(FiltersFilePath))
			{
				return;
			}
			if (GetUnchangableFilterNamesList().Contains(Settings.Default.Filter))
			{
				SaveAs(DefaultFilterNames[0], force: true);
			}
			xmlDocument.Load(FiltersFilePath);
			XmlElement xmlElement = GetXmlElement(xmlDocument.DocumentElement, filter);
			if (xmlElement != null && xmlElement.ParentNode != null)
			{
				xmlElement.ParentNode.RemoveChild(xmlElement);
				if (System.IO.File.Exists(FiltersFilePath))
				{
					System.IO.File.SetAttributes(FiltersFilePath, FileAttributes.Normal);
				}
				xmlDocument.Save(FiltersFilePath);
				filter.Parent.Children.Remove(filter);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	private static XmlElement GetXmlElement(XmlElement root, FilterViewModel filter)
	{
		XmlElement xmlElement = root;
		for (int num = filter.NestingLevel - 1; num >= 0; num--)
		{
			string str = filter.FullPath[num];
			bool flag = false;
			foreach (object item in xmlElement)
			{
				if (item is XmlElement xmlElement2 && xmlElement2.GetAttribute("Name").EqualsIgnoreCase(str))
				{
					xmlElement = xmlElement2;
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return null;
			}
		}
		return xmlElement;
	}

	public void SwapFilter(string filterId, bool bUp)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument
			{
				XmlResolver = null
			};
			if (GetFilter(filterId) == null || !System.IO.File.Exists(FiltersFilePath))
			{
				return;
			}
			if (GetUnchangableFilterNamesList().Contains(Settings.Default.Filter))
			{
				SaveAs(DefaultFilterNames[0], force: true);
			}
			xmlDocument.Load(FiltersFilePath);
			FilterViewModel filter = GetFilter(filterId);
			XmlElement xmlElement = GetXmlElement(xmlDocument.DocumentElement, filter);
			if (xmlElement == null || xmlElement.ParentNode == null)
			{
				return;
			}
			XmlNode parentNode = xmlElement.ParentNode;
			if (bUp)
			{
				XmlNode previousSibling = xmlElement.PreviousSibling;
				if (previousSibling == null)
				{
					return;
				}
				parentNode.RemoveChild(previousSibling);
				parentNode.InsertAfter(previousSibling, xmlElement);
				int num = filter.Parent.Children.IndexOf(filter);
				FilterViewModel child = filter.Parent.Children[num - 1];
				filter.Parent.ChildRemove(child);
				filter.Parent.ChildInsert(num, child);
			}
			else
			{
				XmlNode nextSibling = xmlElement.NextSibling;
				if (nextSibling == null)
				{
					return;
				}
				parentNode.RemoveChild(xmlElement);
				parentNode.InsertAfter(xmlElement, nextSibling);
				int num2 = filter.Parent.Children.IndexOf(filter);
				FilterViewModel child2 = filter.Parent.Children[num2 + 1];
				filter.Parent.ChildRemove(child2);
				filter.Parent.ChildInsert(num2, child2);
			}
			if (System.IO.File.Exists(FiltersFilePath))
			{
				System.IO.File.SetAttributes(FiltersFilePath, FileAttributes.Normal);
			}
			xmlDocument.Save(FiltersFilePath);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	internal void FiltersExpandedStateSaveAsync()
	{
		Task.Run(delegate
		{
			string path = System.IO.Path.Combine(AppHelper.SettingsFolder, "filters.expanded.txt");
			lock (LockExpandedFile)
			{
				try
				{
					System.IO.File.WriteAllLines(path, GetAllFamilyFullPathList(FiltersRoot, (FilterViewModel f) => f.IsExpanded), AppHelper.AnsiEnc());
				}
				catch (Exception ex)
				{
					Log.Debug(ex.Message);
				}
			}
		});
	}

	internal void FiltersExpandedStateRestore()
	{
		string path = System.IO.Path.Combine(AppHelper.SettingsFolder, "filters.expanded.txt");
		string[] namesToExpand = new string[0];
		lock (LockExpandedFile)
		{
			try
			{
				if (System.IO.File.Exists(path))
				{
					namesToExpand = System.IO.File.ReadAllLines(path, AppHelper.AnsiEnc());
				}
			}
			catch (Exception ex)
			{
				Log.Debug(ex.Message);
			}
		}
		ExpandAddFilters(FiltersRoot, namesToExpand);
	}

	private void ExpandAddFilters(FilterViewModel root, ICollection<string> namesToExpand)
	{
		if (!namesToExpand.Contains(root.FullPathString))
		{
			return;
		}
		root.IsExpanded = true;
		foreach (FilterViewModel child in root.Children)
		{
			ExpandAddFilters(child, namesToExpand);
		}
	}

	private IEnumerable<string> GetAllFamilyFullPathList(FilterViewModel root, Func<FilterViewModel, bool> filter)
	{
		List<string> list = new List<string> { root.ToString() };
		foreach (FilterViewModel child in root.Children)
		{
			if (filter(child))
			{
				list.AddRange(GetAllFamilyFullPathList(child, filter));
			}
		}
		return list;
	}

	public static IEnumerable<string> GetUnchangableFilterNamesList()
	{
		return new List<string>
		{
			DefaultFilterNames[1],
			DefaultFilterNames[2],
			DefaultFilterNames[3],
			DefaultFilterNames[4]
		};
	}

	public static IEnumerable<string> GetChangableFilterNamesList()
	{
		return from f in (from d in System.IO.Directory.EnumerateDirectories(AppHelper.FiltersFolder)
				where System.IO.File.Exists(System.IO.Path.Combine(d, "filters.xml"))
				select d).Select(System.IO.Path.GetFileName)
			where !GetUnchangableFilterNamesList().Contains(f)
			select f;
	}

	public void SaveAs(string newName, bool force = false)
	{
		if (GetUnchangableFilterNamesList().Contains(newName))
		{
			AppHelper.Error("Cannot override default filters list");
		}
		else
		{
			if (GetChangableFilterNamesList().Contains(newName) && !force && Interaction.MsgBox(Words.AreYouSureOverrideFilter, MsgBoxStyle.YesNo | MsgBoxStyle.Information, Words.Filters) != MsgBoxResult.Yes)
			{
				return;
			}
			if (!AppHelper.EnsureDirectoryExist(AppHelper.FiltersFolder))
			{
				AppHelper.Error(Words.FiltersCannotCreateDir + ": " + AppHelper.FiltersFolder);
				return;
			}
			string text = System.IO.Path.Combine(AppHelper.FiltersFolder, newName);
			if (AppHelper.EnsureDirectoryExist(text))
			{
				string filterSelectedName = MainWindowVm.FilterSelectedName;
				try
				{
					GetXmlDocument(out var xmlDocument, out var _, FiltersRoot, cleanXml: false);
					MainWindowVm.FilterSelectedName = newName;
					if (System.IO.File.Exists(FiltersFilePath))
					{
						System.IO.File.SetAttributes(FiltersFilePath, FileAttributes.Normal);
					}
					xmlDocument.Save(FiltersFilePath);
					return;
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					AppHelper.Error("Failed to save filters. Check log for details.");
					MainWindowVm.FilterSelectedName = filterSelectedName;
					return;
				}
			}
			AppHelper.Error(Words.FiltersCannotCreateDir + ": " + text);
		}
	}

	public void RemoveFiltersList()
	{
		string filterFolder = FilterFolder;
		MainWindowVm.FilterSelectedName = ((UserLanguageHelper.Language == "en") ? DefaultFilterNames[2] : DefaultFilterNames[1]);
		AppHelper.DeleteDirectoryHard(filterFolder);
	}
}
