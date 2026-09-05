using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spotnet.ViewModel;

namespace Spotnet.DataVirtualization;

public interface ISpotRow : IDisposable
{
	bool IsInFavorites { get; }

	bool IsMySpot { get; }

	bool IsDeleteSafePeriodIsNotReached { get; }

	string Afzender { get; }

	string AfzenderId { get; }

	PosterIdentType PosterIdent { get; set; }

	string Datum { get; }

	string Formaat { get; }

	string Genre { get; }

	long Id { get; }

	string Leeftijd { get; }

	string Modulus { get; }

	string Omvang { get; }

	string Tag { get; }

	string Titel { get; }

	int NumberOfSpamReports { get; }

	Visibility VisibilityOfSpamCell { get; }

	BitmapSource BitmapSource { get; }

	string Description { get; }

	bool IsVisible { get; set; }

	SpotLoadingStatusEnum SpotLoadingStatus { get; }

	FontWeight FontWeight { get; set; }

	double ImageOpacity { get; set; }

	string SpotMessageId { get; }

	string ErrorOnGettingSpot { get; set; }

	bool IsAnimatedAlready { get; set; }

	int IsNewSpotBorderThickness { get; set; }

	SolidColorBrush IsNewSpotBorderColor { get; set; }

	void LoadSpotAsync(SpotsListTypeEnum listType);
}
