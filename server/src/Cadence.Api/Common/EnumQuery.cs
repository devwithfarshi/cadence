namespace Cadence.Api.Common;

/// <summary>
/// Parses enum values out of a query string, in the wire spelling the API uses everywhere else.
/// </summary>
/// <remarks>
/// <para>
/// Query-string binding does not go through <c>System.Text.Json</c>, so the snake_case converter
/// registered for request and response bodies does not apply to it. Minimal APIs bind an enum
/// parameter with <c>Enum.TryParse</c> <b>case-sensitively</b> and with no naming policy — which
/// means <c>?status=completed</c> and <c>?summaryStatus=not_recorded</c> both fail to bind and the
/// request is rejected as malformed, while <c>?status=Completed</c> works.
/// </para>
/// <para>
/// The fix has to live at the endpoint: a type's binding can only be customised by a static
/// <c>TryParse</c> on the type itself, and these are framework enums. So filters bind as strings and
/// come through here.
/// </para>
/// <para>
/// Underscores are dropped before matching, which turns <c>not_recorded</c> into a case-insensitive
/// match for <c>NotRecorded</c>. That is exactly the inverse of the snake_case policy, so the two
/// cannot disagree about a value.
/// </para>
/// </remarks>
internal static class EnumQuery
{
    /// <summary>
    /// Converts the supplied values, reporting the first one that is not a member of the enum.
    /// </summary>
    /// <remarks>
    /// An unknown value is an error rather than a silently dropped filter. Ignoring it would return
    /// a page of unfiltered results that looks like a correct answer to a different question.
    /// </remarks>
    public static bool TryParseAll<TEnum>(
        string[]? values,
        out IReadOnlyList<TEnum>? parsed,
        out string? invalid)
        where TEnum : struct, Enum
    {
        parsed = null;
        invalid = null;

        if (values is null || values.Length == 0)
        {
            return true;
        }

        var result = new List<TEnum>(values.Length);

        foreach (var value in values)
        {
            if (!TryParse<TEnum>(value, out var item))
            {
                invalid = value;
                return false;
            }

            result.Add(item);
        }

        parsed = result;
        return true;
    }

    public static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(
            value?.Replace("_", string.Empty, StringComparison.Ordinal),
            ignoreCase: true,
            out parsed);

    /// <summary>The permitted spellings, for an error message that says what to send instead.</summary>
    public static string Allowed<TEnum>()
        where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>().Select(ToSnakeCase));

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsUpper(name[index]) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[index]));
        }

        return builder.ToString();
    }
}
