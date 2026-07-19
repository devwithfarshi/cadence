namespace Cadence.Domain.Enums;

// Every enum here mirrors a union type in `client/src/types/domain.ts` exactly — the client and the
// API must agree on the wire values or the contract in blueprint §6 breaks.
//
// These are persisted as `text` with a CHECK constraint rather than as PostgreSQL enum types
// (blueprint §3.1): adding a value to a PG enum needs a migration that cannot run inside a
// transaction alongside other DDL, whereas a CHECK is trivially alterable. The cost is a few bytes
// per row, which is not worth a deployment constraint.
//
// The serialised name is the snake_case string the client already uses, applied by a converter in
// the EF configuration; the C# names stay PascalCase.

/// <summary>Role within one organization. Coarse capability; see blueprint §5.4.</summary>
public enum UserRole
{
    Owner,
    Admin,
    Member,
    Guest,
}

public enum UserStatus
{
    Active,
    Invited,
    Suspended,
}

public enum MeetingStatus
{
    Scheduled,
    Live,
    Processing,
    Completed,
    Cancelled,
}

public enum RecordingStatus
{
    NotRecorded,
    Recording,
    Paused,
    Recorded,
    Failed,
}

/// <summary>
/// Lifecycle of a meeting's AI summary.
/// </summary>
/// <remarks>
/// <see cref="Failed"/> is a real, surfaced state — when generation fails the meeting says so and
/// offers a retry. It never falls back to fabricated content (blueprint §23.3).
/// </remarks>
public enum SummaryStatus
{
    None,
    Queued,
    Generating,
    Ready,
    Failed,
}

public enum MeetingPlatform
{
    Zoom,
    GoogleMeet,
    Teams,
    InPerson,
}

public enum ParticipantRole
{
    Host,
    Presenter,
    Attendee,
}

public enum SummaryHighlightKind
{
    Decision,
    Risk,
    Question,
    Highlight,
}

public enum ActionItemPriority
{
    Low,
    Medium,
    High,
    Urgent,
}

public enum ActionItemStatus
{
    Todo,
    InProgress,
    Blocked,
    Done,
}

public enum DocumentType
{
    Pdf,
    Docx,
    Pptx,
    Txt,
    Csv,
    Image,
}

public enum ProcessingStatus
{
    Uploading,
    Processing,
    Indexed,
    Failed,
}

public enum KnowledgeItemKind
{
    Document,
    MeetingNote,
    AiSummary,
    Link,
}

public enum NotificationKind
{
    TranscriptReady,
    SummaryReady,
    MeetingReminder,
    TaskAssigned,
    Mention,
    DocumentUploaded,
}

public enum ActivityKind
{
    MeetingCreated,
    MeetingCompleted,
    SummaryGenerated,
    TaskCreated,
    TaskCompleted,
    DocumentUploaded,
    MemberJoined,
    CommentAdded,
}

public enum IntegrationCategory
{
    Meetings,
    Calendar,
    Storage,
    Productivity,
}

public enum IntegrationStatus
{
    Connected,
    Disconnected,
    Error,
}

public enum OrganizationPlan
{
    Free,
    Team,
    Business,
    Enterprise,
}

/// <summary>Who can see a new recording by default.</summary>
public enum MeetingVisibility
{
    Workspace,
    Participants,
    Private,
}

/// <summary>How long recordings and transcripts are kept before the purge job removes them.</summary>
public enum RetentionPeriod
{
    ThreeMonths,
    TwelveMonths,
    Forever,
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>What a chat citation points at. Every citation must resolve to a real record.</summary>
public enum ChatSourceKind
{
    Meeting,
    Document,
    Knowledge,
}

/// <summary>Scope granted to an API key.</summary>
public enum ApiKeyScope
{
    Read,
    Write,
}

public enum ThemeMode
{
    Light,
    Dark,
    System,
}

public enum ViewMode
{
    List,
    Grid,
}

public enum CalendarView
{
    Month,
    Week,
    Day,
}

/// <summary>Action items support a board layout that the list/grid pair does not cover.</summary>
public enum TasksView
{
    List,
    Board,
    Calendar,
}

public enum UiDensity
{
    Comfortable,
    Compact,
}

/// <summary>Detail level requested from the model when summarising.</summary>
public enum SummaryLength
{
    Brief,
    Standard,
    Detailed,
}
