using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// How the AI features behave for one user.
/// </summary>
public sealed class AiPreferences : ValueObject
{
    // Materialisation constructor for the persistence layer. EF needs a parameterless one it
    // can call before setting the mapped members; the factories below are the only path callers get.
    private AiPreferences() => OutputLanguage = null!;

    private AiPreferences(
        SummaryLength summaryLength,
        bool autoSummarise,
        bool autoExtractActionItems,
        bool requireActionItemReview,
        string outputLanguage)
    {
        SummaryLength = summaryLength;
        AutoSummarise = autoSummarise;
        AutoExtractActionItems = autoExtractActionItems;
        RequireActionItemReview = requireActionItemReview;
        OutputLanguage = outputLanguage;
    }

    public SummaryLength SummaryLength { get; private set; }

    public bool AutoSummarise { get; private set; }

    public bool AutoExtractActionItems { get; private set; }

    /// <summary>
    /// When set, detected items are held for review instead of created outright. Defaults to on:
    /// an extraction that is confidently wrong should not silently assign work to a colleague.
    /// </summary>
    public bool RequireActionItemReview { get; private set; }

    /// <summary>BCP-47 tag the model is asked to answer in.</summary>
    public string OutputLanguage { get; private set; }

    public static AiPreferences Default() =>
        new(Enums.SummaryLength.Standard, true, true, true, "en");

    public static AiPreferences Create(
        SummaryLength summaryLength,
        bool autoSummarise,
        bool autoExtractActionItems,
        bool requireActionItemReview,
        string outputLanguage)
    {
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(outputLanguage),
            "Output language is required.");

        return new AiPreferences(
            summaryLength,
            autoSummarise,
            autoExtractActionItems,
            requireActionItemReview,
            outputLanguage.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return SummaryLength;
        yield return AutoSummarise;
        yield return AutoExtractActionItems;
        yield return RequireActionItemReview;
        yield return OutputLanguage;
    }
}
