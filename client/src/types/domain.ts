/**
 * Domain models for the AI Meeting Assistant.
 *
 * These are the wire shapes the UI consumes. The mock service layer in
 * `src/lib/api` returns exactly these types, so swapping in a real backend is a
 * matter of changing the transport, not the components.
 *
 * All timestamps are ISO 8601 strings so they survive a JSON round-trip
 * through localStorage without a revival step.
 */

export type ISODateString = string;

/** Every persisted record carries these. */
export interface BaseEntity {
  id: string;
  createdAt: ISODateString;
  updatedAt: ISODateString;
}

/* -------------------------------------------------------------------------- */
/* Users & team                                                               */
/* -------------------------------------------------------------------------- */

export type UserRole = "owner" | "admin" | "member" | "guest";
export type UserStatus = "active" | "invited" | "suspended";

export interface User extends BaseEntity {
  name: string;
  email: string;
  /** Generated avatar seed; the UI renders initials when no image is present. */
  avatarUrl: string | null;
  role: UserRole;
  status: UserStatus;
  jobTitle: string;
  department: string;
  timezone: string;
  lastActiveAt: ISODateString;
}

/** The signed-in session, persisted so a refresh restores the user. */
export interface AuthSession {
  userId: string;
  email: string;
  name: string;
  avatarUrl: string | null;
  /** Mock bearer token — shaped like a real one so the API layer can send it. */
  token: string;
  issuedAt: ISODateString;
  expiresAt: ISODateString;
}

/* -------------------------------------------------------------------------- */
/* Meetings                                                                   */
/* -------------------------------------------------------------------------- */

export type MeetingStatus =
  | "scheduled"
  | "live"
  | "processing"
  | "completed"
  | "cancelled";

export type RecordingStatus =
  | "not_recorded"
  | "recording"
  | "paused"
  | "recorded"
  | "failed";

export type SummaryStatus =
  | "none"
  | "queued"
  | "generating"
  | "ready"
  | "failed";

export type MeetingPlatform = "zoom" | "google_meet" | "teams" | "in_person";

export type ParticipantRole = "host" | "presenter" | "attendee";

export interface Participant {
  userId: string;
  name: string;
  email: string;
  avatarUrl: string | null;
  role: ParticipantRole;
  /** Share of total speaking time, 0–1. Drives the speaker distribution chart. */
  talkTimeRatio: number;
  attended: boolean;
}

export interface Meeting extends BaseEntity {
  title: string;
  description: string;
  /** Scheduled start. For live meetings this is when recording began. */
  startsAt: ISODateString;
  endsAt: ISODateString;
  /** Actual recorded length in seconds; 0 until the meeting completes. */
  durationSeconds: number;
  status: MeetingStatus;
  recordingStatus: RecordingStatus;
  summaryStatus: SummaryStatus;
  platform: MeetingPlatform;
  meetingUrl: string | null;
  organizerId: string;
  participants: Participant[];
  tags: string[];
  isFavorite: boolean;
  isArchived: boolean;
  /** Timestamps (seconds into the recording) the user flagged while listening. */
  bookmarks: Bookmark[];
}

export interface Bookmark {
  id: string;
  /** Offset from the start of the recording, in seconds. */
  atSeconds: number;
  label: string;
  createdAt: ISODateString;
}

/* -------------------------------------------------------------------------- */
/* Transcript & AI output                                                     */
/* -------------------------------------------------------------------------- */

export interface TranscriptSegment {
  id: string;
  meetingId: string;
  speakerId: string;
  speakerName: string;
  /** Offset from the start of the recording, in seconds. */
  startSeconds: number;
  endSeconds: number;
  text: string;
  /** Model confidence 0–1; low values are surfaced with a subtle marker. */
  confidence: number;
  /** Set when the AI flagged this line as containing a commitment. */
  isActionItem: boolean;
}

export type SummaryHighlightKind =
  | "decision"
  | "risk"
  | "question"
  | "highlight";

export interface SummaryHighlight {
  id: string;
  kind: SummaryHighlightKind;
  text: string;
  /** Links back into the transcript so the user can verify the claim. */
  sourceSegmentId: string | null;
  atSeconds: number | null;
}

