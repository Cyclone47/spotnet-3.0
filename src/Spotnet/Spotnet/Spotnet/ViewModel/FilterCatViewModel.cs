using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Spotnet.Mvvm;
using Microsoft.VisualBasic;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.ViewModel;

internal class FilterCatViewModel : ViewModelBase
{
	public SpotCat CatLink;

	private bool? _isChecked;

	private bool? _isExpanded;

	private bool _noCheckbox;

	private FilterCatViewModel _parent;

	private static ReadOnlyCollection<FilterCatViewModel> _rootCollection;

	public List<FilterCatViewModel> Children { get; private set; }

	public bool? IsChecked
	{
		get
		{
			return _isChecked;
		}
		set
		{
			SetIsChecked(value, updateChildren: true, updateParent: true);
		}
	}

	public bool? IsExpanded
	{
		get
		{
			return _isExpanded;
		}
		set
		{
			if ((value != true || _isExpanded != true) && (value != false || _isExpanded != false))
			{
				_isExpanded = value;
				if (_isExpanded == true && _parent != null)
				{
					_parent.IsExpanded = true;
				}
				RaisePropertyChanged("IsExpanded");
			}
		}
	}

	public bool IsInitiallySelected { get; private set; }

	public string IsVisible
	{
		get
		{
			if (!_noCheckbox)
			{
				return "Visible";
			}
			return "Hidden";
		}
		set
		{
			_noCheckbox = !value.IsNullOrEmpty();
		}
	}

	public string Name { get; private set; }

	public double TheMargin
	{
		get
		{
			if (!_noCheckbox)
			{
				return double.NaN;
			}
			return 0.0;
		}
	}

	public static ReadOnlyCollection<FilterCatViewModel> RootCollection => LazyInitializer.EnsureInitialized(ref _rootCollection, () => new FilterCatViewModel("Empty").InitializeAsRoot());

	private FilterCatViewModel(string name)
	{
		IsInitiallySelected = false;
		_isChecked = false;
		_isExpanded = false;
		_noCheckbox = false;
		Name = name;
		Children = new List<FilterCatViewModel>();
	}

