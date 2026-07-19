using System.Buffers;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores an enum as the snake_case string the client already uses.
/// </summary>
/// <remarks>
/// <para>
/// Enums are <c>text</c> with a <c>CHECK</c> constraint rather than a PostgreSQL <c>enum</c> type
/// (§3.1): adding a value to a PG enum needs a migration that cannot run inside a transaction with
/// other DDL, while a CHECK is trivially alterable. Storing the name rather than the ordinal also
/// means a psql session is readable and reordering the C# enum cannot silently reinterpret existing
/// rows.
/// </para>
/// <para>
/// The exact strings are the contract in blueprint §6 — <c>InProgress</c> ↔ <c>in_progress</c>,
/// <c>GoogleMeet</c> ↔ <c>google_meet</c> — so the client's TypeScript unions deserialise unchanged.
/// </para>
/// </remarks>
public sealed class SnakeCaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(value => ToSnakeCase(value), text => FromSnakeCase(text))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ByWireValue =
        Enum.GetValues<TEnum>().ToDictionary(SnakeCase, value => value, StringComparer.Ordinal);

    public static string ToSnakeCase(TEnum value) => SnakeCase(value);

    private static TEnum FromSnakeCase(string text) =>
        ByWireValue.TryGetValue(text, out var value)
            ? value
            // A row holding a value this build does not know about is a data problem, not something
            // to paper over with a default — silently coercing it would hide a failed migration.
            : throw new InvalidOperationException(
                $"'{text}' is not a known {typeof(TEnum).Name}. The database holds a value this build does not recognise.");

    private static string SnakeCase(TEnum value)
    {
        var name = value.ToString()!;
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                builder.Append(name[i]);
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The same mapping for a collection of enums, stored as a <c>text[]</c> column.
/// </summary>
public sealed class SnakeCaseEnumListConverter<TEnum>()
    : ValueConverter<List<TEnum>, string[]>(
        values => values.Select(SnakeCaseEnumConverter<TEnum>.ToSnakeCase).ToArray(),
        values => values.Select(Parse).ToList())
    where TEnum : struct, Enum
{
    private static TEnum Parse(string text) =>
        Enum.GetValues<TEnum>()
            .First(value => string.Equals(
                SnakeCaseEnumConverter<TEnum>.ToSnakeCase(value),
                text,
                StringComparison.Ordinal));
}
