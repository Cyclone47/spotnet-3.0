using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using Spotnet.Mvvm;
using Spotnet.Mvvm.Threading;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Views;

namespace Spotnet.ViewModel;

public class FilterViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private bool _isExpanded;

	private bool _isSelected;

	private bool _isVisible;

	private string _name;

	private int _newCount;

	public FilterViewModel Parent { get; set; }

	public ObservableCollection<FilterViewModel> Children { get; set; }

	public string Name
	{
		get
		{
			return _name;
		}
		private set
		{
			if (!(_name == value))
			{
				_name = value;
				RaisePropertyChanged("DisplayText");
			}
		}
	}

	public string Query { get; set; }

	public bool IsVisible
	{
		get
		{
			return _isVisible;
		}
		set
		{
			if (_isVisible != value)
			{
				_isVisible = value;
				RaisePropertyChanged("IsVisible");
				RaisePropertyChanged("Visibility");
			}
		}
	}

	public int NewCount
	{
		get
		{
			return _newCount;
		}
		set
		{
			if (_newCount != value)
			{
				_newCount = value;
				RaisePropertyChanged("DisplayText");
			}
		}
	}

	private string ImageNormal { get; set; }

	private string ImageSelected { get; set; }

	public string Id => AppHelper.MakeMd5(FullPathString);

	public string DisplayText
	{
		get
		{
			if (Name.Trim().IsNullOrEmpty())
			{
				return "";
			}
			if (NewCount > 0)
			{
				return Name + " (" + NewCount + ")";
			}
			return Name;
		}
	}

	public bool IsExpanded
	{
		get
		{
			return _isExpanded;
		}
		set
		{
			if (value != _isExpanded)
			{
				_isExpanded = value;
				RaisePropertyChanged("IsExpanded");
				// A group with no icon of its own shows the folder pair, so folding it
				// changes the glyph.
				RaisePropertyChanged(nameof(Glyph));
			}
			if (_isExpanded && Parent != null)
			{
				Parent.IsExpanded = true;
			}
		}
	}

	public Visibility Visibility
	{
		get
		{
			if (!IsVisible)
			{
				return Visibility.Hidden;
			}
			return Visibility.Visible;
		}
	}

	/// <summary>
	/// The FontAwesome glyph for this filter under the Modern styles, or null when the
	/// bitmap in <see cref="Image" /> should be drawn instead.
	/// </summary>
	/// <remarks>
	/// A group whose own icon is not one the glyph table knows still gets an icon: the
	/// folder pair, which follows whether the group is folded or unfolded.
	/// </remarks>
	public string Glyph
	{
		get
		{
			if (!ThemeHelper.UsesGlyphIcons)
			{
				return null;
			}

			string glyph = FilterIconGlyphs.ForIcon(
				(IsSelected && !ImageSelected.IsNullOrEmpty()) ? ImageSelected : ImageNormal);
			if (glyph != null)
			{
				return glyph;
			}

			return (Children != null && Children.Count > 0)
				? (IsExpanded ? FilterIconGlyphs.FolderOpen : FilterIconGlyphs.FolderClosed)
				: null;
		}
	}

	public bool HasGlyph => Glyph != null;

	/// <summary>Font size for <see cref="Glyph" />, matched to <see cref="ImageSize" />.</summary>
	public double GlyphSize => (NestingLevel < 2) ? 15.0 : 12.0;

	/// <summary>
	/// Re-reads everything that depends on the active style. Called for every filter when
	/// the style changes, so the tree swaps between bitmaps and glyphs in place.
	/// </summary>
	public void RefreshIcon()
	{
		RaisePropertyChanged(nameof(Glyph));
		RaisePropertyChanged(nameof(HasGlyph));
		RaisePropertyChanged(nameof(GlyphSize));
		RaisePropertyChanged(nameof(Image));
		RaisePropertyChanged(nameof(ImageSize));

		foreach (FilterViewModel child in Children ?? Enumerable.Empty<FilterViewModel>())
		{
			child.RefreshIcon();
		}
	}

	public string Image
	{
		get
		{
			string text = ((IsSelected && !ImageSelected.IsNullOrEmpty()) ? ImageSelected : ImageNormal);
			text = (text ?? "").Trim();
			if (!text.Contains(":"))
			{
				while (text.StartsWith("/"))
				{
					text = text.Substring(1);
				}
				while (text.StartsWith("\\"))
				{
					text = text.Substring(1);
				}
				string text2 = Path.Combine(AppHelper.FiltersFolder, Settings.Default.Filter, text);
				if (File.Exists(text2))
				{
					return text2;
				}
				string text3 = Path.Combine(AppHelper.FiltersFolder, text);
				if (File.Exists(text3))
				{
					return text3;
				}
				return null;
			}
			return text;
		}
	}

	private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

	public double ImageSize
	{
		get
		{
			if (MainWindowVm.FiltersDb.DoNotResizeFilterImages)
			{
				return double.NaN;
			}
			return (NestingLevel < 2) ? 24 : 16;
		}
	}

	public SolidColorBrush GenreColorBrush
	{
		get
		{
			if (Settings.Default.ColoringFilters)
			{
				int getCatFromQuery = GetCatFromQuery;
				if (getCatFromQuery > 0 && getCatFromQuery < 1000)
				{
					return Spots.CategoryToColor(getCatFromQuery);
				}
			}
			return Brushes.Transparent;
		}
	}

	public Visibility GenreColorVisibility
	{
		get
		{
			int getCatFromQuery = GetCatFromQuery;
			if (!Settings.Default.ColoringFilters || getCatFromQuery <= 0 || getCatFromQuery >= 1000)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	private int GetCatFromQuery
	{
		get
		{
			if (!int.TryParse(Query.Replace(" ", "").Replace("cat=", ""), out var result))
			{
				return -1;
			}
			return result;
		}
	}

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (_isSelected != value)
			{
				_isSelected = value;
				RaisePropertyChanged("IsSelected");
				if (!ImageSelected.IsNullOrEmpty())
				{
					RaisePropertyChanged("Image");
					RaisePropertyChanged(nameof(Glyph));
				}
			}
		}
	}

	public int NestingLevel
	{
		get
		{
			if (Parent == null)
			{
				return 0;
			}
			return Parent.NestingLevel + 1;
		}
	}

	public FilterViewModel Top
	{
		get
		{
			FilterViewModel filterViewModel = this;
			while (filterViewModel.Parent != null)
			{
				filterViewModel = filterViewModel.Parent;
			}
			return filterViewModel;
		}
	}

	public List<string> FullPath
	{
		get
		{
			List<string> list = new List<string>(NestingLevel);
			for (FilterViewModel filterViewModel = this; filterViewModel != null; filterViewModel = filterViewModel.Parent)
			{
				list.Add(filterViewModel.Name);
			}
			return list;
		}
	}

	public string FullPathString
	{
		get
		{
			List<string> list = FullPath.ToList();
			list.Reverse();
			string text = string.Join("/", list);
			if (text.StartsWith("ROOT/"))
			{
				text = text.Substring(5);
			}
			return text;
		}
	}

	public bool CanBeModified
	{
		get
		{
			string text = Filters.SimplifyQuery(Query).Replace(" ", "").Replace("(", "")
				.Replace(")", "");
			if (text.Equals("cat!=0"))
			{
				return true;
			}
			if (new Regex("^cat=[1-6,9]$", RegexOptions.IgnoreCase).IsMatch(text))
			{
				return true;
			}
			return new Regex("^catsmatch'[a-zA-Z0-9\\s]+'$", RegexOptions.IgnoreCase).IsMatch(text);
		}
	}

	public FilterViewModel(string name, string query, string image = "", string imageSelected = "", bool isVisible = true)
	{
		Children = new ObservableCollection<FilterViewModel>();
		Name = name;
		Query = query;
		IsVisible = isVisible;
		_isSelected = false;
		ImageNormal = image;
		ImageSelected = imageSelected;
		AssignDefaultImageIfNecessary();
		if (Favorites.IsFavoritesQuery(Query))
		{
			ImageNormal = "\\Images\\fav24.ico";
			Name = Words.Favorites;
		}
		MainWindow.ColoringForFiltersChanged += delegate
		{
			RaisePropertyChanged("GenreColorBrush");
			RaisePropertyChanged("GenreColorVisibility");
		};
	}

	public void ChildAdd(FilterViewModel child)
	{
		ChildInsert(Children.Count, child);
	}

	public void ChildrenAddRange(List<FilterViewModel> children)
	{
		foreach (FilterViewModel child in children)
		{
			ChildAdd(child);
		}
	}

	public void ChildInsert(int index, FilterViewModel child)
	{
		if (child.IsVisible)
		{
			if (index > Children.Count)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			if (Children.Any((FilterViewModel c) => c.Name.EqualsIgnoreCase(child.Name)))
			{
				throw new Exception(string.Format(Words.FilterNameAlreadyExists, child.Name));
			}
			child.Parent = this;
			DispatcherHelper.UIDispatcher.Invoke(delegate
			{
				Children.Insert(index, child);
			});
		}
	}

	public void ChildRemove(FilterViewModel child)
	{
		DispatcherHelper.UIDispatcher.Invoke(delegate
		{
			Children.Remove(child);
		});
	}

	public void ChildrenClear()
	{
		DispatcherHelper.UIDispatcher.Invoke(delegate
		{
			Children.Clear();
		});
	}

	public XmlNode CreateXmlElement(XmlDocument doc)
	{
		XmlElement xmlElement = doc.CreateElement("Filter");
		xmlElement.SetAttribute("Name", Name);
		if (!IsVisible)
		{
			xmlElement.SetAttribute("Visible", "false");
		}
		if (!ImageNormal.IsNullOrEmpty())
		{
			xmlElement.SetAttribute("Image", ImageNormal);
		}
		if (!ImageSelected.IsNullOrEmpty())
		{
			xmlElement.SetAttribute("SelectedImage", ImageSelected);
		}
		string value = (Query.IsNullOrWhiteSpace() ? " " : Query);
		xmlElement.SetAttribute("Query", value);
		if (Children.Any())
		{
			foreach (FilterViewModel child in Children)
			{
				xmlElement.AppendChild(child.CreateXmlElement(doc));
			}
		}
		return xmlElement;
	}

	private void AssignDefaultImageIfNecessary()
	{
		if (!Name.IsNullOrWhiteSpace() && Image.IsNullOrEmpty())
		{
			if (Query.IsNullOrEmpty() || Favorites.IsFavoritesQuery(Query))
			{
				ImageNormal = "\\Images\\favorites2.ico";
			}
			else
			{
				ImageNormal = ((!Query.ToLower().Contains("tag match '") && !Query.ToLower().Contains("sender match '")) ? ((!Query.ToLower().Contains("tag like") && !Query.ToLower().Contains("tag = '")) ? "\\Images\\custom2.ico" : "\\Images\\tag2.ico") : "\\Images\\people2.ico");
			}
		}
	}

	public override string ToString()
	{
		return FullPathString;
	}
}