	private ReadOnlyCollection<FilterCatViewModel> InitializeAsRoot()
	{
		SpotCat spotCat5 = new SpotCat();
		Collection children = spotCat5.Children;
		children.Add(GetSubCat(AppHelper.CatDesc(1, 0), "0"), AppHelper.CatDesc(1, 0));
		Collection children2 = ((SpotCat)children[AppHelper.CatDesc(1, 0)]).Children;
		children2.Add(GetSubCat(Categories.Source, "0b"), "Bron");
		Collection children3 = ((SpotCat)children2["Bron"]).Children;
		children3.Add(GetSubCat("Retail", "b3"));
		children3.Add(GetSubCat("Telesync", "b9"));
		children3.Add(GetSubCat("R5", "b7"));
		children3.Add(GetSubCat("Cam", "b0"));
		children2.Add(GetSubCat(Categories.Language, "0c"), "Taal");
		Collection children4 = ((SpotCat)children2["Taal"]).Children;
		children4.Add(GetSubCat(Categories.LangEnSpoken, "c10"));
		children4.Add(GetSubCat(Categories.LangNlSpoken, "c11"));
		children4.Add(GetSubCat(Categories.LangGrSpoken, "c12"));
		children4.Add(GetSubCat(Categories.LangFrSpoken, "c13"));
		children4.Add(GetSubCat(Categories.LangSpSpoken, "c14"));
		children4.Add(GetSubCat(Categories.LangNoSubtitles, "c0"));
		children4.Add(GetSubCat(Categories.LangEnSubsExt, "c3"));
		children4.Add(GetSubCat(Categories.LangEnSubsInt, "c4"));
		children4.Add(GetSubCat(Categories.LangEnSubsAdj, "c7"));
		children4.Add(GetSubCat(Categories.LangNlSubsExt, "c1"));
		children4.Add(GetSubCat(Categories.LangNlSubsInt, "c2"));
		children4.Add(GetSubCat(Categories.LangNlSubsAdj, "c6"));
		children2.Add(GetSubCat(Words.ColumnGenre, "0d"), "Genre");
		Collection children5 = ((SpotCat)children2["Genre"]).Children;
		children5.Add(GetSubCat(Categories.GAction, "d0"));
		children5.Add(GetSubCat(Categories.GAnime, "d29"));
		children5.Add(GetSubCat(Categories.GAnimation, "d2"));
		children5.Add(GetSubCat(Categories.GAsian, "d28"));
		children5.Add(GetSubCat(Categories.GAdventure, "d1"));
		children5.Add(GetSubCat(Categories.GCabare, "d3"));
		children5.Add(GetSubCat(Categories.GCartoon, "d32"));
		children5.Add(GetSubCat(Categories.GDetective, "c50"));
		children5.Add(GetSubCat(Categories.GAnimals, "c51"));
		children5.Add(GetSubCat(Categories.GDocumentary, "d6"));
		children5.Add(GetSubCat(Categories.GDrama, "d7"));
		children5.Add(GetSubCat(Categories.GFamily, "d8"));
		children5.Add(GetSubCat(Categories.GFantasy, "d9"));
		children5.Add(GetSubCat(Categories.GFilmHouse, "d10"));
		children5.Add(GetSubCat(Categories.GHistory, "d41"));
		children5.Add(GetSubCat(Categories.GHorror, "d12"));
		children5.Add(GetSubCat(Categories.GYouth, "d33"));
		children5.Add(GetSubCat(Categories.GComedy, "d4"));
		children5.Add(GetSubCat(Categories.GShort, "d19"));
		children5.Add(GetSubCat(Categories.GCrime, "d5"));
		children5.Add(GetSubCat(Categories.GMusic, "d13"));
		children5.Add(GetSubCat(Categories.GMusical, "d14"));
		children5.Add(GetSubCat(Categories.GMystery, "d15"));
		children5.Add(GetSubCat(Categories.GWar, "d21"));
		children5.Add(GetSubCat(Categories.GRomantic, "d16"));
		children5.Add(GetSubCat(Categories.GSciFiction, "d17"));
		children5.Add(GetSubCat(Categories.GSport, "d18"));
		children5.Add(GetSubCat(Categories.GThriller, "d20"));
		children5.Add(GetSubCat(Categories.GWoman, "d46"));
		children5.Add(GetSubCat(Categories.GWestern, "d22"));
		children5.Add(GetSubCat(Categories.GWhatHappened, "d54"));
		children2.Add(GetSubCat(Words.ColumnFormat, "0a"), "Formaat");
		Collection children6 = ((SpotCat)children2["Formaat"]).Children;
		children6.Add(GetSubCat("MPG", "a2"));
		children6.Add(GetSubCat("WMV", "a1"));
		children6.Add(GetSubCat("DivX", "a0"));
		children6.Add(GetSubCat("DVD5", "a3"));
		children6.Add(GetSubCat("DVD9", "a10"));
		children6.Add(GetSubCat("Bluray", "a6"));
		children6.Add(GetSubCat("x264", "a9"));
		children.Add(GetSubCat(AppHelper.CatDesc(6, 0), "5"), AppHelper.CatDesc(6, 0));
		Collection children7 = ((SpotCat)children[AppHelper.CatDesc(6, 0)]).Children;
		children7.Add(GetSubCat(Categories.Source, "5b"), "Bron");
		Collection children8 = ((SpotCat)children7["Bron"]).Children;
		children8.Add(GetSubCat("Retail", "b3"));
		children8.Add(GetSubCat("Telesync", "b9"));
		children8.Add(GetSubCat("R5", "b7"));
		children8.Add(GetSubCat("Cam", "b0"));
		children7.Add(GetSubCat(Categories.Language, "5c"), "Taal");
		Collection children9 = ((SpotCat)children7["Taal"]).Children;
		children9.Add(GetSubCat(Categories.LangEnSpoken, "c10"));
		children9.Add(GetSubCat(Categories.LangNlSpoken, "c11"));
		children9.Add(GetSubCat(Categories.LangGrSpoken, "c12"));
		children9.Add(GetSubCat(Categories.LangFrSpoken, "c13"));
		children9.Add(GetSubCat(Categories.LangSpSpoken, "c14"));
		children9.Add(GetSubCat(Categories.LangNoSubtitles, "c0"));
		children9.Add(GetSubCat(Categories.LangEnSubsExt, "c3"));
		children9.Add(GetSubCat(Categories.LangEnSubsInt, "c4"));
		children9.Add(GetSubCat(Categories.LangEnSubsAdj, "c7"));
		children9.Add(GetSubCat(Categories.LangNlSubsExt, "c1"));
		children9.Add(GetSubCat(Categories.LangNlSubsInt, "c2"));
		children9.Add(GetSubCat(Categories.LangNlSubsAdj, "c6"));
		children7.Add(GetSubCat(Words.ColumnGenre, "5d"), "Genre");
		Collection children10 = ((SpotCat)children7["Genre"]).Children;
		children10.Add(GetSubCat(Categories.GAction, "d0"));
		children10.Add(GetSubCat(Categories.GAnime, "d29"));
		children10.Add(GetSubCat(Categories.GAnimation, "d2"));
		children10.Add(GetSubCat(Categories.GAsian, "d28"));
		children10.Add(GetSubCat(Categories.GAdventure, "d1"));
		children10.Add(GetSubCat(Categories.GCabare, "d3"));
		children10.Add(GetSubCat(Categories.GCartoon, "d32"));
		children10.Add(GetSubCat(Categories.GDetective, "c50"));
		children10.Add(GetSubCat(Categories.GAnimals, "c51"));
		children10.Add(GetSubCat(Categories.GDocumentary, "d6"));
		children10.Add(GetSubCat(Categories.GDrama, "d7"));
		children10.Add(GetSubCat(Categories.GFamily, "d8"));
		children10.Add(GetSubCat(Categories.GFantasy, "d9"));
		children10.Add(GetSubCat(Categories.GFilmHouse, "d10"));
		children10.Add(GetSubCat(Categories.GHistory, "d41"));
		children10.Add(GetSubCat(Categories.GHorror, "d12"));
		children10.Add(GetSubCat(Categories.GYouth, "d33"));
		children10.Add(GetSubCat(Categories.GComedy, "d4"));
		children10.Add(GetSubCat(Categories.GShort, "d19"));
		children10.Add(GetSubCat(Categories.GCrime, "d5"));
		children10.Add(GetSubCat(Categories.GMusic, "d13"));
		children10.Add(GetSubCat(Categories.GMusical, "d14"));
		children10.Add(GetSubCat(Categories.GMystery, "d15"));
		children10.Add(GetSubCat(Categories.GWar, "d21"));
		children10.Add(GetSubCat(Categories.GRomantic, "d16"));
		children10.Add(GetSubCat(Categories.GSciFiction, "d17"));
		children10.Add(GetSubCat(Categories.GSport, "d18"));
		children10.Add(GetSubCat(Categories.GThriller, "d20"));
		children10.Add(GetSubCat(Categories.GWoman, "d46"));
		children10.Add(GetSubCat(Categories.GWestern, "d22"));
		children10.Add(GetSubCat(Categories.GWhatHappened, "d54"));
		children7.Add(GetSubCat(Words.ColumnFormat, "5a"), "Formaat");
		Collection children11 = ((SpotCat)children7["Formaat"]).Children;
		children11.Add(GetSubCat("MPG", "a2"));
		children11.Add(GetSubCat("WMV", "a1"));
		children11.Add(GetSubCat("DivX", "a0"));
		children11.Add(GetSubCat("DVD5", "a3"));
		children11.Add(GetSubCat("DVD9", "a10"));
		children11.Add(GetSubCat("Bluray", "a6"));
		children11.Add(GetSubCat("x264", "a9"));
		children.Add(GetSubCat(AppHelper.CatDesc(5, 0), "4"), AppHelper.CatDesc(5, 0));
		Collection children12 = ((SpotCat)children[AppHelper.CatDesc(5, 0)]).Children;
		children12.Add(GetSubCat(Categories.Language, "4c"), "Taal");
		Collection children13 = ((SpotCat)children12["Taal"]).Children;
		children13.Add(GetSubCat(Categories.LangEnglish, "c4"));
		children13.Add(GetSubCat(Categories.LangDutch, "c2"));
		children13.Add(GetSubCat(Categories.LangGerman, "c12"));
		children13.Add(GetSubCat(Categories.LangFrench, "c13"));
		children13.Add(GetSubCat(Categories.LangSpanish, "c14"));
		children12.Add(GetSubCat(Words.ColumnGenre, "4d"), "Genre");
		Collection children14 = ((SpotCat)children12["Genre"]).Children;
		children14.Add(GetSubCat(Categories.GAdventure, "d1"));
		children14.Add(GetSubCat(Categories.BGBiography, "d49"));
		children14.Add(GetSubCat(Categories.BGComputer, "d35"));
		children14.Add(GetSubCat(Categories.BGCover, "d30"));
		children14.Add(GetSubCat(Categories.BGNewspaper, "d43"));
		children14.Add(GetSubCat(Categories.GDetective, "d50"));
		children14.Add(GetSubCat(Categories.GAnimals, "d51"));
		children14.Add(GetSubCat(Categories.GDrama, "d7"));
		children14.Add(GetSubCat(Categories.BGEconomy, "d34"));
		children14.Add(GetSubCat(Categories.GFantasy, "d9"));
		children14.Add(GetSubCat(Categories.BGHealth, "d40"));
		children14.Add(GetSubCat(Categories.BGHandicraft, "d39"));
		children14.Add(GetSubCat(Categories.GHistory, "d41"));
		children14.Add(GetSubCat(Categories.BGHobby, "d36"));
		children14.Add(GetSubCat(Categories.GYouth, "d33"));
		children14.Add(GetSubCat(Categories.BGCrafts, "d38"));
		children14.Add(GetSubCat(Categories.BGCooking, "d37"));
		children14.Add(GetSubCat(Categories.BGArt, "d60"));
		children14.Add(GetSubCat(Categories.GCrime, "d5"));
		children14.Add(GetSubCat(Categories.GMystery, "d15"));
		children14.Add(GetSubCat(Categories.BGNonFiction, "d55"));
		children14.Add(GetSubCat(Categories.GWar, "d21"));
		children14.Add(GetSubCat(Categories.BGPoetry, "d57"));
		children14.Add(GetSubCat(Categories.BGPsychology, "d42"));
		children14.Add(GetSubCat(Categories.BGTravel, "d53"));
		children14.Add(GetSubCat(Categories.BGReligion, "d47"));
		children14.Add(GetSubCat(Categories.BGNovel, "d48"));
		children14.Add(GetSubCat(Categories.GRomantic, "d16"));
		children14.Add(GetSubCat(Categories.GSciFiction, "d17"));
		children14.Add(GetSubCat(Categories.GSport, "d18"));
		children14.Add(GetSubCat(Categories.BGFairytale, "d58"));
		children14.Add(GetSubCat(Categories.BGComicStrip, "d31"));
		children14.Add(GetSubCat(Categories.BGStudy, "d32"));
		children14.Add(GetSubCat(Categories.BGTechnique, "d59"));
		children14.Add(GetSubCat(Categories.GThriller, "d20"));
		children14.Add(GetSubCat(Categories.BGJournal, "d44"));
		children14.Add(GetSubCat(Categories.GWoman, "d46"));
		children14.Add(GetSubCat(Categories.GWhatHappened, "d54"));
		children14.Add(GetSubCat(Categories.BGScience, "d45"));
		children14.Add(GetSubCat(Categories.BGBusiness, "d34"));
		children.Add(GetSubCat(AppHelper.CatDesc(2, 0), "1"), AppHelper.CatDesc(2, 0));
		Collection children15 = ((SpotCat)children[AppHelper.CatDesc(2, 0)]).Children;
		children15.Add(GetSubCat(Categories.Source, "1b"), "Bron");
		Collection children16 = ((SpotCat)children15["Bron"]).Children;
		children16.Add(GetSubCat("CD", "b0"));
		children16.Add(GetSubCat("DVD", "b3"));
		children16.Add(GetSubCat(Categories.TRadio, "b1"));
		children16.Add(GetSubCat(Categories.TVinyl, "b5"));
		children16.Add(GetSubCat(Categories.TStream, "b6"));
		children15.Add(GetSubCat(Categories.Type, "1z"), "Type");
		Collection children17 = ((SpotCat)children15["Type"]).Children;
		children17.Add(GetSubCat(Categories.MGAlbum, "z0"));
		children17.Add(GetSubCat(Categories.MGLiveset, "z1"));
		children17.Add(GetSubCat(Categories.MGPodcast, "z2"));
		children17.Add(GetSubCat(Categories.MGAudiobook, "z3"));
		children15.Add(GetSubCat(Words.ColumnGenre, "1d"), "Genre");
		Collection children18 = ((SpotCat)children15["Genre"]).Children;
		children18.Add(GetSubCat(Categories.MBalkans, "d34"));
		children18.Add(GetSubCat(Categories.MBlues, "d0"));
		children18.Add(GetSubCat(Categories.MCabaret, "d2"));
		children18.Add(GetSubCat(Categories.MChillout, "d36"));
		children18.Add(GetSubCat(Categories.MClassics, "d24"));
		children18.Add(GetSubCat(Categories.MCompilation, "d1"));
		children18.Add(GetSubCat(Categories.MCountry, "d26"));
		children18.Add(GetSubCat(Categories.MDance, "d3"));
		children18.Add(GetSubCat(Categories.MDisco, "d23"));
		children18.Add(GetSubCat(Categories.MVarious, "d4"));
		children18.Add(GetSubCat(Categories.MDnB, "d29"));
		children18.Add(GetSubCat(Categories.MDubstep, "d27"));
		children18.Add(GetSubCat(Categories.MElectro, "d30"));
		children18.Add(GetSubCat(Categories.MFolk, "d31"));
		children18.Add(GetSubCat(Categories.MHardstyle, "d5"));
		children18.Add(GetSubCat(Categories.MHiphop, "d15"));
		children18.Add(GetSubCat(Categories.MHollands, "d11"));
		children18.Add(GetSubCat(Categories.MJazz, "d7"));
		children18.Add(GetSubCat(Categories.MYouth, "d8"));
		children18.Add(GetSubCat(Categories.MClassical, "d9"));
		children18.Add(GetSubCat(Categories.MLatin, "d37"));
		children18.Add(GetSubCat(Categories.MLive, "d38"));
		children18.Add(GetSubCat(Categories.MMetal, "d25"));
		children18.Add(GetSubCat(Categories.MNederhop, "d28"));
		children18.Add(GetSubCat(Categories.MPop, "d13"));
		children18.Add(GetSubCat(Categories.MRnB, "d14"));
		children18.Add(GetSubCat(Categories.MReggae, "d16"));
		children18.Add(GetSubCat(Categories.MRock, "d18"));
		children18.Add(GetSubCat(Categories.MSoundtrack, "d19"));
		children18.Add(GetSubCat(Categories.MSoul, "d32"));
		children18.Add(GetSubCat(Categories.MTrance, "d33"));
		children18.Add(GetSubCat(Categories.MTechno, "d35"));
		children18.Add(GetSubCat(Categories.MWorld, "d6"));
		children15.Add(GetSubCat(Categories.Bitrate, "1c"), "Bitrate");
		Collection children19 = ((SpotCat)children15["Bitrate"]).Children;
		children19.Add(GetSubCat("< 96kbit", "c1"));
		children19.Add(GetSubCat("96kbit", "c2"));
		children19.Add(GetSubCat("128kbit", "c3"));
		children19.Add(GetSubCat("160kbit", "c4"));
		children19.Add(GetSubCat("192kbit", "c5"));
		children19.Add(GetSubCat("256kbit", "c6"));
		children19.Add(GetSubCat("320kbit", "c7"));
		children19.Add(GetSubCat(Categories.BitrateLossless, "c8"));
		children19.Add(GetSubCat(Categories.BitrateVariable, "c0"));
		children15.Add(GetSubCat(Words.ColumnFormat, "1a"), "Formaat");
		Collection children20 = ((SpotCat)children15["Formaat"]).Children;
		children20.Add(GetSubCat("MP3", "a0"));
		children20.Add(GetSubCat("WMA", "a1"));
		children20.Add(GetSubCat("WAV", "a2"));
		children20.Add(GetSubCat("OGG", "a3"));
		children20.Add(GetSubCat("DTS", "a5"));
		children20.Add(GetSubCat("AAC", "a6"));
		children20.Add(GetSubCat("APE", "a7"));
		children20.Add(GetSubCat("FLAC", "a8"));
		children20.Add(GetSubCat("EAC", "a4"));
		children.Add(GetSubCat(AppHelper.CatDesc(3, 0), "2"), AppHelper.CatDesc(3, 0));
		Collection children21 = ((SpotCat)children[AppHelper.CatDesc(3, 0)]).Children;
		children21.Add(GetSubCat(Words.ColumnGenre, "2c"), "Genre");
		Collection children22 = ((SpotCat)children21["Genre"]).Children;
		children22.Add(GetSubCat(Categories.GAction, "c0"));
		children22.Add(GetSubCat(Categories.GAdventure, "c1"));
		children22.Add(GetSubCat(Categories.GBoardGame, "c13"));
		children22.Add(GetSubCat(Categories.GEducational, "c15"));
		children22.Add(GetSubCat(Categories.GYouth, "c10"));
		children22.Add(GetSubCat(Categories.GCards, "c14"));
		children22.Add(GetSubCat(Categories.GMusic, "c16"));
		children22.Add(GetSubCat(Categories.GParty, "c17"));
		children22.Add(GetSubCat(Categories.GPlatform, "c8"));
		children22.Add(GetSubCat(Categories.GPuzzel, "c11"));
		children22.Add(GetSubCat(Categories.GRace, "c5"));
		children22.Add(GetSubCat(Categories.GRoleplay, "c3"));
		children22.Add(GetSubCat(Categories.GShooter, "c7"));
		children22.Add(GetSubCat(Categories.GSimulation, "c4"));
		children22.Add(GetSubCat(Categories.GSport, "c9"));
		children22.Add(GetSubCat(Categories.GStrategy, "c2"));
		children22.Add(GetSubCat(Categories.GFly, "c6"));
		children21.Add(GetSubCat(Words.ColumnFormat, "2b"), "Formaat");
		Collection children23 = ((SpotCat)children21["Formaat"]).Children;
		children23.Add(GetSubCat("Rip", "b1"));
		children23.Add(GetSubCat("DVD", "b2"));
		children23.Add(GetSubCat("DLC", "b3"));
		children23.Add(GetSubCat("Patch", "b5"));
		children23.Add(GetSubCat("Crack", "b6"));
		children21.Add(GetSubCat(Categories.Platform, "2a"), "Platform");
		Collection children24 = ((SpotCat)children21["Platform"]).Children;
		children24.Add(GetSubCat("Windows", "a0"));
		children24.Add(GetSubCat("Linux", "a2"));
		children24.Add(GetSubCat("Macintosh", "a1"));
		children24.Add(GetSubCat("XBox", "a6"));
		children24.Add(GetSubCat("XBox 360", "a7"));
		children24.Add(GetSubCat("Gameboy Advance", "a8"));
		children24.Add(GetSubCat("Gamecube", "a9"));
		children24.Add(GetSubCat("Nintendo DS", "a10"));
		children24.Add(GetSubCat("Nintendo Wii", "a11"));
		children24.Add(GetSubCat("Playstation", "a3"));
		children24.Add(GetSubCat("Playstation 2", "a4"));
		children24.Add(GetSubCat("Playstation 3", "a12"));
		children24.Add(GetSubCat("Playstation Portable", "a5"));
		children24.Add(GetSubCat("Windows Phone", "a13"));
		children24.Add(GetSubCat("iOs", "a14"));
		children24.Add(GetSubCat("Android", "a15"));
		children.Add(GetSubCat(AppHelper.CatDesc(4, 0), "3"), AppHelper.CatDesc(4, 0));
		Collection children25 = ((SpotCat)children[AppHelper.CatDesc(4, 0)]).Children;
		children25.Add(GetSubCat(Words.ColumnGenre, "3b"), "Genre");
		Collection children26 = ((SpotCat)children25["Genre"]).Children;
		children26.Add(GetSubCat(Categories.SwAudio, "b0"));
		children26.Add(GetSubCat(Categories.SwSafeguard, "b23"));
		children26.Add(GetSubCat(Categories.SwCommunication, "b29"));
		children26.Add(GetSubCat(Categories.SwDownload, "b15"));
		children26.Add(GetSubCat(Categories.SwEducational, "b26"));
		children26.Add(GetSubCat(Categories.SwPhoto, "b9"));
		children26.Add(GetSubCat(Categories.SwGraphic, "b2"));
		children26.Add(GetSubCat(Categories.SwInternet, "b28"));
		children26.Add(GetSubCat(Categories.SwOffice, "b27"));
		children26.Add(GetSubCat(Categories.SwDevelopment, "b30"));
		children26.Add(GetSubCat(Categories.SwSpotnet, "b31"));
		children26.Add(GetSubCat(Categories.SwSystem, "b24"));
		children26.Add(GetSubCat(Categories.SwVideo, "b1"));
		children25.Add(GetSubCat(Categories.Platform, "3a"), "Platform");
		Collection children27 = ((SpotCat)children25["Platform"]).Children;
		children27.Add(GetSubCat("Windows", "a0"));
		children27.Add(GetSubCat("Linux", "a2"));
		children27.Add(GetSubCat("Macintosh", "a1"));
		children27.Add(GetSubCat("Navigatie", "a5"));
		children27.Add(GetSubCat("iOs", "a6"));
		children27.Add(GetSubCat("Android", "a7"));
		children27.Add(GetSubCat("Windows Phone", "a4"));
		children.Add(GetSubCat(Categories.CatErotica, "8"), "Erotiek");
		Collection children28 = ((SpotCat)children["Erotiek"]).Children;
		children28.Add(GetSubCat(Categories.Language, "8c"), "Taal");
		Collection children29 = ((SpotCat)children28["Taal"]).Children;
		children29.Add(GetSubCat(Categories.LangEnSpoken, "c10"));
		children29.Add(GetSubCat(Categories.LangNlSpoken, "c11"));
		children29.Add(GetSubCat(Categories.LangGrSpoken, "c12"));
		children29.Add(GetSubCat(Categories.LangFrSpoken, "c13"));
		children29.Add(GetSubCat(Categories.LangSpSpoken, "c14"));
		children29.Add(GetSubCat(Categories.LangNoSubtitles, "c0"));
		children29.Add(GetSubCat(Categories.LangEnSubsExt, "c3"));
		children29.Add(GetSubCat(Categories.LangEnSubsInt, "c4"));
		children29.Add(GetSubCat(Categories.LangEnSubsAdj, "c7"));
		children29.Add(GetSubCat(Categories.LangNlSubsExt, "c1"));
		children29.Add(GetSubCat(Categories.LangNlSubsInt, "c2"));
		children29.Add(GetSubCat(Categories.LangNlSubsAdj, "c6"));
		children28.Add(GetSubCat(Words.ColumnGenre, "8d"), "Genre");
		Collection children30 = ((SpotCat)children28["Genre"]).Children;
		children30.Add(GetSubCat(Categories.SexHetero, "d23"));
		children30.Add(GetSubCat(Categories.SexHomo, "d24"));
		children30.Add(GetSubCat(Categories.SexLesbo, "d25"));
		children30.Add(GetSubCat(Categories.SexAmateur, "d76"));
		children30.Add(GetSubCat(Categories.SexBBW, "d84"));
		children30.Add(GetSubCat(Categories.SexBi, "d26"));
		children30.Add(GetSubCat(Categories.SexOutside, "d89"));
		children30.Add(GetSubCat(Categories.SexDark, "d87"));
		children30.Add(GetSubCat(Categories.SexFetich, "d82"));
		children30.Add(GetSubCat(Categories.SexGroup, "d77"));
		children30.Add(GetSubCat(Categories.SexHard, "d86"));
		children30.Add(GetSubCat(Categories.SexHentai, "d88"));
		children30.Add(GetSubCat(Categories.SexYoung, "d80"));
		children30.Add(GetSubCat(Categories.SexOld, "d83"));
		children30.Add(GetSubCat(Categories.SexPOV, "d78"));
		children30.Add(GetSubCat(Categories.SexSM, "d85"));
		children30.Add(GetSubCat(Categories.SexSoft, "d81"));
		children30.Add(GetSubCat(Categories.SexSolo, "d79"));
		children28.Add(GetSubCat(Words.ColumnFormat, "8a"), "Formaat");
		Collection children31 = ((SpotCat)children28["Formaat"]).Children;
		children31.Add(GetSubCat("MPG", "a2"));
		children31.Add(GetSubCat("WMV", "a1"));
		children31.Add(GetSubCat("DivX", "a0"));
		children31.Add(GetSubCat("DVD5", "a3"));
		children31.Add(GetSubCat("DVD9", "a10"));
		children31.Add(GetSubCat("Bluray", "a6"));
		children31.Add(GetSubCat("x264", "a9"));
		foreach (SpotCat child in spotCat5.Children)
		{
			FilterCatViewModel filterCatViewModel = new FilterCatViewModel(child.Name)
			{
				CatLink = child,
				IsExpanded = child.Name.EqualsIgnoreCase(AppHelper.CatDesc(1, 0))
			};
			foreach (SpotCat child2 in child.Children)
			{
				FilterCatViewModel filterCatViewModel2 = new FilterCatViewModel(child2.Name)
				{
					IsVisible = "yes",
					CatLink = child2
				};
				filterCatViewModel.Children.Add(filterCatViewModel2);
				filterCatViewModel2.Children.AddRange(from SpotCat spotCat4 in child2.Children
					select new FilterCatViewModel(spotCat4.Name)
					{
						CatLink = spotCat4
					});
			}
			filterCatViewModel.Initialize();
			filterCatViewModel._parent = this;
			Children.Add(filterCatViewModel);
		}
		return new ReadOnlyCollection<FilterCatViewModel>(Children);
	}

