using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.IntegrationTests;

/// <summary>
/// Reads and writes JSON exactly the way the API does.
/// </summary>
/// <remarks>
/// <para>
/// The <c>System.Net.Http.Json</c> extensions serialise with <c>JsonSerializerDefaults.Web</c> and
/// nothing else, so a test using them directly does <b>not</b> share the API's converters. Enums are
/// the case that matters: the API writes <c>"status": "not_recorded"</c> and a default reader,
/// expecting either an integer or the exact member name, throws.
/// </para>
/// <para>
/// Worth having as a wrapper rather than passing options at each call site: a forgotten argument
/// silently reverts one test to a different contract than the one under test, which is the kind of
/// failure that gets "fixed" by loosening the assertion.
/// </para>
/// </remarks>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, Uri url) =>
        client.GetFromJsonAsync<T>(url, Options);

    public static Task<T?> ReadJsonAsync<T>(this HttpContent content) =>
        content.ReadFromJsonAsync<T>(Options);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, Uri url, T value) =>
        client.PostAsJsonAsync(url, value, Options);

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, Uri url, T value) =>
        client.PatchAsJsonAsync(url, value, Options);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, Uri url, T value) =>
        client.PutAsJsonAsync(url, value, Options);
}
