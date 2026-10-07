using System.Globalization;
using ClockworkUmbraco.Services.Interfaces;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;

namespace ClockworkUmbraco.Services;

public class WordOfTheDayService : IWordOfTheDayService
{
    private const string DateAlias = "wordOfTheDayDate";
    private const string WordAlias = "word";

    private static readonly CultureInfo TrCulture = new("tr-TR");
    private static readonly DateTime Epoch = new(2024, 1, 1);

    private readonly IPublishedContentQuery _publishedContentQuery;
    private readonly IPublishedValueFallback _publishedValueFallback;

    public WordOfTheDayService(
        IPublishedContentQuery publishedContentQuery,
        IPublishedValueFallback publishedValueFallback)
    {
        _publishedContentQuery = publishedContentQuery;
        _publishedValueFallback = publishedValueFallback;
    }

    public Headword? GetWordOfTheDay(DateTime? date = null)
    {
        var today = (date ?? DateTime.Now).Date;

        var headwordNodes = _publishedContentQuery.ContentAtRoot()
            .SelectMany(root => root.DescendantsOfType(Headword.ModelTypeAlias))
            .Where(node => node.TemplateId != null
                           && !string.IsNullOrWhiteSpace(node.Value<string>(WordAlias)))
            .ToList();

        if (headwordNodes.Count == 0)
        {
            return null;
        }

        // 1) Bugüne özel olarak atanmýþ kelime var mý?
        var assigned = headwordNodes
            .Where(node => node.HasValue(DateAlias)
                           && node.Value<DateTime>(DateAlias).Date == today)
            .OrderBy(node => node.Id)
            .FirstOrDefault();

        if (assigned != null)
        {
            return new Headword(assigned, _publishedValueFallback);
        }

        // 2) Atanmýþ kelime yoksa: alfabetik sýralý listede, gün sayýsýna göre döngü
        var ordered = headwordNodes
            .OrderBy(node => node.Value<string>(WordAlias), StringComparer.Create(TrCulture, true))
            .ThenBy(node => node.Id)
            .ToList();

        var daysSinceEpoch = (int)(today - Epoch).TotalDays;
        var index = ((daysSinceEpoch % ordered.Count) + ordered.Count) % ordered.Count;

        return new Headword(ordered[index], _publishedValueFallback);
    }
}