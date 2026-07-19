using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Chat;
using Cadence.Domain.Collaboration;
using Cadence.Domain.Common;
using Cadence.Domain.Identity;
using Cadence.Domain.Integrations;
using Cadence.Domain.Intelligence;
using Cadence.Domain.Library;
using Cadence.Domain.Meetings;
using Cadence.Domain.Work;
using Cadence.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for Cadence.
/// </summary>
/// <remarks>
/// One context, not one per module. Modules are a code-organisation boundary, not a data boundary —
/// they share a database, foreign keys cross them, and a command that spans two modules has to
/// commit atomically. Splitting the context would make that impossible without distributed
/// transactions (blueprint §7.1).
/// </remarks>
public sealed class CadenceDbContext(
    DbContextOptions<CadenceDbContext> options,
    ICurrentUser currentUser)
    : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();

    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();

    public DbSet<AiSummary> AiSummaries => Set<AiSummary>();

    public DbSet<SummaryHighlight> SummaryHighlights => Set<SummaryHighlight>();

    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<KnowledgeItem> KnowledgeItems => Set<KnowledgeItem>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Integration> Integrations => Set<Integration>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    /// <summary>
    /// The workspace every tenant-scoped query is restricted to.
    /// </summary>
    /// <remarks>
    /// Read through a property rather than captured into the filter expression, so EF treats it as a
    /// parameter and reuses one compiled query across requests instead of recompiling per tenant.
    /// <see cref="Guid.Empty"/> when unauthenticated, which matches nothing — an unauthenticated
    /// caller sees zero rows rather than everything.
    /// </remarks>
    public Guid CurrentOrganizationId => currentUser.OrganizationId ?? Guid.Empty;

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        // Already inside a transaction — a nested BeginTransaction would throw, and the outer scope
        // is the one that should decide the commit boundary.
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        // The execution strategy owns the retry loop, so the whole unit is replayed on a transient
        // failure. Opening the transaction outside it would retry a transaction that no longer exists.
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Domain events are dispatched and discarded within the unit of work; they are never
        // persisted. Without this, EF discovers AggregateRoot.DomainEvents as a navigation and
        // demands a primary key for DomainEvent.
        builder.Ignore<DomainEvent>();

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplySingularTableNames(builder);
        ResetDerivedConstraintNames(builder);
        ApplyEnumConversions(builder);
        ApplyGlobalFilters(builder);

        base.OnModelCreating(builder);
    }

    /// <summary>
    /// Re-derives foreign-key and index names from the final table names, in snake_case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two problems, one fix. Configurations are applied in an unspecified order, so a relationship
    /// built before its principal's table was renamed keeps the name it had at that moment — leaving
    /// a schema where the same rule reads <c>fk_ai_summary_meetings_meeting_id</c> in one place and
    /// <c>fk_transcript_segment_meeting_meeting_id</c> in another.
    /// </para>
    /// <para>
    /// Clearing the name makes EF recompute it, but the recompute happens after
    /// <c>EFCore.NamingConventions</c> has already run, so the result comes back as <c>FK_…</c> /
    /// <c>IX_…</c> while keys and check constraints stay lowercase. The lowercasing is therefore
    /// applied here rather than left to the convention.
    /// </para>
    /// </remarks>
    private static void ResetDerivedConstraintNames(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
            {
                foreignKey.SetConstraintName(null);
                foreignKey.SetConstraintName(Lower(foreignKey.GetConstraintName()));
            }

            foreach (var index in entityType.GetDeclaredIndexes())
            {
                index.SetDatabaseName(null);
                index.SetDatabaseName(Lower(index.GetDatabaseName()));
            }
        }
    }

    private static string? Lower(string? name) => name?.ToLowerInvariant();

    /// <summary>
    /// Names each table after its entity, singular — <c>meeting</c>, not <c>meetings</c>.
    /// </summary>
    /// <remarks>
    /// EF pluralises from the <c>DbSet</c> property name by default. Singular matches the blueprint's
    /// ERD and index list (§3.2, §3.6), and it means a table name is always derivable from the entity
    /// name without knowing how English pluralises it — <c>ai_summary</c> rather than a guess between
    /// <c>ai_summaries</c> and <c>ai_summarys</c>.
    /// </remarks>
    private static void ApplySingularTableNames(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            // Owned types share their owner's table, and a JSON-mapped collection is a column;
            // naming either would either be ignored or split the table apart.
            if (entityType.IsOwned())
            {
                continue;
            }

            entityType.SetTableName(SnakeCase(entityType.ClrType.Name));
        }
    }

    private static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Stores every enum property as its snake_case name.
    /// </summary>
    /// <remarks>
    /// Applied by walking the model rather than per property. Twenty-odd enum columns configured by
    /// hand is twenty-odd chances to forget one, and a forgotten one silently persists as an integer
    /// ordinal — readable to nobody, and reinterpreted the moment the enum is reordered.
    /// </remarks>
    private static void ApplyEnumConversions(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            // A JSON-mapped owned type is one column; its members have no column of their own to
            // constrain, and the converter is declared in its configuration instead.
            var isJsonMapped = entityType.IsMappedToJson();

            foreach (var property in entityType.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                if (!clrType.IsEnum)
                {
                    continue;
                }

                var converterType = typeof(SnakeCaseEnumConverter<>).MakeGenericType(clrType);
                property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);

                if (!isJsonMapped)
                {
                    AddEnumCheckConstraint(entityType, property, clrType);
                }
            }
        }
    }

    /// <summary>
    /// Constrains an enum column to the values the enum actually defines.
    /// </summary>
    /// <remarks>
    /// This is what makes "text plus a CHECK" a real alternative to a PostgreSQL <c>enum</c> type
    /// (§3.10). Without it the column is free text, and a typo in a hand-written <c>UPDATE</c> —
    /// or a rollback to a build that wrote a value since renamed — lands silently and only fails
    /// later, when something reads the row back.
    /// </remarks>
    private static void AddEnumCheckConstraint(
        IMutableEntityType entityType,
        IMutableProperty property,
        Type enumType)
    {
        var columnName = property.GetColumnName();
        var allowed = string.Join(
            ", ",
            Enum.GetValues(enumType)
                .Cast<object>()
                .Select(value => $"'{SnakeCase(value.ToString()!)}'"));

        entityType.AddCheckConstraint(
            $"ck_{entityType.GetTableName()}_{columnName}",
            $"{columnName} IN ({allowed})");
    }

    /// <summary>
    /// Applies the tenant and soft-delete filters to every entity that opts in by interface.
    /// </summary>
    /// <remarks>
    /// <b>The most security-critical code in the system</b> (§3.3). Done here by walking the model,
    /// never per query: a filter that has to be remembered is a filter that will eventually be
    /// forgotten, and forgetting it is a cross-tenant leak rather than a bug someone notices.
    /// A dedicated integration test seeds two organizations and asserts zero cross-visibility.
    /// </remarks>
    private void ApplyGlobalFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            if (!isTenantScoped && !isSoftDeletable)
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "entity");
            Expression? predicate = null;

            if (isTenantScoped)
            {
                // entity.OrganizationId == this.CurrentOrganizationId
                predicate = Expression.Equal(
                    Expression.Property(parameter, nameof(ITenantScoped.OrganizationId)),
                    Expression.Property(
                        Expression.Constant(this),
                        nameof(CurrentOrganizationId)));
            }

            if (isSoftDeletable)
            {
                // entity.DeletedAt == null
                var notDeleted = Expression.Equal(
                    Expression.Property(parameter, nameof(ISoftDeletable.DeletedAt)),
                    Expression.Constant(null, typeof(DateTimeOffset?)));

                predicate = predicate is null ? notDeleted : Expression.AndAlso(predicate, notDeleted);
            }

            builder.Entity(clrType).HasQueryFilter(Expression.Lambda(predicate!, parameter));
        }
    }
}