export interface AISummary extends BaseEntity {
  meetingId: string;
  executiveSummary: string;
  keyPoints: string[];
  highlights: SummaryHighlight[];
  /** Model that produced this summary, shown in the provenance line. */
  model: string;
  generatedAt: ISODateString;
}

/* -------------------------------------------------------------------------- */
/* Tasks & action items                                                       */
/* -------------------------------------------------------------------------- */

export type TaskPriority = "low" | "medium" | "high" | "urgent";
export type TaskStatus = "todo" | "in_progress" | "blocked" | "done";

export interface ActionItem extends BaseEntity {
  title: string;
  description: string;
  assigneeId: string | null;
  creatorId: string;
  dueDate: ISODateString | null;
  priority: TaskPriority;
  status: TaskStatus;
  /** Null for tasks created by hand rather than extracted from a meeting. */
  meetingId: string | null;
  /** Transcript line the AI extracted this from, when applicable. */
  sourceSegmentId: string | null;
  completedAt: ISODateString | null;
  tags: string[];
}

/* -------------------------------------------------------------------------- */
/* Documents & knowledge base                                                 */
/* -------------------------------------------------------------------------- */

export type DocumentType = "pdf" | "docx" | "pptx" | "txt" | "csv" | "image";

export type ProcessingStatus =
  | "uploading"
  | "processing"
  | "indexed"
  | "failed";

export interface DocumentFile extends BaseEntity {
  name: string;
  type: DocumentType;
  /** Size in bytes. */
  sizeBytes: number;
  ownerId: string;
  processingStatus: ProcessingStatus;
  /** Free-text preview used by search and the knowledge base cards. */
  excerpt: string;
  tags: string[];
  isFavorite: boolean;
  meetingId: string | null;
}

export type KnowledgeItemKind =
  | "document"
  | "meeting_note"
  | "ai_summary"
  | "link";

export interface KnowledgeItem extends BaseEntity {
  title: string;
  kind: KnowledgeItemKind;
  category: string;
  excerpt: string;
  tags: string[];
  isFavorite: boolean;
  ownerId: string;
  /** Points at the document, meeting or URL this item surfaces. */
  sourceId: string | null;
  sourceUrl: string | null;
  lastOpenedAt: ISODateString | null;
}

/* -------------------------------------------------------------------------- */
/* Comments                                                                   */
/* -------------------------------------------------------------------------- */

export interface Comment extends BaseEntity {
  meetingId: string;
  authorId: string;
  body: string;
  /** User ids referenced with @ in the body. */
  mentions: string[];
  /** Null for top-level comments. */
  parentId: string | null;
  /** Optional anchor into the recording. */
  atSeconds: number | null;
}

/* -------------------------------------------------------------------------- */
/* Notifications & activity                                                   */
/* -------------------------------------------------------------------------- */

export type NotificationKind =
  | "transcript_ready"
  | "summary_ready"
  | "meeting_reminder"
  | "task_assigned"
  | "mention"
  | "document_uploaded";

export interface AppNotification extends BaseEntity {
  kind: NotificationKind;
  title: string;
  body: string;
  isRead: boolean;
  isArchived: boolean;
  /** In-app route this notification deep-links to. */
  href: string | null;
  actorId: string | null;
}

export type ActivityKind =
  | "meeting_created"
  | "meeting_completed"
  | "summary_generated"
  | "task_created"
  | "task_completed"
  | "document_uploaded"
  | "member_joined"
  | "comment_added";

export interface ActivityLog extends BaseEntity {
  kind: ActivityKind;
  actorId: string;
  /** Rendered as the activity line; already resolved to display text. */
  summary: string;
  targetId: string | null;
  href: string | null;
}

/* -------------------------------------------------------------------------- */
/* Integrations                                                               */
/* -------------------------------------------------------------------------- */

export type IntegrationCategory =
  | "meetings"
  | "calendar"
  | "storage"
  | "productivity";

export type IntegrationStatus = "connected" | "disconnected" | "error";

