﻿using System.Collections.ObjectModel;
using Avalonia.Input;
using Blast.API.Processes;
using Blast.API.Search;
using Blast.API.Search.SearchOperations;
using Blast.Core;
using Blast.Core.Interfaces;
using Blast.Core.Objects;
using Blast.Core.Results;
using Microsoft.Data.Sqlite;

namespace Firefox.Fluent.Plugin;

public class FirefoxSearchApp : ISearchApplication
{
    private const string BookmarkTag = "bookmark";
    private const string HistoryTag = "history";
    private const string FirefoxTag = "firefox";
    private const string BookmarkIconGlyph = "\uE8A4";

    /// <summary>
    /// These search operations include also the implementation, meaning the FirefoxSearchApp will not handle them
    /// </summary>
    internal static readonly ObservableCollection<ISearchOperation> SupportedOperations = new()
    {
        new ActionSearchOperation(result =>
        {
            ProcessUtils.GetManagerInstance().StartNewProcess(result.Context);
            return new ValueTask<IHandleResult>(new HandleResult(true, false));
        })
        {
            OperationName = "Open",
            Description = "Opens the URL",
            IconGlyph = "\uE71B",
            KeyGesture = new KeyGesture(Key.D1, KeyModifiers.Control)
        },
        new CopySearchOperationSelfRun("Copy URL")
    };

    private readonly SearchApplicationInfo _applicationInfo;

    private readonly SearchTag _bookmarkSearchTag = new()
    {
        Name = BookmarkTag,
        IconGlyph = BookmarkIconGlyph
    };

    private readonly ObservableCollection<SearchTag> _bookmarkSearchTags;

    private readonly FirefoxSearchAppSettings _firefoxSearchAppSettings;

    private readonly SearchTag _firefoxSearchTag = new()
    {
        Name = FirefoxTag,
        IconGlyph = "\uE81C"
    };

    private readonly SearchTag _historySearchTag = new()
    {
        Name = HistoryTag,
        IconGlyph = "\uE81C"
    };

    private readonly ObservableCollection<SearchTag> _historySearchTags;

    public FirefoxSearchApp()
    {
        _applicationInfo = new SearchApplicationInfo("Firefox",
            "Search for bookmarks and history of Firefox", SupportedOperations)
        {
            MinimumSearchLength = 1,
            MinimumTagSearchLength = 0,
            SearchEmptyTextEmptyTag = false,
            IsProcessSearchOffline = false,
            SearchAllTime = ApplicationSearchTime.Moderate,
            ApplicationIconGlyph = "\uEB41",
            DefaultSearchTags = new List<SearchTag> {_bookmarkSearchTag, _historySearchTag}
        };
        _applicationInfo.SettingsPage = _firefoxSearchAppSettings = new FirefoxSearchAppSettings(_applicationInfo);
        _historySearchTags = new ObservableCollection<SearchTag> {_bookmarkSearchTag, _firefoxSearchTag};
        _bookmarkSearchTags = new ObservableCollection<SearchTag> {_historySearchTag, _firefoxSearchTag};
    }

    public SearchApplicationInfo GetApplicationInfo()
    {
        return _applicationInfo;
    }

    public ValueTask<ISearchResult> GetSearchResultForId(object searchObjectId)
    {
        if (searchObjectId is not FirefoxObjectId firefoxObjectId) return ValueTask.FromResult<ISearchResult>(null);

        BitmapImageResult bitmapImageResult = null;
        try
        {
            using SqliteConnection iconsSqliteConnection =
                CreateSqliteConnectionAndOpen(Path.Combine(firefoxObjectId.FirefoxPath, "favicons.sqlite"));
            using SqliteCommand iconsCommand = iconsSqliteConnection?.CreateCommand();
            if (iconsCommand != null)
                iconsCommand.CommandText =
                    "select data from (select * from moz_pages_w_icons join moz_icons_to_pages on moz_pages_w_icons.id == moz_icons_to_pages.page_id join moz_icons on moz_icons_to_pages.icon_id == moz_icons.id where page_url==$url and width != 65535)";
            iconsCommand?.Parameters.AddWithValue("url", firefoxObjectId.Url);
            using SqliteDataReader iconsReader = iconsCommand!.ExecuteReader();
            if (iconsReader.HasRows && iconsReader.Read())
            {
                byte[] fieldValue = iconsReader.GetFieldValue<byte[]>(0);
                try
                {
                    bitmapImageResult = new BitmapImageResult(new MemoryStream(fieldValue));
                }
                catch (Exception)
                {
                    // ignored, not supported icon
                }
            }
        }
        catch (Exception)
        {
            // ignored
        }

        return ValueTask.FromResult<ISearchResult>(new FirefoxSearchResult(firefoxObjectId.Title, firefoxObjectId.Url,
                string.Empty, firefoxObjectId.IsHistory ? "History" : "Bookmark",
                firefoxObjectId.IsHistory ? _historySearchTags : _bookmarkSearchTags, bitmapImageResult, 0)
            {SearchObjectId = searchObjectId});
    }

