using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using NLog;
using System.IO;
using Avalonia.Media.Imaging;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;

namespace Spotnet.Mac.ViewModels;

public sealed class SpotDetailViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly SpotDatabaseService _dbService;
    private readonly NzbService? _nzbService;

    private SpotItem? _spot;
    private string _description = "";
    private bool _isLoading;
    private string _statusMessage = "";
    private Bitmap? _posterImage;

    public SpotItem? Spot
    {
        get => _spot;
        set
        {
            if (SetProperty(ref _spot, value))
            {
                OnPropertyChanged(nameof(HasSpot));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Poster));
                OnPropertyChanged(nameof(CategoryIcon));
                OnPropertyChanged(nameof(CategoryName));
                OnPropertyChanged(nameof(FormattedDate));
                OnPropertyChanged(nameof(FormattedSize));
                OnPropertyChanged(nameof(MsgId));
                OnPropertyChanged(nameof(WebsiteUrl));
                OnPropertyChanged(nameof(SpamReportCount));
                OnPropertyChanged(nameof(IsFavorite));
                OnPropertyChanged(nameof(FavoriteStar));
                OnPropertyChanged(nameof(FavoriteTooltip));
                OnPropertyChanged(nameof(FavoriteForeground));

                PosterImage = null;
                RebuildFields();

                if (value != null)
                {
                    _ = LoadSpotDetailsAsync(value);
                }
                else
                {
                    Description = "";
                    Comments.Clear();
                    SpamReports.Clear();
                    SpamReportCount = 0;
                    OnPropertyChanged(nameof(HasSpamReports));
                }
            }
        }
    }

    public bool HasSpot => _spot != null;
    public string Title => _spot?.Subject ?? "Selecteer een spot";
    public string Poster => _spot?.SenderWithId ?? "";
    public string CategoryIcon => _spot?.CategoryIcon ?? "";
    public string CategoryName => _spot?.CategoryName ?? "";
    public string FormattedDate => _spot?.FormattedDate ?? "";
    public string FormattedSize => _spot?.FormattedSize ?? "";
    public string MsgId => _spot?.MsgId ?? "";

    /// <summary>
    /// The subcategory rows Windows lists in the spot panel — Formaat, Bron, Bitrate,
    /// Taal, Genre — built from the spot's own cats, so a spot only shows the rows it
    /// actually carries.
    /// </summary>
    public ObservableCollection<SpotDetailField> Fields { get; } = new();

    /// <summary>The "Website" row: the search link Windows offers for the title.</summary>
    public string WebsiteUrl => _spot == null
        ? ""
        : "http://www.google.nl/search?q=" + Uri.EscapeDataString(_spot.Subject);

    /// <summary>The "Meldingen" row: how many spam reports this spot has.</summary>
    private int _spamReportCount;
    public int SpamReportCount
    {
        get => _spamReportCount;
        private set => SetProperty(ref _spamReportCount, value);
    }

    /// <summary>Individual spam reports recorded against this spot.</summary>
    public ObservableCollection<SpamReportItem> SpamReports { get; } = new();
    public bool HasSpamReports => SpamReports.Count > 0;

    private void RebuildFields()
    {
        Fields.Clear();
        if (_spot == null) return;

        Fields.Add(new SpotDetailField("Categorie", _spot.CategoryName));

        // cats reads "2 2a2 2b0 2c8 2d13 2z0": the bare category, then one token per
        // subcategory. Windows shows the first of each letter, in a b c d order.
        var seen = new HashSet<char>();
        foreach (string token in _spot.Cats.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 3) continue;

            char letter = token[1];
            if (letter is not (>= 'a' and <= 'z')) continue;
            if (!int.TryParse(token.AsSpan(2), out int code)) continue;
            if (letter == 'z' || !seen.Add(letter)) continue;

            string label = SpotCategories.DescribeLetter(_spot.Category, letter);
            string value = SpotCategories.Translate(_spot.Category, letter, code);
            if (label.Length > 0 && value.Length > 0)
            {
                Fields.Add(new SpotDetailField(label, value));
            }
        }

        Fields.Add(new SpotDetailField("Omvang", _spot.FormattedSize));
        Fields.Add(new SpotDetailField("Afzender", _spot.SenderWithId));
        Fields.Add(new SpotDetailField("Meldingen", SpamReportCount.ToString()));
    }

    public Bitmap? PosterImage
    {
        get => _posterImage;
        private set => SetProperty(ref _posterImage, value);
    }

    public bool HasPoster => PosterImage != null;

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value))
            {
                RebuildWebLinks();
            }
        }
    }

    public ObservableCollection<string> DetectedWebLinks { get; } = new();
    public bool HasDetectedWebLinks => DetectedWebLinks.Count > 0;

    private void RebuildWebLinks()
    {
        DetectedWebLinks.Clear();
        if (!string.IsNullOrWhiteSpace(_description))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = System.Text.RegularExpressions.Regex.Matches(_description, @"https?://[^\s<>'""\)\]]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (Spotnet.Helpers.WebLinkHelper.TryResolveWebLink(match.Value, out string? valid) && !string.IsNullOrEmpty(valid))
                {
                    if (seen.Add(valid))
                    {
                        DetectedWebLinks.Add(valid);
                    }
                }
            }
        }
        OnPropertyChanged(nameof(HasDetectedWebLinks));
    }

    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private string _newCommentText = "";
    public string NewCommentText
    {
        get => _newCommentText;
        set
        {
            if (SetProperty(ref _newCommentText, value)) OnPropertyChanged(nameof(CommentPreview));
        }
    }

    private string _senderNickname = "Spotter";
    public string SenderNickname { get => _senderNickname; set => SetProperty(ref _senderNickname, value); }

    private bool _isPostingComment;
    public bool IsPostingComment { get => _isPostingComment; set => SetProperty(ref _isPostingComment, value); }

    // ── Comment composer ──────────────────────────────────────────────────────
    // Mirrors the Windows reply form: a preview toggle, an Afzender field, a B/I/U/link/
    // colour toolbar over the Reactie box, a smiley picker and a Reageer button.

    private int _commentSelectionStart;
    public int CommentSelectionStart { get => _commentSelectionStart; set => SetProperty(ref _commentSelectionStart, value); }

    private int _commentSelectionEnd;
    public int CommentSelectionEnd { get => _commentSelectionEnd; set => SetProperty(ref _commentSelectionEnd, value); }

    private bool _showPreview;
    public bool ShowPreview { get => _showPreview; set => SetProperty(ref _showPreview, value); }

    private bool _showSmileys;
    public bool ShowSmileys { get => _showSmileys; set => SetProperty(ref _showSmileys, value); }

    private string _commentColor = "000000";
    public string CommentColor { get => _commentColor; set => SetProperty(ref _commentColor, value); }

    /// <summary>The comment as it will read once posted, with UBB and smileys resolved.</summary>
    public string CommentPreview => SpotMarkup.ToPlainText(NewCommentText);

    public IReadOnlyList<KeyValuePair<string, string>> Smileys => SpotMarkup.SmileyList;

    public ICommand ReloadCommentsCommand { get; }
    public ICommand ToggleCommentPreviewCommand { get; }
    public ICommand ToggleSmileysCommand { get; }
    public ICommand WrapCommentCommand { get; }
    public ICommand InsertSmileyCommand { get; }

    /// <summary>
    /// Wraps the selected text in a UBB tag, or drops an empty pair at the caret when
    /// nothing is selected — the same thing the Windows toolbar buttons do.
    /// </summary>
    private void WrapSelection(string open, string close)
    {
        string text = NewCommentText;
        int start = Math.Clamp(Math.Min(CommentSelectionStart, CommentSelectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(CommentSelectionStart, CommentSelectionEnd), 0, text.Length);

        NewCommentText = text[..start] + open + text[start..end] + close + text[end..];
        CommentSelectionStart = CommentSelectionEnd = start + open.Length + (end - start) + close.Length;
    }

    private void InsertAtCaret(string snippet)
    {
        string text = NewCommentText;
        int at = Math.Clamp(Math.Max(CommentSelectionStart, CommentSelectionEnd), 0, text.Length);

        NewCommentText = text[..at] + snippet + text[at..];
        CommentSelectionStart = CommentSelectionEnd = at + snippet.Length;
    }

    private string _commentPostStatus = "";
    public string CommentPostStatus { get => _commentPostStatus; set => SetProperty(ref _commentPostStatus, value); }

    public ObservableCollection<CommentItem> Comments { get; } = new();

    public ICommand CopyMsgIdCommand { get; }
    public ICommand DownloadNzbCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand PostCommentCommand { get; }
    public ICommand OpenWebsiteCommand { get; }
    public ICommand OpenWebLinkCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }

    public bool IsFavorite => _spot?.IsFavorite ?? false;
    public string FavoriteStar => IsFavorite ? "★" : "☆";
    public string FavoriteTooltip => IsFavorite ? "Verwijderen uit Favorieten" : "Toevoegen aan Favorieten";
    public string FavoriteForeground => IsFavorite ? "#FFD700" : "#888888";

    public event Action<string>? RequestNzbDownload;
    public event Action? RequestClose;

    /// <summary>
    /// Raised after every NZB fetch so the Downloads tab can record it.
    /// Parameters: spot, success, nzbPath, message, downloadJob (null when not integrated), spotDescription.
    /// </summary>
    public event Action<SpotItem, bool, string?, string, Network.NzbDownloadJob?, string?>? NzbFetched;

    private readonly CommentService? _commentService;
    private readonly SpotBodyService? _bodyService;
    private readonly Spotnet.Platform.IExternalLauncher _launcher = new Spotnet.Mac.Platform.MacExternalLauncher();

    public SpotDetailViewModel(SpotDatabaseService dbService, NzbService? nzbService = null,
                               CommentService? commentService = null, SpotBodyService? bodyService = null)
    {
        _dbService = dbService;
        _nzbService = nzbService;
        _commentService = commentService;
        _bodyService = bodyService;

        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

        ToggleFavoriteCommand = new RelayCommand(async () =>
        {
            if (_spot == null || string.IsNullOrWhiteSpace(_spot.MsgId)) return;
            bool newState = !_spot.IsFavorite;
            _spot.IsFavorite = newState;
            if (newState)
            {
                await _dbService.AddFavoriteAsync(_spot.MsgId);
            }
            else
            {
                await _dbService.RemoveFavoriteAsync(_spot.MsgId);
            }
            OnPropertyChanged(nameof(IsFavorite));
            OnPropertyChanged(nameof(FavoriteStar));
            OnPropertyChanged(nameof(FavoriteTooltip));
            OnPropertyChanged(nameof(FavoriteForeground));
        });

        OpenWebsiteCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(WebsiteUrl))
            {
                _launcher.OpenUrl(WebsiteUrl);
            }
        });

        OpenWebLinkCommand = new RelayCommand(param =>
        {
            if (param is string url && !string.IsNullOrEmpty(url))
            {
                _launcher.OpenUrl(url);
            }
        });

        CopyMsgIdCommand = new RelayCommand(() =>
        {
            if (_spot != null)
            {
                StatusMessage = "Message-ID gekopieerd!";
            }
        });

        DownloadNzbCommand = new RelayCommand(async () =>
        {
            if (_spot == null) return;

            if (_nzbService != null)
            {
                StatusMessage = "NZB ophalen van Usenet...";
                var (success, path, msg, job) = await _nzbService.DownloadAsync(_spot);
                StatusMessage = msg;
                NzbFetched?.Invoke(_spot, success, path, msg, job, Description);
            }
            else
            {
                RequestNzbDownload?.Invoke(_spot.MsgId);
            }
        });

        ReloadCommentsCommand = new RelayCommand(async () =>
        {
            if (_spot != null) await LoadSpotDetailsAsync(_spot);
        });

        ToggleCommentPreviewCommand = new RelayCommand(() => ShowPreview = !ShowPreview);
        ToggleSmileysCommand = new RelayCommand(() => ShowSmileys = !ShowSmileys);

        WrapCommentCommand = new RelayCommand(param =>
        {
            switch (param as string)
            {
                case "b": WrapSelection("[b]", "[/b]"); break;
                case "i": WrapSelection("[i]", "[/i]"); break;
                case "u": WrapSelection("[u]", "[/u]"); break;
                case "url": WrapSelection("[url=]", "[/url]"); break;
                case "color": WrapSelection($"[color=#{CommentColor.TrimStart('#')}]", "[/color]"); break;
            }
        });

        InsertSmileyCommand = new RelayCommand(param =>
        {
            if (param is string name) InsertAtCaret($"[img={name}]");
        });

        PostCommentCommand = new RelayCommand(async () =>
        {
            if (_spot == null) return;
            if (string.IsNullOrWhiteSpace(NewCommentText)) return;

            if (_commentService != null)
            {
                IsPostingComment = true;
                CommentPostStatus = "Reactie plaatsen...";

                var (success, comment, msg) = await _commentService.PostCommentAsync(
                    _spot,
                    SenderNickname,
                    NewCommentText
                );

                IsPostingComment = false;
                CommentPostStatus = msg;

                if (success && comment != null)
                {
                    Comments.Add(comment);
                    NewCommentText = "";
                }
            }
            else
            {
                CommentPostStatus = "Geen reactiedienst beschikbaar.";
            }
        });
    }

    public async Task LoadSpotDetailsAsync(SpotItem spot)
    {
        IsLoading = true;
        StatusMessage = "Details laden...";
        try
        {
            // Anything already cached shows immediately; the network fills in the rest.
            Comments.Clear();
            foreach (var c in await _dbService.GetCommentsAsync(spot.MsgId))
            {
                Comments.Add(c);
            }

            // Load spam reports
            SpamReports.Clear();
            var reports = await _dbService.GetSpamReportsAsync(spot.MsgId);
            foreach (var r in reports)
            {
                SpamReports.Add(r);
            }
            SpamReportCount = reports.Count;
            OnPropertyChanged(nameof(HasSpamReports));
            RebuildFields();

            Description = "";
            StatusMessage = "Spot ophalen van Usenet...";

            // The description and the cover image live in the spot's own article.
            if (_bodyService != null)
            {
                var body = await _bodyService.FetchAsync(spot);
                if (!ReferenceEquals(spot, _spot)) return;   // user moved on

                if (body != null)
                {
                    Description = SpotMarkup.ToPlainText(body.Description);
                    if (body.Image is { Length: > 0 })
                    {
                        try
                        {
                            using var stream = new MemoryStream(body.Image);
                            PosterImage = new Bitmap(stream);
                            OnPropertyChanged(nameof(HasPoster));
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Cover image for {0} is not a readable bitmap", spot.MsgId);
                        }
                    }
                }
            }

            if (Description.Length == 0)
            {
                Description = "(Geen beschrijving beschikbaar — het artikel staat niet meer op de server.)";
            }

            // Comments come from the reply group, using the index built during sync.
            if (_commentService != null)
            {
                StatusMessage = "Reacties ophalen...";
                var fetched = await _commentService.FetchCommentsAsync(spot);
                if (!ReferenceEquals(spot, _spot)) return;

                if (fetched.Count > 0)
                {
                    Comments.Clear();
                    foreach (var c in fetched) Comments.Add(c);
                }
            }

            StatusMessage = "";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load spot details for {0}", spot.MsgId);
            StatusMessage = "Fout bij laden van spot details.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
