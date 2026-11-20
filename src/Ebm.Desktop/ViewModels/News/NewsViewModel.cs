using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.News;
using Ebm.Domain.Players;
using Ebm.Services.News;

namespace Ebm.Desktop.ViewModels.News;

/// <summary>One story in the news list.</summary>
public sealed class NewsRowViewModel(NewsItem item)
{
    public NewsItem Item => item;

    public string Date => item.Date.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);

    public string Category => PlayerCatalog.DisplayName(item.Category);

    public string Headline => item.Headline;

    public string Body => item.Body;

    public string Tone => item.Sentiment switch { >= 2 => "▲▲", 1 => "▲", 0 => string.Empty, -1 => "▼", _ => "▼▼" };
}

/// <summary>One reputation change of the selected club.</summary>
public sealed class ReputationRowViewModel(ReputationChange change)
{
    public string Date => change.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public string Cause => PlayerCatalog.DisplayName(change.Cause);

    public string Change => string.Create(CultureInfo.CurrentCulture, $"{change.Before} → {change.After} ({change.Points:+0;-0})");
}

/// <summary>
/// The news screen (Step 31, docs/NEWS.md): the stored stories, newest first, filtered by club and category; the selected
/// story's text; the club's fans and reputation history. Read-only: news is written by the calendar.
/// </summary>
public sealed partial class NewsViewModel : ObservableObject
{
    private readonly INewsService _news;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private NewsCategory? _category;

    [ObservableProperty]
    private IReadOnlyList<NewsRowViewModel> _stories = [];

    [ObservableProperty]
    private NewsRowViewModel? _selected;

    [ObservableProperty]
    private string _fansText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ReputationRowViewModel> _reputation = [];

    [ObservableProperty]
    private IReadOnlyList<Option<ClubId>> _clubChoices = [];

    public NewsViewModel(INewsService news, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(news);
        _news = news;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    /// <summary>The category filter: <c>null</c> for every category.</summary>
    public IReadOnlyList<Option<NewsCategory>> Categories { get; } =
        [new(null, "All news"), .. Enum.GetValues<NewsCategory>().Select(c => new Option<NewsCategory>(c, PlayerCatalog.DisplayName(c)))];

    public string SelectedText => Selected is { } s ? $"{s.Headline}{Environment.NewLine}{s.Date} · {s.Category}{Environment.NewLine}{Environment.NewLine}{s.Body}" : "Choose a story.";

    public async Task RefreshAsync()
    {
        var keep = Selected?.Item.Id;
        if (ClubChoices.Count != Lookups.Clubs.Count + 1)
        {
            ClubChoices = [new Option<ClubId>(null, "All clubs"), .. Lookups.Clubs];
        }

        Stories = [.. (await _news.GetNewsAsync(new NewsQuery(ClubId, Category))).Select(n => new NewsRowViewModel(n))];
        Selected = Stories.FirstOrDefault(s => s.Item.Id == keep) ?? Stories.FirstOrDefault();
        if (ClubId is { } club)
        {
            var fans = await _news.GetFansAsync(club);
            FansText = fans is null
                ? "No league this season."
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"Reputation {fans.Reputation} ({NewsWriter.Ordinal(fans.ReputationRank)} of {fans.Clubs} in the {fans.Competition}) · fans expect: {PlayerCatalog.DisplayName(fans.Expectation)} · ")
                    + (fans.Position is { } p ? $"{NewsWriter.Ordinal(p)}, form {fans.Form} · " : string.Empty)
                    + (fans.Mood is { } m ? $"mood: {PlayerCatalog.DisplayName(m)}" : $"mood: judged after {MediaRules.FanJudgementGames} league games");
            Reputation = [.. (await _news.GetReputationHistoryAsync(club)).Reverse().Select(c => new ReputationRowViewModel(c))];
        }
        else
        {
            FansText = "Choose a club to see its fans and reputation history.";
            Reputation = [];
        }
    }

    async partial void OnClubIdChanged(ClubId? value) => await RefreshAsync();

    async partial void OnCategoryChanged(NewsCategory? value) => await RefreshAsync();

    partial void OnSelectedChanged(NewsRowViewModel? value) => OnPropertyChanged(nameof(SelectedText));
}