export interface Integration extends BaseEntity {
  key: string;
  name: string;
  description: string;
  category: IntegrationCategory;
  status: IntegrationStatus;
  connectedAt: ISODateString | null;
  accountLabel: string | null;
}

/* -------------------------------------------------------------------------- */
/* Organizations & workspace                                                  */
/* -------------------------------------------------------------------------- */

export type OrganizationPlan = "free" | "team" | "business" | "enterprise";

export interface Organization extends BaseEntity {
  name: string;
  /** URL-safe identifier, shown in the workspace switcher. */
  slug: string;
  plan: OrganizationPlan;
  /** Ids of members belonging to this organization. */
  memberIds: string[];
  /** Exactly one organization is active at a time. */
  isCurrent: boolean;
  ownerId: string;
}

export type MeetingVisibility = "workspace" | "participants" | "private";
export type RetentionPeriod = "3m" | "12m" | "forever";

export interface WorkspaceSettings {
  name: string;
  defaultVisibility: MeetingVisibility;
  retention: RetentionPeriod;
}

/* -------------------------------------------------------------------------- */
/* API keys & invitations                                                     */
/* -------------------------------------------------------------------------- */

export interface ApiKey extends BaseEntity {
  name: string;
  /** Shown in full exactly once, at creation. */
  prefix: string;
  /** The secret, stored only so the demo can reveal it once. */
  secret: string;
  lastUsedAt: ISODateString | null;
  revokedAt: ISODateString | null;
  scopes: ("read" | "write")[];
}

export type InvitationStatus = "pending" | "accepted" | "revoked";

export interface Invitation extends BaseEntity {
  email: string;
  role: UserRole;
  status: InvitationStatus;
  invitedById: string;
  expiresAt: ISODateString;
}

/* -------------------------------------------------------------------------- */
/* Preferences                                                                */
/* -------------------------------------------------------------------------- */

export type ThemeMode = "light" | "dark" | "system";
export type ViewMode = "list" | "grid";
export type CalendarView = "month" | "week" | "day";
/** Tasks support a board layout that the grid/list pair doesn't cover. */
export type TasksView = "list" | "board" | "calendar";

/** Which notification kinds reach the user, and through which channel. */
export interface NotificationPreferences {
  inApp: NotificationKind[];
  email: NotificationKind[];
}

export interface AiPreferences {
  /** Detail level for generated summaries. */
  summaryLength: "brief" | "standard" | "detailed";
  autoSummarise: boolean;
  autoExtractActionItems: boolean;
  /** Detected items are held for review rather than created outright. */
  requireActionItemReview: boolean;
  /** Language the model is asked to answer in. */
  outputLanguage: string;
}

export interface Preferences {
  theme: ThemeMode;
  sidebarCollapsed: boolean;
  meetingsView: ViewMode;
  knowledgeView: ViewMode;
  calendarView: CalendarView;
  tasksView: TasksView;
  language: string;
  density: "comfortable" | "compact";
  recentMeetingIds: string[];
  recentSearches: string[];
  notifications: NotificationPreferences;
  ai: AiPreferences;
}

/* -------------------------------------------------------------------------- */
/* AI chat                                                                    */
/* -------------------------------------------------------------------------- */

export type ChatRole = "user" | "assistant";

export interface ChatSource {
  id: string;
  label: string;
  kind: "meeting" | "document" | "knowledge";
  href: string;
}

export interface ChatMessage {
  id: string;
  role: ChatRole;
  content: string;
  createdAt: ISODateString;
  /** Citations backing an assistant answer. */
  sources: ChatSource[];
}

export interface ChatConversation extends BaseEntity {
  title: string;
  messages: ChatMessage[];
}

/* -------------------------------------------------------------------------- */
/* Query envelope shared by every list endpoint                               */
/* -------------------------------------------------------------------------- */

export type SortDirection = "asc" | "desc";

export interface ListQuery<TSortKey extends string = string> {
  search?: string;
  sortBy?: TSortKey;
  sortDir?: SortDirection;
  page?: number;
  pageSize?: number;
}

export interface Paginated<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
