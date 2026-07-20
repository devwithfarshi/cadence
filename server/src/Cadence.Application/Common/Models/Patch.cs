using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Application.Common.Models;

/// <summary>
/// One field of a PATCH body: present or absent, and independently null or not.
/// </summary>
/// <remarks>
/// <para>
/// A nullable property cannot express a partial update, because it collapses two different requests
/// into the same value. <c>{"assigneeId": null}</c> means <i>unassign this task</i>;
/// <c>{"title": "New"}</c> means <i>leave the assignee alone</i>. Both arrive as a null
/// <c>Guid?</c>, so a handler reading one cannot tell which was sent — and picking either
/// interpretation breaks the other caller.
/// </para>
/// <para>
/// <see cref="HasValue"/> distinguishes them, and it is decided by the deserialiser rather than by
/// the handler: <c>System.Text.Json</c> only invokes a converter for a property that actually
/// appears in the payload, so an absent field leaves the struct at its default and an explicit null
/// does not. That is why this is a converter rather than a flag the client sets — a flag can
/// disagree with the body it describes.
/// </para>
/// <para>
/// The client's task drawer sends exactly this shape today: <c>updateTask(id, { status })</c>,
/// <c>updateTask(id, { assigneeId: null })</c>. Modelling it directly is what lets P8 swap the mock
/// for HTTP without rewriting call sites.
/// </para>
/// </remarks>
/// <param name="HasValue">Whether the field appeared in the request body at all.</param>
/// <param name="Value">What it was set to. Meaningless unless <paramref name="HasValue"/>.</param>
[JsonConverter(typeof(PatchConverterFactory))]
public readonly record struct Patch<T>(bool HasValue, T? Value)
{
    /// <summary>The field was not sent.</summary>
    public static Patch<T> Unset => default;

    /// <summary>The field was sent, carrying <paramref name="value"/>.</summary>
    public static Patch<T> Set(T? value) => new(true, value);

    /// <summary>Returns what was sent, or <paramref name="fallback"/> when the field was absent.</summary>
    public T? Or(T? fallback) => HasValue ? Value : fallback;

    /// <summary>Runs <paramref name="apply"/> only if the field was sent.</summary>
    public void Apply(Action<T?> apply)
    {
        if (HasValue)
        {
            apply(Value);
        }
    }
}

/// <summary>Builds the converter for whichever <c>T</c> a <see cref="Patch{T}"/> closes over.</summary>
internal sealed class PatchConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(PatchConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class PatchConverter<T> : JsonConverter<Patch<T>>
{
    /// <summary>
    /// Reached only when the property is present, which is the whole mechanism.
    /// </summary>
    public override Patch<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        Patch<T>.Set(JsonSerializer.Deserialize<T>(ref reader, options));

    /// <summary>
    /// Present for completeness; Cadence never returns a <see cref="Patch{T}"/> in a response.
    /// </summary>
    public override void Write(
        Utf8JsonWriter writer,
        Patch<T> value,
        JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
