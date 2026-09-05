using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class AdvancedSettings : MetroWindow, INotifyPropertyChanged
{
    private string _downloaderSavedState;
    private static int _headerItemIndex;
    private List<KeyValuePair<string, UserControl>> _settingsDictionary;
    public List<KeyValuePair<string, UserControl>> SettingsDictionary
    {
        get
        {
            List<KeyValuePair<string, UserControl>> list = _settingsDictionary;
            if (list == null)
            {
                List<KeyValuePair<string, UserControl>> obj = new List<KeyValuePair<string, UserControl>>
                {
                    new KeyValuePair<string, UserControl>(Words.MenuAdvDownloads, null),
                    new KeyValuePair<string, UserControl>(Words.MenuAdvDownloadsAdvanced, null),
                    new KeyValuePair<string, UserControl>(Words.MenuAdvCommon, null),
                    new KeyValuePair<string, UserControl>(Words.MenuAdvSpotsList, null),
                    new KeyValuePair<string, UserControl>(Words.MenuAdvTabs, null),
                    new KeyValuePair<string, UserControl>(Words.MenuAdvDatabase, null),
                    new KeyValuePair<string, UserControl>("Spotnet Remote", null)
                };
                List<KeyValuePair<string, UserControl>> list2 = obj;
                _settingsDictionary = obj;
                list = list2;
            }

            return list;
        }
    }

    public List<string> SettingHeaders => SettingsDictionary.Select((KeyValuePair<string, UserControl> p) => p.Key).ToList();

    public int HeaderItemIndex
    {
        get
        {
            return _headerItemIndex;
        }

        set
        {
            _headerItemIndex = value;
            OnPropertyChanged("HeaderItemIndex");
            UpdateContentGrid(value);
        }
    }

    public bool IsDownloaderSettingsEnabled => Settings.Default.DownloadAction <= 1;

    public event PropertyChangedEventHandler PropertyChanged;
    private event Action<string> DownloadFolderChanged;
    public AdvancedSettings()
    {
        InitializeComponent();
        base.DataContext = this;
        HeaderItemIndex = _headerItemIndex;
    }

    private void UpdateContentGrid(int selectedIndex)
    {
        UserControl userControl = null;
        ContentGrid.Children.Clear();
        if (SettingsDictionary[selectedIndex].Value == null)
        {
            switch (selectedIndex)
            {
                case 0:
                {
                    Action<string> onDownloadFolderChanged = delegate (string dir)
                    {
                        this.DownloadFolderChanged?.Invoke(dir);
                    };
                    userControl = new SettingsForDownload(onDownloadFolderChanged);
                    break;
                }

                case 1:
                    userControl = new SettingsForAdvancedDownload();
                    DownloadFolderChanged += delegate (string s)
                    {
                        ((SettingsForAdvancedDownload)userControl).DownloadFolderChanged?.Invoke(s);
                    };
                    break;
                case 2:
                    userControl = new SettingsForCommon();
                    break;
                case 3:
                    userControl = new SettingsForSpotsList();
                    break;
                case 4:
                    userControl = new SettingsForTabs();
                    break;
                case 5:
                    userControl = new SettingsForDatabase();
                    break;
                case 6:
                    userControl = new SettingsForRemote();
                    break;
            }

            SettingsDictionary[selectedIndex] = new KeyValuePair<string, UserControl>(SettingsDictionary[selectedIndex].Key, userControl);
        }
        else
        {
            userControl = SettingsDictionary[selectedIndex].Value;
        }

        if (userControl != null)
        {
            ContentGrid.IsEnabled = IsDownloaderSettingsEnabled || selectedIndex > 1;
            ContentGrid.Children.Add(userControl);
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SaveAllSettings()
    {
        bool flag = false;
        for (int i = 0; i < SettingsDictionary.Count; i++)
        {
            KeyValuePair<string, UserControl> keyValuePair = SettingsDictionary[i];
            if (keyValuePair.Value != null && !((IAdvancedSettingsControl)keyValuePair.Value).VerifyFields())
            {
                flag = true;
                HeaderItemIndex = i;
                break;
            }
        }

        if (flag)
        {
            return false;
        }

        CheckDownloaderRestartRequiredStep1();
        bool externalNzbGet = Settings.Default.ExternalNzbGet;
        for (int j = 0; j < SettingsDictionary.Count; j++)
        {
            KeyValuePair<string, UserControl> keyValuePair2 = SettingsDictionary[j];
            if (keyValuePair2.Value != null && !((IAdvancedSettingsControl)keyValuePair2.Value).Save())
            {
                flag = true;
                HeaderItemIndex = j;
                break;
            }
        }

        if (CheckDownloaderRestartRequiredStep2())
        {
            bool flag2 = true;
            if (!externalNzbGet)
            {
                flag2 = Sys.Downloader.ShutdownProcessAsync().Result;
            }

            if (!flag2)
            {
                AppHelper.Error("Failed to restart downloader, check logs for details");
            }
            else
            {
                AppHelper.ResetAllUsenetConnections();
                Sys.MainWindow.InitializeDownloader();
                Sys.Downloader.StartProcessAsync();
            }
        }

        Settings.Default.Save();
        return !flag;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveAllSettings())
        {
            string originalText = Words.Save;
            SaveButton.Content = "✓ Opgeslagen";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (s, ev) =>
            {
                SaveButton.Content = originalText;
                timer.Stop();
            };
            timer.Start();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveAllSettings())
        {
            Close();
        }
    }

    private void CheckDownloaderRestartRequiredStep1()
    {
        _downloaderSavedState = GetDownloadsStarting();
    }

    private string GetDownloadsStarting()
    {
        return $"{Settings.Default.DownloadFolder}{Settings.Default.ExternalNzbGet}{Settings.Default.NzbGetControlIP}{Settings.Default.NzbGetControlPort}{Settings.Default.NzbGetControlUsername}{Settings.Default.NzbGetControlPassword}{Settings.Default.NzbGetDestDir}{Settings.Default.NzbGetInterDir}{Settings.Default.NzbGetQueueDir}{Settings.Default.NzbGetServer1Host}{Settings.Default.NzbGetServer1Port}{Settings.Default.NzbGetServer1Username}{Settings.Default.NzbGetServer1Password}{Settings.Default.NzbGetServer1Encryption}{Settings.Default.UseSocksProxy}";
    }

    private bool CheckDownloaderRestartRequiredStep2()
    {
        return _downloaderSavedState != GetDownloadsStarting();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
