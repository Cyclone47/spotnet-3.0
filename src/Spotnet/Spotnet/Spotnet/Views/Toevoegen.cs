using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Controls;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse;
using Spotnet.Properties;
using Spotnet.Utilities;
using Spotnet.ViewModel;

namespace Spotnet.Views;
public partial class Toevoegen : System.Windows.Controls.UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _desktopFolder = AppHelper.DesktopDirectory;
    private string _dummyNzbPath;
    public NntpSettings HeaderSettings { get; set; }
    private string DefaultWebsite => SpotParser.MakeGoogleSearch(TitelTextBox.Text.Trim());

    internal Toevoegen()
    {
        base.Initialized += Toevoegen_Initialized;
        InitializeComponent();
    }

    private static bool CheckRemoteImage(string sUrl, ref byte[] rez, ref long sizeX, ref long sizeY)
    {
        try
        {
            if (!AppHelper.HasHttp(sUrl))
            {
                return false;
            }

            WebClient webClient = new WebClient();
            rez = webClient.DownloadData(sUrl);
            bool flag = false;
            foreach (object key in webClient.ResponseHeaders.Keys)
            {
                string name = key.ToStringSafely();
                string a = webClient.ResponseHeaders[name];
                if (string.Equals(a, "image/png", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "image/gif", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "image/jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "image/bmp", StringComparison.OrdinalIgnoreCase))
                {
                    flag = rez.GetUpperBound(0) > 10;
                }
            }

            if (!flag)
            {
                return false;
            }

            MemoryStream memoryStream = new MemoryStream(rez);
            BitmapFrame bitmapFrame = BitmapFrame.Create(memoryStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if ((bitmapFrame.PixelWidth > 10) & (bitmapFrame.PixelHeight > 10))
            {
                sizeX = bitmapFrame.PixelWidth;
                sizeY = bitmapFrame.PixelHeight;
                memoryStream.Close();
                return true;
            }

            memoryStream.Close();
            return false;
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return false;
        }
    }

    private bool CheckLocalFileImage(string path, ref byte[] rez, ref long sizeX, ref long sizeY)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return false;
            }

            using FileStream fileStream = System.IO.File.Open(path, FileMode.Open, FileAccess.Read);
            BitmapFrame bitmapFrame = BitmapFrame.Create(fileStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (bitmapFrame.PixelWidth > 10 && bitmapFrame.PixelHeight > 10)
            {
                sizeX = bitmapFrame.PixelWidth;
                sizeY = bitmapFrame.PixelHeight;
                int num = checked((int)fileStream.Length);
                rez = new byte[num];
                fileStream.Position = 0L;
                fileStream.Read(rez, 0, num);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }

        return false;
    }

    private bool CheckUrl(string sUrl)
    {
        try
        {
            if (!AppHelper.HasHttp(sUrl))
            {
                return false;
            }

            WebRequest webRequest = WebRequest.Create(new Uri(sUrl));
            webRequest.Proxy = null;
            return string.Equals(webRequest.GetResponse().ContentType.Substring(0, 4).ToLower(), "text", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return false;
        }
    }

    private bool CheckValues(out string zErr)
    {
        if (CatBox.SelectedIndex < 0)
        {
            zErr = Words.PleaseSelectCategory;
            return false;
        }

        if (Cat2Box.IsEnabled & (Cat2Box.SelectedIndex < 0))
        {
            zErr = Words.PleaseSelectType;
            return false;
        }

        if (SCatBox.SelectedIndex < 0)
        {
            zErr = Words.PleaseSelectSubCat;
            return false;
        }

        if (TitelTextBox.Text.Trim().Length < 3)
        {
            zErr = Words.PleaseEnterSubject;
            return false;
        }

        if (DescTextBox.Text.Trim().IsNullOrEmpty())
        {
            zErr = Words.PleaseEnterDescription;
            return false;
        }

        if (DescTextBox.Text.Trim().Length < 50)
        {
            zErr = Words.DescriptionIsTooShort;
            return false;
        }

        if (GetSubCats((byte)CatBox.SelectedIndex, sIncludeHCat: false).IsNullOrEmpty())
        {
            zErr = Words.PleaseSelectAtLeastOneCat;
            return false;
        }

        if (ImageTextBox.Text.Trim().IsNullOrEmpty())
        {
            zErr = Words.PleaseAddThePicture;
            return false;
        }

        if (NzbTextBox.Text.Trim().IsNullOrEmpty())
        {
            zErr = Words.PleaseAddNZBFile;
            return false;
        }

        if (NzbTextBox.Text.Trim().Equals(EncryptedNzbTextBox.Text.Trim()))
        {
            zErr = Words.EncryptedNZBLikeMainNZBError;
            return false;
        }

        if (PosterTextBox.Text.Trim().Length < 3)
        {
            zErr = Words.SenderNameIsTooShort;
            return false;
        }

        zErr = "";
        return true;
    }

    private void DoButton()
    {
        try
        {
            string zErr;
            try
            {
                if (DoPost(out zErr))
                {
                    System.Windows.MessageBox.Show(Words.SpotWasAdded, Words.Thanks, MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    Settings.Default.Nickname = AppHelper.StripNonAlphaNumericCharacters(PosterTextBox.Text);
                    Settings.Default.Tagname = AppHelper.StripNonAlphaNumericCharacters(TagTextBox.Text);
                    Settings.Default.Save();
                    ((CloseableTabItem)base.Parent).CloseMe();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                zErr = ex.Message;
            }

            AppHelper.Error(zErr);
            base.IsEnabled = true;
            PostButton.IsEnabled = true;
        }
        finally
        {
            base.Cursor = null;
            Mouse.OverrideCursor = null;
        }
    }

    private bool DoPost(out string zErr)
    {
        byte[] rez = null;
        long sizeX = 0L;
        long sizeY = 0L;
        if (!CheckValues(out zErr))
        {
            return false;
        }

        if (!System.IO.File.Exists(ImageTextBox.Text.Trim()))
        {
            if (!CheckRemoteImage(AppHelper.AddHttp(ImageTextBox.Text.Trim()), ref rez, ref sizeX, ref sizeY))
            {
                zErr = Words.PictureNotFoundCheckURL;
                return false;
            }
        }
        else if (!CheckLocalFileImage(ImageTextBox.Text.Trim(), ref rez, ref sizeX, ref sizeY))
        {
            zErr = Words.CannotAddPicture;
            return false;
        }

        if (WebsiteCheckBox.IsChecked.GetValueOrDefault() && !WebsiteTextBox.Text.Trim().IsNullOrEmpty() && !CheckUrl(AppHelper.AddHttp(WebsiteTextBox.Text.Trim())))
        {
            zErr = Words.WebsiteNotFoundCheckURL;
            return false;
        }

        Engine uploadPhuse = AppHelper.UploadPhuse;
        string headerGroup = Settings.Default.HeaderGroup;
        int num = CatBox.SelectedIndex + 1;
        string subCats = GetSubCats((byte)CatBox.SelectedIndex, sIncludeHCat: true);
        string sUrl = (WebsiteCheckBox.IsChecked.GetValueOrDefault() ? AppHelper.AddHttp(WebsiteTextBox.Text.Trim()) : "");
        string nZBGroup = Settings.Default.NZBGroup;
        RSACryptoServiceProvider key = UserKeyHelper.GetKey();
        string sHashMsgId = AppHelper.CreateMsgId();
        byte[] avatar = AppHelper.GetAvatar();
        NntpSettings settings = HeaderSettings;
        string postString = "";
        return Spots.CreateSpot(uploadPhuse, headerGroup, TitelTextBox.Text, DescTextBox.Text, (byte)num, subCats, sUrl, "nl", sizeX, sizeY, NzbTextBox.Text, EncryptedNzbTextBox.Text, PosterTextBox.Text, TagTextBox.Text, nZBGroup, key, sHashMsgId, rez, avatar, !Settings.Default.ExternalSigning, ref settings, ref zErr, ref postString);
    }

    private void GetCats(AppHelper.SpotCategory hCat, int hType)
    {
        string[] array = new string[3]
        {
            "b",
            "c",
            "d"
        };
        int upperBound = array.GetUpperBound(0);
        for (int i = 0; i <= upperBound; i = checked(i + 1))
        {
            List<string> list = new List<string>();
            List<int> list2 = new List<int>();
            if (hCat == AppHelper.SpotCategory.Video)
            {
                switch (i)
                {
                    case 0:
                    {
                        if (hType == 2)
                        {
                            list2.Add(3);
                            list2.Add(10);
                            break;
                        }

                        int num4 = 0;
                        do
                        {
                            if (num4 != 10)
                            {
                                list2.Add(num4);
                            }

                            num4++;
                        }
                        while (num4 <= 15);
                        break;
                    }

                    case 1:
                        if (hType == 2)
                        {
                            list2.Add(2);
                            list2.Add(4);
                            list2.Add(12);
                            list2.Add(13);
                            list2.Add(14);
                            list2.Add(15);
                        }

                        break;
                    case 2:
                        switch (hType)
                        {
                            case 0:
                            case 1:
                            {
                                int num2 = 0;
                                do
                                {
                                    list2.Add(num2);
                                    num2++;
                                }
                                while (num2 <= 22);
                                list2.Add(27);
                                list2.Add(28);
                                list2.Add(29);
                                list2.Add(32);
                                list2.Add(33);
                                list2.Add(41);
                                list2.Add(50);
                                list2.Add(51);
                                list2.Add(54);
                                break;
                            }

                            case 2:
                            {
                                list2.Add(1);
                                list2.Add(5);
                                list2.Add(7);
                                list2.Add(9);
                                list2.Add(15);
                                list2.Add(16);
                                list2.Add(17);
                                list2.Add(21);
                                list2.Add(30);
                                list2.Add(31);
                                int num3 = 33;
                                do
                                {
                                    list2.Add(num3);
                                    num3++;
                                }
                                while (num3 <= 60);
                                break;
                            }

                            case 3:
                            {
                                list2.Add(23);
                                list2.Add(24);
                                list2.Add(25);
                                list2.Add(26);
                                int num = 72;
                                do
                                {
                                    list2.Add(num);
                                    num++;
                                }
                                while (num <= 90);
                                break;
                            }
                        }

                        break;
                }
            }

            if (list2.Count == 0)
            {
                for (int j = 0; j < 100; j++)
                {
                    list2.Add(j);
                }
            }

            foreach (int item in list2)
            {
                int num5 = Convert.ToInt32(item);
                string text = ((!(hCat == AppHelper.SpotCategory.Video && hType == 2 && i == 1)) ? AppHelper.TranslateCat(hCat, array[i] + num5.ToStringSafely(), strict: true) : AppHelper.TranslateCat(AppHelper.SpotCategory.Movies, array[i] + num5.ToStringSafely(), strict: true));
                if (!text.IsNullOrEmpty())
                {
                    list.Add(text + "\t" + num5.ToStringSafely());
                }
            }

            foreach (string item2 in list)
            {
                ListBoxItem listBoxItem = new ListBoxItem();
                System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox();
                string content = item2.Split('\t')[0];
                checkBox.Content = content;
                listBoxItem.Content = checkBox;
                listBoxItem.Tag = double.Parse(item2.Split('\t')[1]);
                switch (i)
                {
                    case 0:
                        Cat1.Items.Add(listBoxItem);
                        break;
                    case 1:
                        Cat2.Items.Add(listBoxItem);
                        break;
                    case 2:
                        Cat3.Items.Add(listBoxItem);
                        break;
                }
            }
        }

        if (Cat1.Items.Count == 0)
        {
            Cat1.Visibility = Visibility.Hidden;
            CatLab1.Visibility = Visibility.Hidden;
        }
        else
        {
            Cat1.IsEnabled = true;
            Cat1.Visibility = Visibility.Visible;
            CatLab1.Content = AppHelper.TranslateCatDesc(hCat, "b0");
            CatLab1.Visibility = Visibility.Visible;
        }

        if (Cat2.Items.Count == 0)
        {
            Cat2.Visibility = Visibility.Hidden;
            CatLab2.Visibility = Visibility.Hidden;
        }
        else
        {
            Cat2.IsEnabled = true;
            Cat2.Visibility = Visibility.Visible;
            CatLab2.Content = AppHelper.TranslateCatDesc(hCat, "c0");
            CatLab2.Visibility = Visibility.Visible;
        }

        if (Cat3.Items.Count == 0)
        {
            Cat3.Visibility = Visibility.Hidden;
            CatLab3.Visibility = Visibility.Hidden;
            return;
        }

        Cat3.IsEnabled = true;
        Cat3.Visibility = Visibility.Visible;
        CatLab3.Content = AppHelper.TranslateCatDesc(hCat, "d0");
        CatLab3.Visibility = Visibility.Visible;
    }

    private string GetSubCats(byte hCat, bool sIncludeHCat)
    {
        string text = null;
        try
        {
            if (sIncludeHCat)
            {
                byte b = Convert.ToByte(((FrameworkElement)SCatBox.SelectedItem).Tag);
                text = ((b <= 9) ? ("A0" + b) : ("A" + b));
                if (hCat == 0)
                {
                    ComboBoxItem comboBoxItem = (ComboBoxItem)Cat2Box.SelectedItem;
                    if (comboBoxItem != null)
                    {
                        byte num = Convert.ToByte(comboBoxItem.Tag);
                        if (num == 1)
                        {
                            text += "B04D11";
                        }

                        if (num == 3)
                        {
                            bool flag = false;
                            foreach (ListBoxItem item in (IEnumerable)Cat3.Items)
                            {
                                if (((ToggleButton)item.Content).IsChecked.GetValueOrDefault())
                                {
                                    switch (Convert.ToByte(item.Tag))
                                    {
                                        case 23:
                                            flag = true;
                                            text += "D75";
                                            break;
                                        case 24:
                                            flag = true;
                                            text += "D74";
                                            break;
                                        case 25:
                                            flag = true;
                                            text += "D73";
                                            break;
                                        case 26:
                                            flag = true;
                                            text += "D72";
                                            break;
                                    }
                                }
                            }

                            if (!flag)
                            {
                                text += "D23D75";
                            }
                        }
                    }
                }
            }

            foreach (ListBoxItem item2 in (IEnumerable)Cat1.Items)
            {
                if (((ToggleButton)item2.Content).IsChecked.GetValueOrDefault())
                {
                    text += ((double.Parse(RuntimeHelpers.GetObjectValue(item2.Tag).ToString()) <= 9.0) ? ("B0" + item2.Tag.ToStringSafely()) : ("B" + item2.Tag.ToStringSafely()));
                }
            }

            foreach (ListBoxItem item3 in (IEnumerable)Cat2.Items)
            {
                if (((ToggleButton)item3.Content).IsChecked.GetValueOrDefault())
                {
                    text += ((double.Parse(RuntimeHelpers.GetObjectValue(item3.Tag).ToString()) <= 9.0) ? ("C0" + item3.Tag.ToStringSafely()) : ("C" + item3.Tag.ToStringSafely()));
                }
            }

            foreach (ListBoxItem item4 in (IEnumerable)Cat3.Items)
            {
                if (((ToggleButton)item4.Content).IsChecked.GetValueOrDefault())
                {
                    text += ((double.Parse(RuntimeHelpers.GetObjectValue(item4.Tag).ToString()) <= 9.0) ? ("D0" + item4.Tag.ToStringSafely()) : ("D" + item4.Tag.ToStringSafely()));
                }
            }

            if (sIncludeHCat && hCat < 2)
            {
                ComboBoxItem comboBoxItem2 = (ComboBoxItem)Cat2Box.SelectedItem;
                if (comboBoxItem2 != null)
                {
                    text = text + "Z0" + Convert.ToByte(comboBoxItem2.Tag).ToStringSafely();
                }
            }

            return text;
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return "";
        }
    }

    private static ComboBoxItem NewItem(string sName, int iTag)
    {
        return new ComboBoxItem
        {
            Tag = iTag,
            Content = sName
        };
    }

    private void UpdateBoxes()
    {
        Cat1.Items.Clear();
        Cat1.IsEnabled = false;
        Cat2.Items.Clear();
        Cat2.IsEnabled = false;
        Cat3.Items.Clear();
        Cat3.IsEnabled = false;
        if (CatBox.SelectedIndex >= 0 && (Cat2Box.SelectedIndex >= 0 || !Cat2Box.IsEnabled))
        {
            GetCats((AppHelper.SpotCategory)(byte)(CatBox.SelectedIndex + 1), Cat2Box.SelectedIndex);
        }
    }

    private void NzbPathBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = Words.AddNZB,
            InitialDirectory = _desktopFolder,
            Filter = Words.NZBFiles,
            FilterIndex = 1,
            RestoreDirectory = true,
            CheckFileExists = true,
            ShowReadOnly = false,
            DefaultExt = "nzb",
            Multiselect = false
        };
        openFileDialog.InitialDirectory = ((!Settings.Default.LastFolder.IsNullOrWhiteSpace()) ? Settings.Default.LastFolder : _desktopFolder);
        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            NzbTextBox.Text = openFileDialog.FileName;
            Settings.Default.LastFolder = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
            Settings.Default.Save();
        }
    }

    private void PostButton_Click(object sender, RoutedEventArgs e)
    {
        base.Cursor = System.Windows.Input.Cursors.Wait;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        PostButton.IsEnabled = false;
        base.IsEnabled = false;
        UpdateLayout();
        base.Dispatcher.BeginInvoke(new Action(DoButton), DispatcherPriority.Background);
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        string zErr = null;
        bool flag = false;
        try
        {
            byte[] rez = null;
            long sizeX = 0L;
            long sizeY = 0L;
            if (!CheckValues(out zErr))
            {
                return;
            }

            string text = ImageTextBox.Text.Trim();
            if (!System.IO.File.Exists(text))
            {
                if (!CheckRemoteImage(AppHelper.AddHttp(text), ref rez, ref sizeX, ref sizeY))
                {
                    zErr = Words.PictureNotFoundCheckURL;
                    return;
                }
            }
            else if (!CheckLocalFileImage(text, ref rez, ref sizeX, ref sizeY))
            {
                zErr = Words.CannotAddPicture;
                return;
            }

            if (WebsiteCheckBox.IsChecked.GetValueOrDefault() && !WebsiteTextBox.Text.Trim().IsNullOrEmpty() && !CheckUrl(AppHelper.AddHttp(WebsiteTextBox.Text.Trim())))
            {
                zErr = Words.WebsiteNotFoundCheckURL;
                return;
            }

            Engine uploadPhuse = AppHelper.UploadPhuse;
            string headerGroup = Settings.Default.HeaderGroup;
            int num = CatBox.SelectedIndex + 1;
            string subCats = GetSubCats((byte)CatBox.SelectedIndex, sIncludeHCat: true);
            string sUrl = AppHelper.AddHttp(WebsiteTextBox.Text.Trim());
            string nZBGroup = Settings.Default.NZBGroup;
            RSACryptoServiceProvider key = UserKeyHelper.GetKey();
            string sHashMsgId = AppHelper.CreateMsgId();
            byte[] avatar = AppHelper.GetAvatar();
            NntpSettings settings = HeaderSettings;
            string postString = "221\r\n";
            string sTitle = Words.Preview + ": " + TitelTextBox.Text.Trim();
            if (!Spots.CreateSpot(uploadPhuse, headerGroup, sTitle, DescTextBox.Text, (byte)num, subCats, sUrl, "nl", sizeX, sizeY, NzbTextBox.Text, EncryptedNzbTextBox.Text, PosterTextBox.Text, TagTextBox.Text, nZBGroup, key, sHashMsgId, rez, avatar, !Settings.Default.ExternalSigning, ref settings, ref zErr, ref postString, isFakeCreation: true))
            {
                return;
            }

            SpotEx spotEx = null;
            if (Spots.GetSpot(null, null, 0L, null, ref spotEx, AppHelper.HeaderSettings(bIncludePosition: false), ref zErr, postString))
            {
                spotEx.PreviewImage = AppHelper.WriteBytesToTmpFile(rez, ".tmp");
                spotEx.IsPreview = true;
                FileCacheManager.PreviewData = new KeyValuePair<string, SpotEx>(spotEx.MessageId, spotEx);
                DispatcherHelper.RunAsync(delegate
                {
                    Sys.MainWindow.OpenSpot(SpotRowViewModel.InitializeNewSpotRow(spotEx), null, saveParrentTab: true, isPreview: true);
                });
                flag = true;
            }
        }
        finally
        {
            if (!flag && !zErr.IsNullOrEmpty())
            {
                AppHelper.Error(zErr);
            }
        }
    }

    private void ImageBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = Words.AddPictures,
            InitialDirectory = _desktopFolder,
            Filter = Words.FilterToPicture,
            FilterIndex = 1,
            RestoreDirectory = true,
            CheckFileExists = true,
            ShowReadOnly = false,
            DefaultExt = "jpg",
            Multiselect = false
        };
        openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            ImageTextBox.Text = openFileDialog.FileName;
        }
    }

    private void ShowPictureOnFormAsync(string path)
    {
        Task.Run(delegate
        {
            path = path.Trim();
            byte[] rez = null;
            long sizeX = 0L;
            long sizeY = 0L;
            if (!System.IO.File.Exists(path))
            {
                if (path.IndexOf(":", StringComparison.Ordinal) <= 1 || !CheckRemoteImage(AppHelper.AddHttp(path), ref rez, ref sizeX, ref sizeY))
                {
                    return;
                }
            }
            else if (!CheckLocalFileImage(path, ref rez, ref sizeX, ref sizeY))
            {
                return;
            }

            BitmapSource img = (BitmapSource)new ImageSourceConverter().ConvertFrom(rez);
            DispatcherHelper.UIDispatcher.Invoke(delegate
            {
                ImagePreview.Source = img;
            });
        });
    }

    private void Cat2Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SCatBox.Items.Clear();
        SCatBox.IsEnabled = true;
        Label1.Content = AppHelper.TranslateCatDesc((AppHelper.SpotCategory)(CatBox.SelectedIndex + 1), "a0");
        int num = 0;
        ComboBoxItem comboBoxItem = (ComboBoxItem)Cat2Box.SelectedItem;
        if (comboBoxItem != null)
        {
            num = Convert.ToByte(comboBoxItem.Tag);
        }

        for (long num2 = 0L; num2 <= 100; num2++)
        {
            if (CatBox.SelectedIndex == 0)
            {
                switch (num)
                {
                    case 2:
                        if (num2 != 5 && num2 != 11)
                        {
                            continue;
                        }

                        break;
                    default:
                        if (num2 == 5 || num2 == 11 || num2 == 12 || num2 == 13)
                        {
                            continue;
                        }

                        break;
                    case 4:
                        if (num2 != 12 && num2 != 13)
                        {
                            continue;
                        }

                        break;
                }
            }

            string text = AppHelper.TranslateCat((AppHelper.SpotCategory)(CatBox.SelectedIndex + 1), "a" + num2.ToStringSafely(), strict: true);
            if (text != null && !text.IsNullOrEmpty())
            {
                ComboBoxItem newItem = new ComboBoxItem
                {
                    Tag = num2,
                    Content = text
                };
                SCatBox.Items.Add(newItem);
            }
        }

        UpdateBoxes();
    }

    private void CatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Cat2Box.Items.Clear();
        SCatBox.Items.Clear();
        SCatBox.IsEnabled = false;
        Label1.Content = AppHelper.TranslateCatDesc((AppHelper.SpotCategory)(CatBox.SelectedIndex + 1), "a0");
        switch (CatBox.SelectedIndex)
        {
            case 0:
                Cat2Box.IsEnabled = true;
                Cat2Box.Items.Add(NewItem(Categories.CatFilms, 0));
                Cat2Box.Items.Add(NewItem(Categories.CatSeries, 1));
                Cat2Box.Items.Add(NewItem(Categories.CatBooks, 2));
                Cat2Box.Items.Add(NewItem(Categories.CatErotica, 3));
                Cat2Box.Items.Add(NewItem(Categories.CatImages, 4));
                break;
            case 1:
                Cat2Box.IsEnabled = true;
                Cat2Box.Items.Add(NewItem(Categories.MGAlbum, 0));
                Cat2Box.Items.Add(NewItem(Categories.MGLiveset, 1));
                Cat2Box.Items.Add(NewItem(Categories.MGPodcast, 2));
                Cat2Box.Items.Add(NewItem(Categories.MGAudiobook, 3));
                break;
            default:
                Cat2Box.IsEnabled = false;
                Cat2Box_SelectionChanged(RuntimeHelpers.GetObjectValue(sender), e);
                break;
        }

        UpdateBoxes();
    }

    private void Toevoegen_Initialized(object sender, EventArgs e)
    {
        PosterTextBox.Text = AppHelper.StripNonAlphaNumericCharacters(Settings.Default.Nickname);
        TagTextBox.Text = AppHelper.StripNonAlphaNumericCharacters(Settings.Default.Tagname);
        WebsiteTextBox.Text = DefaultWebsite;
    }

    private void TagTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!char.IsLetterOrDigit(Convert.ToChar(e.Text)))
        {
            e.Handled = true;
        }
    }

    private void TagTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.Equals(TagTextBox.Text, AppHelper.StripNonAlphaNumericCharacters(TagTextBox.Text), StringComparison.OrdinalIgnoreCase))
        {
            TagTextBox.Text = AppHelper.StripNonAlphaNumericCharacters(TagTextBox.Text);
            TagTextBox.SelectionStart = TagTextBox.Text.Length;
        }
    }

    private void PosterTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!char.IsLetterOrDigit(Convert.ToChar(e.Text)))
        {
            e.Handled = true;
        }
    }

    private void PosterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.Equals(PosterTextBox.Text, AppHelper.StripNonAlphaNumericCharacters(PosterTextBox.Text), StringComparison.OrdinalIgnoreCase))
        {
            PosterTextBox.Text = AppHelper.StripNonAlphaNumericCharacters(PosterTextBox.Text);
            PosterTextBox.SelectionStart = PosterTextBox.Text.Length;
        }
    }

    private void ImageTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ShowPictureOnFormAsync(ImageTextBox.Text);
    }

    private void EncryptedNzbBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = Words.AddNZB,
            InitialDirectory = _desktopFolder,
            Filter = Words.NZBFiles,
            FilterIndex = 1,
            RestoreDirectory = true,
            CheckFileExists = true,
            ShowReadOnly = false,
            DefaultExt = "nzb",
            Multiselect = false
        };
        openFileDialog.InitialDirectory = ((!Settings.Default.LastFolder.Trim().IsNullOrEmpty()) ? Settings.Default.LastFolder : _desktopFolder);
        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            EncryptedNzbTextBox.Text = openFileDialog.FileName;
            Settings.Default.LastFolder = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
            Settings.Default.Save();
        }
    }

    private void DummyNzbButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_dummyNzbPath.IsNullOrEmpty())
        {
            _dummyNzbPath = AppHelper.GetTempFileName("nzb");
        }

        string dummy = Spotnet.Properties.Resources.dummy;
        System.IO.File.WriteAllText(_dummyNzbPath, dummy);
        NzbTextBox.Text = _dummyNzbPath;
    }

    private void WebsiteCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        WebsiteTextBox.IsEnabled = true;
    }

    private void WebsiteCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        WebsiteTextBox.IsEnabled = false;
        WebsiteTextBox.Text = DefaultWebsite;
    }

    private void TitelTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!WebsiteCheckBox.IsChecked.GetValueOrDefault())
        {
            WebsiteTextBox.Text = DefaultWebsite;
        }
    }

    private void EncryptedNzbCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        EncryptedNzbBrowseButton.IsEnabled = true;
        EncryptedNzbTextBox.IsEnabled = true;
    }

    private void EncryptedNzbCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        EncryptedNzbBrowseButton.IsEnabled = false;
        EncryptedNzbTextBox.IsEnabled = false;
        EncryptedNzbTextBox.Text = "";
    }
}