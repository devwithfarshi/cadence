using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cadence.Api.Configuration;

/// <summary>
/// OpenAPI document generation.
/// </summary>
/// <remarks>
/// The document is served in <b>every</b> environment — the client renders it as an API reference —
/// but Swagger UI itself is Development-only (§17.1).
/// </remarks>
internal static class SwaggerConfiguration
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddCadenceSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "Cadence API",
                Version = "v1",
                Description =
                    "Meeting intelligence for teams: recordings, transcripts, AI summaries, action "
                    + "items and a workspace knowledge base.\n\n"
                    + "**Authentication is Google-only.** There is no password grant. Obtain a Google "
                    + "ID token via Google Identity Services in the browser, exchange it at "
                    + "`POST /api/v1/auth/google`, then send the returned access token as a bearer "
                    + "token. To try authenticated endpoints here, paste a real access token below.",
            });

            // Feeds the XML comments that Directory.Build.props already generates, so endpoint
            // summaries and parameter docs appear rather than bare method names.
            IncludeXmlComments(options);

            MapPatchTypes(options);

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "The access token returned by /api/v1/auth/google or /api/v1/auth/refresh.",
            });

            // Two Swashbuckle 10 / Microsoft.OpenApi 2.x changes at once: `OpenApiReference` is gone
            // in favour of dedicated reference types, and the requirement is now supplied as a
            // factory taking the document, so the reference can be resolved against it.
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            });

            options.SupportNonNullableReferenceTypes();
        });

        return services;
    }

    private static void IncludeXmlComments(SwaggerGenOptions options)
    {
        // Every Cadence assembly, not just the Api one: DTOs live in Application, and their doc
        // comments are what make the generated schemas readable.
        var directory = AppContext.BaseDirectory;

        foreach (var file in Directory.EnumerateFiles(directory, "Cadence.*.xml"))
        {
            options.IncludeXmlComments(file, includeControllerXmlComments: true);
        }

        var apiXml = Path.Combine(
            directory,
            $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

        if (File.Exists(apiXml))
        {
            options.IncludeXmlComments(apiXml);
        }
    }

    /// <summary>
    /// Documents <see cref="Patch{T}"/> fields as the value they carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Left alone, Swashbuckle reflects over the struct and publishes
    /// <c>{"title": {"hasValue": true, "value": "…"}}</c> — a shape the API does not accept and
    /// would reject. A generated client built from that document would be wrong on every PATCH,
    /// which makes this a correctness problem rather than presentation.
    /// </para>
    /// <para>
    /// Each instantiation is mapped by hand because there is one place to look when a new one is
    /// added, and an unmapped <c>Patch&lt;T&gt;</c> is visible the moment anyone opens the document.
    /// </para>
    /// </remarks>
    private static void MapPatchTypes(SwaggerGenOptions options)
    {
        // Nullable in every case: absence is what the field not being sent means, and null is what
        // clearing it means. The endpoint descriptions carry that distinction — a JSON schema
        // cannot express "omitted means something different from null".
        options.MapType<Patch<string>>(() => Nullable(JsonSchemaType.String));

        options.MapType<Patch<Guid?>>(() => Nullable(JsonSchemaType.String, format: "uuid"));

        options.MapType<Patch<DateTimeOffset?>>(
            () => Nullable(JsonSchemaType.String, format: "date-time"));

        options.MapType<Patch<ActionItemPriority>>(
            () => NullableEnum<ActionItemPriority>());

        options.MapType<Patch<ActionItemStatus>>(
            () => NullableEnum<ActionItemStatus>());

        options.MapType<Patch<IReadOnlyList<string>>>(() => new OpenApiSchema
        {
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
            Items = new OpenApiSchema { Type = JsonSchemaType.String },
        });
    }

    private static OpenApiSchema Nullable(JsonSchemaType type, string? format = null) =>
        new() { Type = type | JsonSchemaType.Null, Format = format };

    /// <summary>Lists the values in the same snake_case spelling the API reads and writes.</summary>
    private static OpenApiSchema NullableEnum<TEnum>()
        where TEnum : struct, Enum
    {
        var schema = Nullable(JsonSchemaType.String);

        schema.Enum =
        [
            .. Enum.GetNames<TEnum>()
                .Select(name => (JsonNode)JsonValue.Create(JsonNamingPolicy.SnakeCaseLower.ConvertName(name))!),
        ];

        return schema;
    }
}