    public IAsyncEnumerable<ISearchResult> SearchAsync(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        if (searchRequest.SearchType == SearchType.SearchProcess && searchRequest.ProcessInfo.ProcessName != FirefoxTag)
            return SynchronousAsyncEnumerable.Empty;

        SearchMode searchMode = searchRequest.SearchedTag switch
        {
            "" or FirefoxTag => SearchMode.All,
            BookmarkTag => SearchMode.Bookmarks,
            HistoryTag => SearchMode.History,
            _ => SearchMode.None
        };

        if (searchMode == SearchMode.None)
            return SynchronousAsyncEnumerable.Empty;

        return new GenericSynchronousAsyncEnumerable<ISearchResult>(searchMode switch
        {
            SearchMode.All => SearchBookmarks(searchRequest, cancellationToken)
                .Concat(SearchHistory(searchRequest, cancellationToken)),
            SearchMode.Bookmarks => SearchBookmarks(searchRequest, cancellationToken),
            SearchMode.History => SearchHistory(searchRequest, cancellationToken),
            _ => throw new ArgumentOutOfRangeException()
        });
    }


    public ValueTask<IHandleResult> HandleSearchResult(ISearchResult searchResult)
    {
        // All search operations are self declared, so no need to use the implicit implementation here
        throw new NotImplementedException();
    }

    private IEnumerable<ISearchResult> SearchBookmarks(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        return SearchInPlaces(
            "select title, url, visit_count from (select * from moz_bookmarks inner join moz_places on moz_bookmarks.fk==moz_places.id)",
            searchRequest, false, cancellationToken);
    }

    private IEnumerable<ISearchResult> SearchHistory(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        return SearchInPlaces("select title, url, visit_count from moz_places", searchRequest, true, cancellationToken);
    }

    private IEnumerable<ISearchResult> SearchInPlaces(string query, SearchRequest searchRequest, bool isHistory,
        CancellationToken cancellationToken)
    {
        foreach (FirefoxProfile firefoxProfile in _firefoxSearchAppSettings.FirefoxProfiles.Where(p => p.IsEnabled))
        {
            using SqliteConnection placesSqliteConnection =
                CreateSqliteConnectionAndOpen(Path.Combine(firefoxProfile.Path, "places.sqlite"));
            if (placesSqliteConnection == null)
                continue;

            string searchedText = searchRequest.SearchedText;
            var where = " where";
            using SqliteCommand sqliteCommand = placesSqliteConnection.CreateCommand();
            var paramCount = 0;
            string[] split = searchedText.Split(" ");
            for (var index = 0; index < split.Length; index++)
            {
                string token = split[index];
                string parameterName = "$p" + paramCount;
                where += $" (url like {parameterName} or title like {parameterName})";
                if (index < split.Length - 1)
                    where += " and";
                sqliteCommand.Parameters.AddWithValue(parameterName, '%' + token + '%');
                paramCount++;
            }

            sqliteCommand.CommandText = query + where;
            using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();

            if (cancellationToken.IsCancellationRequested || !sqliteDataReader.HasRows)
                yield break;

            using SqliteConnection iconsSqliteConnection =
                CreateSqliteConnectionAndOpen(Path.Combine(firefoxProfile.Path, "favicons.sqlite"));
            using SqliteCommand iconsCommand = iconsSqliteConnection?.CreateCommand();
            if (iconsCommand != null)
                iconsCommand.CommandText =
                    "select data from (select * from moz_pages_w_icons join moz_icons_to_pages on moz_pages_w_icons.id == moz_icons_to_pages.page_id join moz_icons on moz_icons_to_pages.icon_id == moz_icons.id where page_url==$url and width != 65535)";
            SqliteParameter urlParameter = iconsCommand?.Parameters.AddWithValue("url", "");

            while (sqliteDataReader.Read())
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                string title = !sqliteDataReader.IsDBNull(0) ? sqliteDataReader.GetString(0) : string.Empty;
                string url = sqliteDataReader.GetString(1);

                double score = title.SearchTokens(searchedText);
                if (score == 0)
                    score = title.SearchTokens(searchedText);

                BitmapImageResult bitmapImageResult = null;
                if (urlParameter != null)
                {
                    urlParameter.Value = url;
                    using SqliteDataReader iconsReader = iconsCommand!.ExecuteReader();
                    if (iconsReader.HasRows && iconsReader.Read())
                    {
                        byte[] fieldValue = iconsReader.GetFieldValue<byte[]>(0);
                        try
                        {
                            bitmapImageResult = new BitmapImageResult(new MemoryStream(fieldValue));
                        }
                        catch (Exception)
                        {
                            // ignored, not supported icon
                        }
                    }
                }

                yield return new FirefoxSearchResult(title, url, searchedText, isHistory ? "History" : "Bookmark",
                    isHistory ? _historySearchTags : _bookmarkSearchTags, bitmapImageResult, score)
                {
                    SearchObjectId = new FirefoxObjectId
                    {
                        Url = url,
                        Title = title,
                        IsHistory = isHistory,
                        FirefoxPath = firefoxProfile.Path
                    }
                };
            }
        }
    }

    private static SqliteConnection CreateSqliteConnectionAndOpen(string path)
    {
        var sqliteConnectionStringBuilder = new SqliteConnectionStringBuilder
        {
            Mode = SqliteOpenMode.ReadOnly,
            DataSource = path,
            Pooling = false
        };
        var sqliteConnection = new SqliteConnection(sqliteConnectionStringBuilder.ConnectionString);
        try
        {
            sqliteConnection.Open();
        }
        catch (SqliteException)
        {
            sqliteConnection.Dispose();
            return null;
        }

        return sqliteConnection;
    }

    private enum SearchMode
    {
        None,
        All,
        Bookmarks,
        History
    }
}

public class FirefoxObjectId
{
    public string Url { get; set; }
    public string Title { get; set; }

    public bool IsHistory { get; set; }
    public string FirefoxPath { get; set; }
}