	private SpotCat GetSubCat(string sName, string sTag)
	{
		return new SpotCat
		{
			Name = sName,
			Tag = sTag
		};
	}

	private void Initialize()
	{
		if (!Children.Any())
		{
			return;
		}
		foreach (FilterCatViewModel child in Children)
		{
			child._parent = this;
			child.Initialize();
		}
		VerifyCheckState();
	}

	private void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
	{
		bool? isChecked = _isChecked;
		_isChecked = value;
		if (updateChildren && _isChecked.HasValue)
		{
			foreach (FilterCatViewModel child in Children)
			{
				child.SetIsChecked(_isChecked, updateChildren: true, updateParent: false);
			}
		}
		if (updateParent && _parent != null)
		{
			_parent.VerifyCheckState();
		}
		if ((_isChecked != true || isChecked != true) && (_isChecked != false || isChecked != false))
		{
			RaisePropertyChanged("IsChecked");
		}
	}

	private void VerifyCheckState()
	{
		bool? nullable = Children.First().IsChecked;
		if (Children.Any((FilterCatViewModel child) => !nullable.Equals(child.IsChecked)))
		{
			nullable = null;
		}
		SetIsChecked(nullable, updateChildren: false, updateParent: true);
	}

	public static void ApplyQueryToRootCollection(string query)
	{
		string text = Filters.SimplifyQuery(query).Replace(" ", "").Replace("(", "")
			.Replace(")", "");
		if (text.Equals("cat!=0"))
		{
			return;
		}
		Match match = new Regex("^cat=([1-6,9])$", RegexOptions.IgnoreCase).Match(text);
		if (match.Success)
		{
			CollapseAndUncheckAll();
			GetFilterCatByTag(match.Groups[1].Value)?.SetIsChecked(true, updateChildren: true, updateParent: false);
		}
		match = new Regex("^catsmatch'([a-zA-Z0-9\\s]+)'$", RegexOptions.IgnoreCase).Match(text);
		if (!match.Success)
		{
			return;
		}
		string value = match.Groups[1].Value;
		List<List<string>> list = new List<List<string>>();
		string[] array = value.Split(new string[1] { "AND" }, StringSplitOptions.None);
		foreach (string text2 in array)
		{
			list.Add(new List<string>(text2.Split(new string[1] { "OR" }, StringSplitOptions.None)));
		}
		CollapseAndUncheckAll();
		foreach (List<string> item in list)
		{
			foreach (string item2 in item)
			{
				FilterCatViewModel filterCatByTag = GetFilterCatByTag(item2);
				if (filterCatByTag != null)
				{
					filterCatByTag.SetIsChecked(true, updateChildren: true, updateParent: true);
					if (filterCatByTag._parent != null)
					{
						filterCatByTag._parent.IsExpanded = true;
					}
				}
			}
		}
	}

