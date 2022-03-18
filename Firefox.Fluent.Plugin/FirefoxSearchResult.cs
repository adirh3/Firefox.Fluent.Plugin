using Blast.Core.Results;

namespace Firefox.Fluent.Plugin;

public sealed class FirefoxSearchResult : SearchResultBase
{
    public FirefoxSearchResult(string title, string url, string searchedText, string resultType,
        ICollection<SearchTag> tags, BitmapImageResult? icon, double score) : base(title, searchedText, resultType,
        score,
        FirefoxSearchApp.SupportedOperations, tags)
    {
        if (icon != null)
        {
            PreviewImage = icon;
        }
        else
        {
            UseIconGlyph = true;
            IconGlyph = "\uE774";
        }

        SearchObjectId = AdditionalInformation = Context = PinUniqueId = url;
    }

    protected override void OnSelectedSearchResultChanged()
    {
    }
}