	private static FilterCatViewModel GetFilterCatByTag(string tag)
	{
		tag = tag.Trim();
		if (tag.IsNullOrEmpty())
		{
			return null;
		}
		if (!int.TryParse(tag[0].ToString(CultureInfo.InvariantCulture), out var result))
		{
			return null;
		}
		string tagLevel1 = (result - 1).ToString(CultureInfo.InvariantCulture);
		FilterCatViewModel filterCatViewModel = RootCollection.FirstOrDefault((FilterCatViewModel c) => c.CatLink.Tag.Equals(tagLevel1));
		if (filterCatViewModel == null)
		{
			return null;
		}
		if (tag.Length == 1)
		{
			return filterCatViewModel;
		}
		string tagLevel2 = tagLevel1 + tag[1];
		FilterCatViewModel filterCatViewModel2 = filterCatViewModel.Children.FirstOrDefault((FilterCatViewModel c) => c.CatLink.Tag.EqualsIgnoreCase(tagLevel2));
		if (filterCatViewModel2 == null)
		{
			return null;
		}
		if (tag.Length == 2)
		{
			return filterCatViewModel2;
		}
		string tagLevel3 = tag.Substring(1);
		return filterCatViewModel2.Children.FirstOrDefault((FilterCatViewModel c) => c.CatLink.Tag.EqualsIgnoreCase(tagLevel3));
	}

	private static void CollapseAndUncheckAll()
	{
		FilterCatViewModel parent = RootCollection.First()._parent;
		parent.SetIsChecked(false, updateChildren: true, updateParent: false);
		parent.CollapseChildren(recursive: true);
	}

	private void CollapseChildren(bool recursive)
	{
		foreach (FilterCatViewModel child in Children)
		{
			child.IsExpanded = false;
			if (recursive)
			{
				child.CollapseChildren(recursive: true);
			}
		}
	}
}
