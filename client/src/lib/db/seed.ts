/**
 * Demo data generator.
 *
 * Runs once, on first launch, when localStorage is empty. Content is written by
 * hand rather than lorem-ipsum'd so every screen reads like a real workspace —
 * the transcripts, summaries and action items for a given meeting are
 * consistent with each other.
 *
 * Dates are generated relative to "now" at seed time, so the demo always has a
 * plausible today, a recent past and a real upcoming week.
 */

import type {
  ActionItem,
  ActivityLog,
  AISummary,
  AppNotification,
  ChatConversation,
  Comment,
  DocumentFile,
  Integration,
  KnowledgeItem,
  Meeting,
  Participant,
  Preferences,
  TranscriptSegment,
  User,
} from "@/types/domain";
import { collection, markSeeded, needsSeed, write } from "./storage";

/* -------------------------------------------------------------------------- */
/* Deterministic randomness                                                   */
/* -------------------------------------------------------------------------- */

/** mulberry32 — small, fast, seeded PRNG so the demo is reproducible. */
function createRandom(seed: number) {
  let state = seed;
  return () => {
    state |= 0;
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rand = createRandom(20260719);

function pick<T>(items: readonly T[]): T {
  return items[Math.floor(rand() * items.length)];
}

function pickMany<T>(items: readonly T[], count: number): T[] {
  const pool = [...items];
  const out: T[] = [];
  for (let i = 0; i < count && pool.length > 0; i += 1) {
    out.push(pool.splice(Math.floor(rand() * pool.length), 1)[0]);
  }
  return out;
}

function intBetween(min: number, max: number): number {
  return Math.floor(rand() * (max - min + 1)) + min;
}

/* -------------------------------------------------------------------------- */
/* Date helpers                                                               */
/* -------------------------------------------------------------------------- */

const NOW = new Date();
const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

function iso(date: Date): string {
  return date.toISOString();
}

function offset(ms: number): Date {
  return new Date(NOW.getTime() + ms);
}

/** A date `days` from today, pinned to a specific local hour and minute. */
function dayAt(days: number, hour: number, minute = 0): Date {
  const date = new Date(NOW.getTime() + days * DAY);
  date.setHours(hour, minute, 0, 0);
  return date;
}

let idCounter = 0;
function id(prefix: string): string {
  idCounter += 1;
  return `${prefix}_${idCounter.toString(36).padStart(5, "0")}`;
}

/* -------------------------------------------------------------------------- */
/* Users                                                                      */
/* -------------------------------------------------------------------------- */

interface UserSpec {
  name: string;
  email: string;
  role: User["role"];
  jobTitle: string;
  department: string;
}

const USER_SPECS: UserSpec[] = [
  {
    name: "Amara Osei",
    email: "amara.osei@northwind.io",
    role: "owner",
    jobTitle: "VP Product",
    department: "Product",
  },
  {
    name: "Daniel Reyes",
    email: "daniel.reyes@northwind.io",
    role: "admin",
    jobTitle: "Engineering Manager",
    department: "Engineering",
  },
  {
    name: "Priya Raghunathan",
    email: "priya.r@northwind.io",
    role: "member",
    jobTitle: "Staff Engineer",
    department: "Engineering",
  },
  {
    name: "Tomás Lindqvist",
    email: "tomas.l@northwind.io",
    role: "member",
    jobTitle: "Design Lead",
    department: "Design",
  },
  {
    name: "Ingrid Bakker",
    email: "ingrid.bakker@northwind.io",
    role: "admin",
    jobTitle: "Head of Revenue",
    department: "Sales",
  },
  {
    name: "Wei Zhang",
    email: "wei.zhang@northwind.io",
    role: "member",
    jobTitle: "Data Scientist",
    department: "Data",
  },
  {
    name: "Nadia Haddad",
    email: "nadia.haddad@northwind.io",
    role: "member",
    jobTitle: "Customer Success Manager",
    department: "Customer Success",
  },
  {
    name: "Marcus Bell",
    email: "marcus.bell@northwind.io",
    role: "guest",
    jobTitle: "Fractional CFO",
    department: "Finance",
  },
];

function buildUsers(): User[] {
  return USER_SPECS.map((spec, index) => ({
    id: `usr_${(index + 1).toString().padStart(3, "0")}`,
    name: spec.name,
    email: spec.email,
    avatarUrl: null,
    role: spec.role,
    status: index === USER_SPECS.length - 1 ? "invited" : "active",
    jobTitle: spec.jobTitle,
    department: spec.department,
    timezone: pick(["Europe/London", "America/New_York", "Europe/Berlin"]),
    lastActiveAt: iso(offset(-intBetween(5, 4000) * MINUTE)),
    createdAt: iso(offset(-intBetween(120, 600) * DAY)),
    updatedAt: iso(offset(-intBetween(1, 30) * DAY)),
  }));
}

/* -------------------------------------------------------------------------- */
/* Meeting scripts                                                            */
/* -------------------------------------------------------------------------- */

/**
 * Each script drives one completed meeting: the dialogue, the summary and the
 * extracted action items all come from the same source so the detail page is
 * internally consistent.
 */
interface MeetingScript {
  title: string;
  description: string;
  tags: string[];
  dialogue: { speaker: number; text: string; action?: boolean }[];
  executiveSummary: string;
  keyPoints: string[];
  decisions: string[];
  risks: string[];
  questions: string[];
  actions: {
    title: string;
    assignee: number;
    priority: ActionItem["priority"];
  }[];
}

const SCRIPTS: MeetingScript[] = [
  {
    title: "Q3 Product Roadmap Review",
    description:
      "Cross-functional review of the Q3 roadmap ahead of the board update.",
    tags: ["roadmap", "product", "quarterly"],
    dialogue: [
      {
        speaker: 0,
        text: "Thanks everyone for making the time. The goal today is to lock the Q3 roadmap so I can take it to the board on Thursday. Three things to settle: the collaboration work, the mobile timeline, and what we cut.",
      },
      {
        speaker: 1,
        text: "Let me start with where engineering is. Real-time collaboration is about seventy percent done. The editing layer works, but conflict resolution under concurrent edits is still flaky.",
      },
      {
        speaker: 2,
        text: "It's flaky in a specific way. When two people edit the same block within roughly two hundred milliseconds, we occasionally drop one of the operations. It's a bug in our operational transform layer, not a design problem.",
      },
      { speaker: 0, text: "How long to fix properly?" },
      {
        speaker: 2,
        text: "Two weeks if I focus on it. I'd rather do it properly than patch around it — that code will be load-bearing for years.",
        action: true,
      },
      {
        speaker: 1,
        text: "I support that. If we ship a collaboration feature that silently loses edits, we'll spend a year rebuilding trust.",
      },
      {
        speaker: 0,
        text: "Agreed. Priya takes the two weeks. That pushes collaboration to the back half of Q3 — which means something moves. Tomás, where's mobile?",
      },
      {
        speaker: 3,
        text: "Designs are done for the core flows. But I want to flag something: we designed mobile assuming the collaboration primitives exist. If collaboration slips, mobile ships without live presence, which is a meaningfully worse product.",
      },
      {
        speaker: 0,
        text: "That's the real dependency then. Ingrid, what's the commercial pressure on mobile?",
      },
      {
        speaker: 4,
        text: "Two enterprise deals name mobile in the contract — combined that's about four hundred thousand in annual recurring revenue, and both close in October. They don't need live presence. They need offline read access and the ability to approve action items from a phone.",
      },
      {
        speaker: 0,
        text: "So we can decouple. Ship mobile in Q3 without presence, add presence in Q4 once collaboration is solid.",
      },
      {
        speaker: 3,
        text: "That works. I'll rework the mobile designs to degrade gracefully without presence.",
        action: true,
      },
      {
        speaker: 5,
        text: "One data point before we finalise. I pulled usage from the last ninety days. The advanced analytics dashboard we were going to build in Q3 — the existing basic version gets opened by about four percent of weekly active accounts. And half of those sessions are under thirty seconds.",
      },
      { speaker: 0, text: "Four percent. That's our cut, isn't it?" },
      {
        speaker: 5,
        text: "I'd argue yes. We'd be spending a third of a quarter's engineering on something almost nobody opens.",
      },
      {
        speaker: 1,
        text: "That frees up roughly six engineer-weeks. Enough to cover Priya's fix and de-risk the mobile timeline.",
      },
      {
        speaker: 0,
        text: "Then the decision is: advanced analytics comes out of Q3 and goes back into the backlog for reassessment in Q4. Any objection?",
      },
      {
        speaker: 4,
        text: "None from sales. Analytics has never once come up in a deal I've run.",
      },
      {
        speaker: 0,
        text: "Good. Wei, can you write up the usage analysis so I can put it in the board deck? I want the cut to look like a decision, not a retreat.",
        action: true,
      },
      {
        speaker: 5,
        text: "I'll have it to you by Wednesday morning with the retention data alongside it.",
      },
      {
        speaker: 0,
        text: "One risk I want on the record. Both of those enterprise deals closing in October assumes mobile ships on time. If mobile slips past September we have a revenue problem, not just a roadmap problem. Ingrid, can you find out how hard those dates actually are?",
        action: true,
      },
      {
        speaker: 4,
        text: "I'll talk to both champions this week. My read is one is firm and one has some give, but I'll confirm rather than guess.",
      },
    ],
    executiveSummary:
      "The team locked the Q3 roadmap around a single dependency: real-time collaboration is behind schedule due to a conflict-resolution defect, and mobile was designed to depend on it. Rather than delay both, the group decoupled them — mobile ships in Q3 without live presence, collaboration lands properly in the back half of the quarter, and presence follows in Q4. Advanced analytics was cut after usage data showed only 4% weekly engagement, freeing roughly six engineer-weeks to absorb the collaboration fix. The main open risk is that two enterprise contracts worth ~$400K ARR name mobile explicitly and close in October.",
    keyPoints: [
      "Real-time collaboration is ~70% complete; concurrent edits within ~200ms can drop operations due to a defect in the operational transform layer.",
      "A proper fix takes two weeks of focused work; the team chose correctness over a patch because the code is long-lived infrastructure.",
      "Mobile designs assumed collaboration primitives; decoupling lets mobile ship without live presence.",
      "Advanced analytics reaches only ~4% of weekly active accounts, with half of sessions under 30 seconds.",
      "Cutting analytics frees ~6 engineer-weeks, enough to cover the collaboration fix and de-risk mobile.",
    ],
    decisions: [
      "Priya takes two weeks to fix operational transform conflict resolution properly rather than patching it.",
      "Mobile ships in Q3 without live presence; presence is added in Q4 once collaboration is stable.",
      "Advanced analytics is removed from Q3 and returned to the backlog for Q4 reassessment.",
    ],
    risks: [
      "Two enterprise deals totalling ~$400K ARR name mobile in the contract and close in October — a mobile slip past September becomes a revenue risk.",
      "Shipping collaboration with known edit-loss behaviour would damage user trust for far longer than the two-week delay costs.",
    ],
    questions: [
      "How firm are the October close dates on both enterprise contracts?",
      "Should presence be re-scoped as a fast-follow rather than a full Q4 item?",
    ],
    actions: [
      {
        title:
          "Fix operational transform conflict resolution for concurrent edits",
        assignee: 2,
        priority: "high",
      },
      {
        title:
          "Rework mobile designs to degrade gracefully without live presence",
        assignee: 3,
        priority: "high",
      },
      {
        title: "Write up analytics usage analysis for the board deck",
        assignee: 5,
        priority: "medium",
      },
      {
        title:
          "Confirm how firm the October close dates are with both enterprise champions",
        assignee: 4,
        priority: "urgent",
      },
    ],
  },
  {
    title: "Northwind Logistics — Renewal Discussion",
    description:
      "Renewal conversation with Northwind Logistics ahead of their contract expiry.",
    tags: ["customer", "renewal", "enterprise"],
    dialogue: [
      {
        speaker: 4,
        text: "Thanks for making time. Your renewal is up in six weeks, so I wanted to have an honest conversation about how the year has gone before we talk numbers.",
      },
      {
        speaker: 6,
        text: "I appreciate that framing. Honestly it's been mixed. The core product does what we need. The support experience has been rough.",
      },
      { speaker: 4, text: "Tell me about the support side specifically." },
      {
        speaker: 6,
        text: "We had three incidents this year where transcription lagged by several hours. Each time we filed a ticket, and each time it took more than a day to get a substantive response. For us that's a workflow problem — our operations team plans the next day's routes off those meeting notes.",
      },
      {
        speaker: 4,
        text: "That's a fair criticism and I'm not going to argue with it. Our first-response target for your tier is four hours and we clearly missed it. Do you know if those were logged as priority tickets?",
      },
      {
        speaker: 6,
        text: "I don't think our team knew to mark them that way, no.",
      },
      {
        speaker: 4,
        text: "Then that's partly on us for not making the escalation path obvious. I'd like to fix both sides — I'll get your team a proper escalation runbook, and I'll flag the response-time misses internally.",
        action: true,
      },
      {
        speaker: 6,
        text: "That would help. The other thing is seats. We're at forty and we've grown — realistically we need sixty next year.",
      },
      {
        speaker: 4,
        text: "Sixty changes the pricing tier, which actually works in your favour on per-seat cost. Let me put together two options: a straight renewal at sixty seats, and a two-year commitment with the support SLA written into the contract rather than left as a target.",
        action: true,
      },
      {
        speaker: 6,
        text: "The second one is more interesting to me. If the SLA is contractual with actual remedies, that addresses my real concern.",
      },
      {
        speaker: 4,
        text: "Understood. I want to be straight with you though — a two-year term at sixty seats needs sign-off from our finance side before I can commit to SLA remedies. I don't want to promise something I have to walk back.",
      },
      {
        speaker: 6,
        text: "That's reasonable. What's your timeline on getting that answer?",
      },
      {
        speaker: 4,
        text: "End of next week. I'll bring Marcus in on the commercial terms and come back to you with something firm.",
        action: true,
      },
      {
        speaker: 6,
        text: "Works for me. One more thing — is mobile access coming? Our depot managers aren't at desks.",
      },
      {
        speaker: 4,
        text: "It's on the Q3 roadmap. I'd rather not give you a date I can't stand behind, but I can say it's actively being built and I'll get you into the beta.",
        action: true,
      },
    ],
    executiveSummary:
      "Northwind Logistics is open to renewing but raised legitimate concerns about support responsiveness — three transcription incidents this year each took over a day for a substantive response against a four-hour target, partly because their team was unaware of the priority escalation path. They intend to grow from 40 to 60 seats, which moves them to a better per-seat tier. The customer expressed clear preference for a two-year term with a contractual SLA including remedies over a straight one-year renewal, but that structure requires finance sign-off before it can be offered. Mobile access is a secondary interest for depot managers.",
    keyPoints: [
      "Three transcription lag incidents this year, each exceeding a day for substantive response against a four-hour tier target.",
      "Customer's team was not aware of the priority escalation path, contributing to the delays.",
      "Seat count growing from 40 to 60, which shifts them into a more favourable pricing tier.",
      "Customer strongly prefers a two-year term with contractual SLA remedies over a standard renewal.",
      "Depot managers need mobile access; currently a Q3 roadmap item with no committed date.",
    ],
    decisions: [
      "Offer two renewal structures: a one-year at 60 seats, and a two-year with contractual SLA remedies.",
      "Do not commit to SLA remedies or a mobile date until finance sign-off and roadmap confidence exist.",
    ],
    risks: [
      "Support responsiveness is the primary renewal risk; a fourth incident before renewal could lose the account.",
      "The two-year SLA structure the customer prefers is not yet approved internally and may not be offerable.",
    ],
    questions: [
      "Will finance approve contractual SLA remedies on a two-year term?",
      "Can Northwind depot managers be included in the mobile beta before renewal closes?",
    ],
    actions: [
      {
        title:
          "Send Northwind an escalation runbook and flag SLA misses internally",
        assignee: 4,
        priority: "high",
      },
      {
        title: "Prepare one-year and two-year renewal options at 60 seats",
        assignee: 4,
        priority: "high",
      },
      {
        title:
          "Get finance sign-off on contractual SLA remedies for a two-year term",
        assignee: 7,
        priority: "urgent",
      },
      {
        title: "Add Northwind depot managers to the mobile beta list",
        assignee: 3,
        priority: "medium",
      },
    ],
  },
  {
    title: "Weekly Engineering Sync",
    description: "Standing engineering sync — status, blockers and incidents.",
    tags: ["engineering", "weekly", "standup"],
    dialogue: [
      {
        speaker: 1,
        text: "Quick round. Priya, you're on the transform layer — where are you?",
      },
      {
        speaker: 2,
        text: "Day four of ten. I've reproduced the edit-loss deterministically now, which was the hard part. The bug is that we apply the transform against the wrong document revision when operations arrive out of order.",
      },
      { speaker: 1, text: "Is the fix structural or localised?" },
      {
        speaker: 2,
        text: "Localised, thankfully. But I want to add property-based tests around it, because unit tests were never going to catch this. That's most of the remaining time.",
        action: true,
      },
      {
        speaker: 1,
        text: "Do it. Wei, the search indexing job — I saw it alerting overnight.",
      },
      {
        speaker: 5,
        text: "Yes, and it's not a real failure. The job times out when a workspace has more than about ten thousand documents, because we index serially. It retries and eventually completes, but it pages someone every time.",
      },
      {
        speaker: 1,
        text: "How many workspaces are over that threshold?",
      },
      {
        speaker: 5,
        text: "Three today. But it grows — six months ago it was zero.",
      },
      {
        speaker: 1,
        text: "So it's a real problem arriving slowly. What's the fix?",
      },
      {
        speaker: 5,
        text: "Batch the indexing and parallelise across workers. Maybe three days. In the meantime I'd like to raise the alert threshold so we stop paging on a known-benign condition.",
        action: true,
      },
      {
        speaker: 1,
        text: "Raise the threshold, but put an expiry on it — I don't want a silenced alert outliving the fix.",
      },
      {
        speaker: 2,
        text: "Can I raise something? We've now got three separate places that parse meeting timestamps and they don't agree on timezone handling. It caused the calendar bug last sprint.",
      },
      {
        speaker: 1,
        text: "That's the kind of thing that gets worse. Write it up as a proper cleanup task and I'll get it prioritised rather than letting it rot in a comment.",
        action: true,
      },
    ],
    executiveSummary:
      "Two active workstreams and one piece of accumulating technical debt. The operational transform fix is on track at day four of ten, with the defect now deterministically reproducible — out-of-order operations were being applied against the wrong document revision. The remaining time goes to property-based tests, since unit tests structurally could not catch this class of bug. Separately, the search indexing job times out on workspaces above ~10,000 documents due to serial indexing; it self-recovers but pages on-call each time. Three workspaces cross that threshold today, up from zero six months ago.",
    keyPoints: [
      "Transform defect reproduced deterministically: transforms applied against the wrong document revision when operations arrive out of order.",
      "Fix is localised, but property-based tests are being added because unit tests cannot catch this class of bug.",
      "Search indexing times out above ~10,000 documents per workspace due to serial processing; it retries successfully but pages on-call.",
      "Workspaces over the threshold grew from zero to three in six months — a slowly arriving real problem.",
      "Three separate code paths parse meeting timestamps with inconsistent timezone handling; already caused one calendar bug.",
    ],
    decisions: [
      "Add property-based tests to the transform fix rather than shipping the localised patch alone.",
      "Raise the indexing alert threshold as a stopgap, but attach an expiry so the silence cannot outlive the fix.",
      "Track timestamp-parsing duplication as a prioritised cleanup task rather than an inline comment.",
    ],
    risks: [
      "A silenced alert without an expiry could mask a genuine indexing failure later.",
      "Inconsistent timezone handling across three code paths has already caused one user-visible bug and will cause more.",
    ],
    questions: [
      "Should indexing parallelisation be pulled forward given the growth rate of large workspaces?",
    ],
    actions: [
      {
        title: "Add property-based tests around operational transform ordering",
        assignee: 2,
        priority: "high",
      },
      {
        title: "Batch and parallelise the search indexing job",
        assignee: 5,
        priority: "medium",
      },
      {
        title: "Raise indexing alert threshold with a hard expiry date",
        assignee: 5,
        priority: "medium",
      },
      {
        title: "Write up timestamp parsing consolidation as a cleanup task",
        assignee: 2,
        priority: "low",
      },
    ],
  },
  {
    title: "Design Critique — Meeting Detail Redesign",
    description:
      "Critique of the new meeting detail layout and summary hierarchy.",
    tags: ["design", "critique", "ux"],
    dialogue: [
      {
        speaker: 3,
        text: "I'll walk through the redesign, then I want genuine pushback rather than polite nodding. The core problem: users open a meeting and can't tell in five seconds whether anything needs them.",
      },
      {
        speaker: 0,
        text: "That matches what I hear. People say the transcript is impressive and then never open it twice.",
      },
      {
        speaker: 3,
        text: "Right. So the new hierarchy puts decisions and action items above the summary, and the transcript becomes a verification tool rather than the main event.",
      },
      {
        speaker: 2,
        text: "I like it, but I'd push on one thing. If action items are the top of the page, they need to be trustworthy. Right now extraction accuracy is maybe eighty percent. Putting weak output at the top makes the whole page feel wrong.",
      },
      {
        speaker: 3,
        text: "That's a good challenge. What if extracted items are visually distinct from confirmed ones until someone accepts them?",
      },
      {
        speaker: 0,
        text: "I like that a lot. It's honest about what the machine knows versus what a human confirmed.",
      },
      {
        speaker: 3,
        text: "I'll add a review state to the action item component and show the source transcript line inline so accepting is a two-second decision.",
        action: true,
      },
      {
        speaker: 2,
        text: "One more: the summary block is long. On a fifty-minute meeting it's four paragraphs, which is a lot to read to decide if you care.",
      },
      {
        speaker: 3,
        text: "Fair. I'll try a single-sentence verdict line above the summary body.",
        action: true,
      },
      {
        speaker: 0,
        text: "Before we commit — has anyone actually tested this with a user, or are we designing from intuition?",
      },
      { speaker: 3, text: "Intuition, so far. That's a legitimate hit." },
      {
        speaker: 0,
        text: "Then let's not ship it blind. Five users, moderated, before it goes to build.",
        action: true,
      },
    ],
    executiveSummary:
      "The redesign reorders the meeting detail page around a single question: does anything here need me? Decisions and action items move above the summary, and the transcript is demoted to a verification tool. The critique surfaced a real dependency — putting AI-extracted action items at the top of the page raises the accuracy bar, and current extraction is around 80%. The team resolved this by making extracted items visually distinct from human-confirmed ones and showing the source transcript line inline. It was also noted that the redesign has not been validated with any users, and the group agreed not to build it without moderated testing first.",
    keyPoints: [
      "Core problem: users cannot determine in five seconds whether a meeting needs their attention.",
      "New hierarchy elevates decisions and action items; transcript becomes a verification tool.",
      "Action item extraction accuracy is ~80%, which is too low to sit unqualified at the top of the page.",
      "Extracted-versus-confirmed states make the system honest about machine confidence.",
      "The redesign is currently based on intuition, with no user validation.",
    ],
    decisions: [
      "Add a review state distinguishing AI-extracted action items from human-confirmed ones.",
      "Show the source transcript line inline so accepting an item is a two-second decision.",
      "Do not proceed to build until the redesign is tested with five moderated users.",
    ],
    risks: [
      "Surfacing 80%-accurate extraction at the top of the page could undermine trust in the entire product surface.",
      "The redesign is unvalidated; building it as-is risks a full rework after testing.",
    ],
    questions: [
      "Can extraction accuracy be raised before the redesign ships, or is the review state sufficient mitigation?",
      "What is the single-sentence verdict line generated from, and how is it validated?",
    ],
    actions: [
      {
        title:
          "Add review state and inline transcript source to the action item component",
        assignee: 3,
        priority: "high",
      },
      {
        title:
          "Prototype a single-sentence verdict line above the summary body",
        assignee: 3,
        priority: "medium",
      },
      {
        title: "Run moderated usability testing with five users before build",
        assignee: 0,
        priority: "urgent",
      },
    ],
  },
];

/** Titles used for meetings that don't get a full script. */
const FILLER_MEETINGS: { title: string; tags: string[] }[] = [
  { title: "Sprint Planning — Sprint 34", tags: ["engineering", "planning"] },
  { title: "Customer Advisory Board", tags: ["customer", "strategy"] },
  { title: "Marketing Campaign Kickoff", tags: ["marketing", "launch"] },
  { title: "Security & Compliance Review", tags: ["security", "compliance"] },
  { title: "1:1 — Daniel & Priya", tags: ["1:1"] },
  { title: "Hiring Loop Debrief — Backend Engineer", tags: ["hiring"] },
  {
    title: "Onboarding Sync — Enterprise Accounts",
    tags: ["customer", "onboarding"],
  },
  { title: "Pricing Strategy Workshop", tags: ["pricing", "strategy"] },
  {
    title: "Incident Postmortem — Indexing Outage",
    tags: ["engineering", "incident"],
  },
  { title: "Board Prep Session", tags: ["executive", "quarterly"] },
  { title: "Design System Working Group", tags: ["design", "platform"] },
  { title: "Support Escalation Review", tags: ["support", "weekly"] },
];

/* -------------------------------------------------------------------------- */
/* Builders                                                                   */
/* -------------------------------------------------------------------------- */

function buildParticipants(
  users: User[],
  indices: number[],
  hostIndex: number,
): Participant[] {
  // Talk time is normalised across participants so the distribution chart sums to 1.
  const weights = indices.map(() => 0.5 + rand());
  const total = weights.reduce((sum, w) => sum + w, 0);

  return indices.map((userIndex, i) => {
    const user = users[userIndex];
    return {
      userId: user.id,
      name: user.name,
      email: user.email,
      avatarUrl: user.avatarUrl,
      role:
        userIndex === hostIndex ? "host" : i === 1 ? "presenter" : "attendee",
      talkTimeRatio: Number((weights[i] / total).toFixed(4)),
      attended: true,
    };
  });
}

interface SeedResult {
  users: User[];
  meetings: Meeting[];
  transcripts: TranscriptSegment[];
  summaries: AISummary[];
  actionItems: ActionItem[];
  documents: DocumentFile[];
  knowledge: KnowledgeItem[];
  comments: Comment[];
  notifications: AppNotification[];
  activity: ActivityLog[];
  integrations: Integration[];
  conversations: ChatConversation[];
}

function build(): SeedResult {
  const users = buildUsers();
  const meetings: Meeting[] = [];
  const transcripts: TranscriptSegment[] = [];
  const summaries: AISummary[] = [];
  const actionItems: ActionItem[] = [];
  const comments: Comment[] = [];
  const activity: ActivityLog[] = [];
  const notifications: AppNotification[] = [];

  /* --- Scripted, completed meetings in the recent past ------------------- */

  const scriptDayOffsets = [-1, -3, -6, -9];

  SCRIPTS.forEach((script, scriptIndex) => {
    const speakerIndices = Array.from(
      new Set(script.dialogue.map((line) => line.speaker)),
    );
    const hostIndex = speakerIndices[0];
    const startsAt = dayAt(
      scriptDayOffsets[scriptIndex],
      intBetween(9, 15),
      30,
    );

    const meetingId = id("mtg");
    let cursor = intBetween(4, 12);
    const segments: TranscriptSegment[] = script.dialogue.map((line) => {
      // Rough speaking pace: ~2.6 words/second, floored so short lines still read.
      const words = line.text.split(" ").length;
      const duration = Math.max(6, Math.round(words / 2.6));
      const segment: TranscriptSegment = {
        id: id("seg"),
        meetingId,
        speakerId: users[line.speaker].id,
        speakerName: users[line.speaker].name,
        startSeconds: cursor,
        endSeconds: cursor + duration,
        text: line.text,
        confidence: Number((0.88 + rand() * 0.11).toFixed(3)),
        isActionItem: Boolean(line.action),
      };
      // The scripted dialogue is a condensed record of the substantive moments,
      // not a word-for-word capture. Spacing lines well apart reflects the
      // pauses, tangents and cross-talk that a real transcript trims away, and
      // keeps the meeting's total length plausible for its depth.
      cursor += duration + intBetween(35, 110);
      return segment;
    });

    transcripts.push(...segments);

    const durationSeconds = cursor + intBetween(60, 240);
    const endsAt = new Date(startsAt.getTime() + durationSeconds * 1000);

    const meeting: Meeting = {
      id: meetingId,
      title: script.title,
      description: script.description,
      startsAt: iso(startsAt),
      endsAt: iso(endsAt),
      durationSeconds,
      status: "completed",
      recordingStatus: "recorded",
      summaryStatus: "ready",
      platform: pick(["zoom", "google_meet", "teams"] as const),
      meetingUrl: `https://meet.example.com/${meetingId}`,
      organizerId: users[hostIndex].id,
      participants: buildParticipants(users, speakerIndices, hostIndex),
      tags: script.tags,
      isFavorite: scriptIndex === 0,
      isArchived: false,
      bookmarks:
        scriptIndex === 0
          ? [
              {
                id: id("bkm"),
                atSeconds:
                  segments[Math.floor(segments.length / 2)].startSeconds,
                label: "Decision on analytics cut",
                createdAt: iso(endsAt),
              },
            ]
          : [],
      createdAt: iso(new Date(startsAt.getTime() - 3 * DAY)),
      updatedAt: iso(endsAt),
    };
    meetings.push(meeting);

    /* Summary, built from the same script so it matches the transcript. */
    const highlightsFrom = (
      kind: "decision" | "risk" | "question" | "highlight",
      texts: string[],
    ) =>
      texts.map((text) => ({
        id: id("hl"),
        kind,
        text,
        sourceSegmentId: pick(segments).id,
        atSeconds: pick(segments).startSeconds,
      }));

    summaries.push({
      id: id("sum"),
      meetingId,
      executiveSummary: script.executiveSummary,
      keyPoints: script.keyPoints,
      highlights: [
        ...highlightsFrom("decision", script.decisions),
        ...highlightsFrom("risk", script.risks),
        ...highlightsFrom("question", script.questions),
      ],
      model: "claude-opus-4-8",
      generatedAt: iso(new Date(endsAt.getTime() + 4 * MINUTE)),
      createdAt: iso(new Date(endsAt.getTime() + 4 * MINUTE)),
      updatedAt: iso(new Date(endsAt.getTime() + 4 * MINUTE)),
    });

    /* Action items extracted from the script. */
    script.actions.forEach((action, actionIndex) => {
      const done = scriptIndex > 1 && actionIndex === 0;
      const completedAt = done ? iso(offset(-intBetween(1, 40) * HOUR)) : null;
      actionItems.push({
        id: id("act"),
        title: action.title,
        description: `Extracted from "${script.title}".`,
        assigneeId: users[action.assignee]?.id ?? users[0].id,
        creatorId: users[hostIndex].id,
        dueDate: iso(dayAt(intBetween(1, 14), 17)),
        priority: action.priority,
        status: done ? "done" : actionIndex === 1 ? "in_progress" : "todo",
        meetingId,
        sourceSegmentId:
          segments.find((segment) => segment.isActionItem)?.id ?? null,
        completedAt,
        tags: script.tags.slice(0, 1),
        createdAt: iso(new Date(endsAt.getTime() + 5 * MINUTE)),
        updatedAt: iso(new Date(endsAt.getTime() + 5 * MINUTE)),
      });
    });

    /* A short comment thread on the two most recent meetings. */
    if (scriptIndex < 2) {
      const parentId = id("cmt");
      comments.push({
        id: parentId,
        meetingId,
        authorId: users[1].id,
        body: `Good discussion. I've linked the follow-up work to this meeting so it doesn't get lost.`,
        mentions: [],
        parentId: null,
        atSeconds: segments[2].startSeconds,
        createdAt: iso(new Date(endsAt.getTime() + 30 * MINUTE)),
        updatedAt: iso(new Date(endsAt.getTime() + 30 * MINUTE)),
      });
      comments.push({
        id: id("cmt"),
        meetingId,
        authorId: users[0].id,
        body: `Agreed — thanks @${users[1].name}. Flagging this for the board pack too.`,
        mentions: [users[1].id],
        parentId,
        atSeconds: null,
        createdAt: iso(new Date(endsAt.getTime() + 52 * MINUTE)),
        updatedAt: iso(new Date(endsAt.getTime() + 52 * MINUTE)),
      });
    }

    activity.push({
      id: id("evt"),
      kind: "summary_generated",
      actorId: users[hostIndex].id,
      summary: `AI summary generated for "${script.title}"`,
      targetId: meetingId,
      href: `/meetings/${meetingId}`,
      createdAt: iso(new Date(endsAt.getTime() + 4 * MINUTE)),
      updatedAt: iso(new Date(endsAt.getTime() + 4 * MINUTE)),
    });
  });

  /* --- Filler meetings: past, today and upcoming ------------------------- */

  FILLER_MEETINGS.forEach((filler, index) => {
    // Spread across roughly -45 days to +9 days.
    const dayOffset = index < 8 ? -intBetween(2, 45) : intBetween(0, 9);
    const isPast = dayOffset < 0;
    const startsAt = dayAt(dayOffset, intBetween(9, 16), pick([0, 15, 30]));
    const durationSeconds = intBetween(15, 60) * 60;
    const endsAt = new Date(startsAt.getTime() + durationSeconds * 1000);

    const participantCount = intBetween(2, 5);
    const indices = pickMany(
      users.map((_, i) => i).filter((i) => users[i].status === "active"),
      participantCount,
    );
    const hostIndex = indices[0];

    const meetingId = id("mtg");
    meetings.push({
      id: meetingId,
      title: filler.title,
      description: `${filler.title} — recurring session.`,
      startsAt: iso(startsAt),
      endsAt: iso(endsAt),
      durationSeconds: isPast ? durationSeconds : 0,
      status: isPast ? "completed" : "scheduled",
      recordingStatus: isPast ? "recorded" : "not_recorded",
      summaryStatus: isPast ? (rand() > 0.25 ? "ready" : "queued") : "none",
      platform: pick(["zoom", "google_meet", "teams", "in_person"] as const),
      meetingUrl: `https://meet.example.com/${meetingId}`,
      organizerId: users[hostIndex].id,
      participants: buildParticipants(users, indices, hostIndex),
      tags: filler.tags,
      isFavorite: index === 3,
      isArchived: index === 6 || index === 7,
      bookmarks: [],
      createdAt: iso(new Date(startsAt.getTime() - 5 * DAY)),
      updatedAt: iso(isPast ? endsAt : startsAt),
    });

    if (isPast) {
      activity.push({
        id: id("evt"),
        kind: "meeting_completed",
        actorId: users[hostIndex].id,
        summary: `"${filler.title}" completed`,
        targetId: meetingId,
        href: `/meetings/${meetingId}`,
        createdAt: iso(endsAt),
        updatedAt: iso(endsAt),
      });
    }
  });

  /* --- One meeting happening right now ----------------------------------- */

  const liveStart = offset(-23 * MINUTE);
  const liveIndices = [0, 1, 2, 3];
  const liveId = id("mtg");
  meetings.push({
    id: liveId,
    title: "Platform Architecture Review",
    description:
      "Reviewing the proposed service boundaries for the platform rewrite.",
    startsAt: iso(liveStart),
    endsAt: iso(offset(37 * MINUTE)),
    durationSeconds: 23 * 60,
    status: "live",
    recordingStatus: "recording",
    summaryStatus: "none",
    platform: "google_meet",
    meetingUrl: `https://meet.example.com/${liveId}`,
    organizerId: users[1].id,
    participants: buildParticipants(users, liveIndices, 1),
    tags: ["engineering", "architecture"],
    isFavorite: false,
    isArchived: false,
    bookmarks: [],
    createdAt: iso(offset(-4 * DAY)),
    updatedAt: iso(offset(-1 * MINUTE)),
  });

  /* --- Standalone tasks not tied to a meeting ---------------------------- */

  const STANDALONE_TASKS = [
    "Update the security questionnaire template for SOC 2",
    "Refresh the onboarding email sequence copy",
    "Audit unused feature flags in production",
    "Draft the Q3 all-hands agenda",
    "Review contractor invoices for June",
  ];

  STANDALONE_TASKS.forEach((title, index) => {
    const done = index % 3 === 0;
    actionItems.push({
      id: id("act"),
      title,
      description: "",
      assigneeId: users[intBetween(0, 6)].id,
      creatorId: users[0].id,
      dueDate: iso(dayAt(intBetween(-3, 16), 17)),
      priority: pick(["low", "medium", "high"] as const),
      status: done ? "done" : pick(["todo", "in_progress", "blocked"] as const),
      meetingId: null,
      sourceSegmentId: null,
      completedAt: done ? iso(offset(-intBetween(2, 90) * HOUR)) : null,
      tags: [],
      createdAt: iso(offset(-intBetween(2, 30) * DAY)),
      updatedAt: iso(offset(-intBetween(1, 40) * HOUR)),
    });
  });

  /* --- Documents --------------------------------------------------------- */

  const DOC_SPECS: {
    name: string;
    type: DocumentFile["type"];
    excerpt: string;
  }[] = [
    {
      name: "Q3 Roadmap Board Deck.pptx",
      type: "pptx",
      excerpt:
        "Board presentation covering Q3 commitments, the analytics cut rationale and the mobile revenue dependency.",
    },
    {
      name: "Northwind Renewal Terms.pdf",
      type: "pdf",
      excerpt:
        "Draft commercial terms for the Northwind Logistics renewal at 60 seats, one and two-year structures.",
    },
    {
      name: "Operational Transform RFC.docx",
      type: "docx",
      excerpt:
        "Technical design for conflict resolution in the collaborative editing layer, including ordering guarantees.",
    },
    {
      name: "Feature Usage Analysis.csv",
      type: "csv",
      excerpt:
        "Ninety-day feature engagement export by workspace, including analytics dashboard session durations.",
    },
    {
      name: "Meeting Detail Redesign.png",
      type: "image",
      excerpt: "High-fidelity mock of the reordered meeting detail hierarchy.",
    },
    {
      name: "Support SLA Policy.pdf",
      type: "pdf",
      excerpt:
        "Current support tier definitions, response targets and escalation paths by plan.",
    },
    {
      name: "Incident Postmortem — Indexing.txt",
      type: "txt",
      excerpt:
        "Timeline and contributing factors for the search indexing timeouts on large workspaces.",
    },
    {
      name: "Hiring Scorecard — Backend.docx",
      type: "docx",
      excerpt:
        "Structured interview scorecard and levelling rubric for backend hires.",
    },
    {
      name: "Pricing Tiers 2026.csv",
      type: "csv",
      excerpt:
        "Per-seat pricing by tier with volume break points and discount floors.",
    },
    {
      name: "Brand Guidelines.pdf",
      type: "pdf",
      excerpt:
        "Logo usage, typography scale and colour tokens for product and marketing.",
    },
  ];

  const documents: DocumentFile[] = DOC_SPECS.map((spec, index) => ({
    id: id("doc"),
    name: spec.name,
    type: spec.type,
    sizeBytes: intBetween(48, 9800) * 1024,
    ownerId: users[intBetween(0, 6)].id,
    processingStatus:
      index === 3 ? "processing" : index === 8 ? "failed" : "indexed",
    excerpt: spec.excerpt,
    tags: pickMany(
      ["finance", "product", "engineering", "customer", "design"],
      2,
    ),
    isFavorite: index < 2,
    meetingId: index < 4 ? meetings[index].id : null,
    createdAt: iso(offset(-intBetween(1, 60) * DAY)),
    updatedAt: iso(offset(-intBetween(1, 20) * DAY)),
  }));

  /* --- Knowledge base ---------------------------------------------------- */

  const KNOWLEDGE_SPECS: {
    title: string;
    kind: KnowledgeItem["kind"];
    category: string;
    excerpt: string;
  }[] = [
    {
      title: "How we decide what to cut",
      kind: "meeting_note",
      category: "Product",
      excerpt:
        "Our standard for removing scope: usage below 5% weekly active, no named deal dependency, and a reversible decision.",
    },
    {
      title: "Support escalation runbook",
      kind: "document",
      category: "Customer Success",
      excerpt:
        "How to mark a ticket priority, who is paged at each tier, and the response targets we commit to contractually.",
    },
    {
      title: "Q3 Roadmap Review — AI Summary",
      kind: "ai_summary",
      category: "Product",
      excerpt:
        "Mobile decoupled from collaboration; analytics cut; two enterprise deals create an October revenue dependency.",
    },
    {
      title: "Operational transform: ordering guarantees",
      kind: "document",
      category: "Engineering",
      excerpt:
        "Why transforms must be applied against the originating revision, and what breaks when they are not.",
    },
    {
      title: "Enterprise pricing playbook",
      kind: "document",
      category: "Sales",
      excerpt:
        "Tier thresholds, when a two-year term is worth offering, and which concessions need finance sign-off.",
    },
    {
      title: "Design critique format",
      kind: "meeting_note",
      category: "Design",
      excerpt:
        "Present the problem before the solution, ask for pushback explicitly, and never ship unvalidated redesigns.",
    },
    {
      title: "Incident response — severity definitions",
      kind: "document",
      category: "Engineering",
      excerpt:
        "What counts as SEV1 through SEV3, who declares, and the expectation for postmortem turnaround.",
    },
    {
      title: "Competitor teardown: transcription accuracy",
      kind: "link",
      category: "Research",
      excerpt:
        "Third-party benchmark comparing word error rate across major meeting transcription providers.",
    },
  ];

  const knowledge: KnowledgeItem[] = KNOWLEDGE_SPECS.map((spec, index) => ({
    id: id("kb"),
    title: spec.title,
    kind: spec.kind,
    category: spec.category,
    excerpt: spec.excerpt,
    tags: pickMany(["reference", "process", "playbook", "research"], 2),
    isFavorite: index === 0 || index === 4,
    ownerId: users[intBetween(0, 6)].id,
    sourceId: spec.kind === "ai_summary" ? meetings[0].id : null,
    sourceUrl: spec.kind === "link" ? "https://example.com/benchmark" : null,
    lastOpenedAt: index < 5 ? iso(offset(-intBetween(1, 200) * HOUR)) : null,
    createdAt: iso(offset(-intBetween(5, 180) * DAY)),
    updatedAt: iso(offset(-intBetween(1, 30) * DAY)),
  }));

  /* --- Notifications ----------------------------------------------------- */

  const notificationSpecs: {
    kind: AppNotification["kind"];
    title: string;
    body: string;
    href: string | null;
    actor: number;
    ageMinutes: number;
    read: boolean;
  }[] = [
    {
      kind: "summary_ready",
      title: "AI summary ready",
      body: `"${SCRIPTS[0].title}" has been summarised with 4 action items extracted.`,
      href: `/meetings/${meetings[0].id}`,
      actor: 0,
      ageMinutes: 14,
      read: false,
    },
    {
      kind: "task_assigned",
      title: "Task assigned to you",
      body: "Confirm how firm the October close dates are with both enterprise champions.",
      href: "/tasks",
      actor: 0,
      ageMinutes: 42,
      read: false,
    },
    {
      kind: "mention",
      title: "Amara Osei mentioned you",
      body: "Agreed — thanks Daniel. Flagging this for the board pack too.",
      href: `/meetings/${meetings[0].id}`,
      actor: 0,
      ageMinutes: 96,
      read: false,
    },
    {
      kind: "transcript_ready",
      title: "Transcript ready",
      body: `"${SCRIPTS[1].title}" transcript is available with speaker labels.`,
      href: `/meetings/${meetings[1].id}`,
      actor: 4,
      ageMinutes: 320,
      read: true,
    },
    {
      kind: "meeting_reminder",
      title: "Meeting starting soon",
      body: "Platform Architecture Review begins in 10 minutes.",
      href: "/live",
      actor: 1,
      ageMinutes: 33,
      read: true,
    },
    {
      kind: "document_uploaded",
      title: "New document uploaded",
      body: "Northwind Renewal Terms.pdf was added to the knowledge base.",
      href: "/documents",
      actor: 4,
      ageMinutes: 700,
      read: true,
    },
  ];

  notifications.push(
    ...notificationSpecs.map((spec) => ({
      id: id("ntf"),
      kind: spec.kind,
      title: spec.title,
      body: spec.body,
      isRead: spec.read,
      isArchived: false,
      href: spec.href,
      actorId: users[spec.actor].id,
      createdAt: iso(offset(-spec.ageMinutes * MINUTE)),
      updatedAt: iso(offset(-spec.ageMinutes * MINUTE)),
    })),
  );

  /* --- Activity log ------------------------------------------------------ */

  activity.push(
    {
      id: id("evt"),
      kind: "task_completed",
      actorId: users[2].id,
      summary:
        "Priya Raghunathan completed “Add property-based tests around operational transform ordering”",
      targetId: null,
      href: "/tasks",
      createdAt: iso(offset(-2 * HOUR)),
      updatedAt: iso(offset(-2 * HOUR)),
    },
    {
      id: id("evt"),
      kind: "document_uploaded",
      actorId: users[4].id,
      summary: "Ingrid Bakker uploaded “Northwind Renewal Terms.pdf”",
      targetId: documents[1].id,
      href: "/documents",
      createdAt: iso(offset(-5 * HOUR)),
      updatedAt: iso(offset(-5 * HOUR)),
    },
    {
      id: id("evt"),
      kind: "comment_added",
      actorId: users[0].id,
      summary: "Amara Osei commented on “Q3 Product Roadmap Review”",
      targetId: meetings[0].id,
      href: `/meetings/${meetings[0].id}`,
      createdAt: iso(offset(-9 * HOUR)),
      updatedAt: iso(offset(-9 * HOUR)),
    },
    {
      id: id("evt"),
      kind: "member_joined",
      actorId: users[7].id,
      summary: "Marcus Bell was invited to the workspace",
      targetId: users[7].id,
      href: "/team",
      createdAt: iso(offset(-2 * DAY)),
      updatedAt: iso(offset(-2 * DAY)),
    },
  );

  /* --- Integrations ------------------------------------------------------ */

  const INTEGRATION_SPECS: {
    key: string;
    name: string;
    description: string;
    category: Integration["category"];
    status: Integration["status"];
    account?: string;
  }[] = [
    {
      key: "zoom",
      name: "Zoom",
      description: "Auto-record and transcribe Zoom meetings as they start.",
      category: "meetings",
      status: "connected",
      account: "northwind.io",
    },
    {
      key: "google_meet",
      name: "Google Meet",
      description: "Join and capture Google Meet calls from your calendar.",
      category: "meetings",
      status: "connected",
      account: "amara.osei@northwind.io",
    },
    {
      key: "teams",
      name: "Microsoft Teams",
      description: "Record Teams meetings and sync participants automatically.",
      category: "meetings",
      status: "disconnected",
    },
    {
      key: "google_calendar",
      name: "Google Calendar",
      description: "Sync upcoming meetings and detect conferencing links.",
      category: "calendar",
      status: "connected",
      account: "amara.osei@northwind.io",
    },
    {
      key: "outlook",
      name: "Outlook Calendar",
      description: "Two-way sync with Outlook events and invitations.",
      category: "calendar",
      status: "disconnected",
    },
    {
      key: "google_drive",
      name: "Google Drive",
      description: "Index documents from Drive into your knowledge base.",
      category: "storage",
      status: "connected",
      account: "Shared drive: Northwind",
    },
    {
      key: "dropbox",
      name: "Dropbox",
      description: "Import and index files stored in Dropbox folders.",
      category: "storage",
      status: "disconnected",
    },
    {
      key: "onedrive",
      name: "OneDrive",
      description: "Sync OneDrive documents for search and AI retrieval.",
      category: "storage",
      status: "error",
      account: "Token expired",
    },
    {
      key: "notion",
      name: "Notion",
      description: "Push meeting summaries into Notion databases.",
      category: "productivity",
      status: "connected",
      account: "Northwind workspace",
    },
    {
      key: "slack",
      name: "Slack",
      description: "Post summaries and action items to channels automatically.",
      category: "productivity",
      status: "connected",
      account: "#product-updates",
    },
    {
      key: "jira",
      name: "Jira",
      description: "Create Jira issues directly from extracted action items.",
      category: "productivity",
      status: "disconnected",
    },
    {
      key: "trello",
      name: "Trello",
      description: "Sync action items to Trello boards and lists.",
      category: "productivity",
      status: "disconnected",
    },
    {
      key: "linear",
      name: "Linear",
      description:
        "Turn action items into Linear issues with assignees intact.",
      category: "productivity",
      status: "connected",
      account: "Team: Platform",
    },
  ];

  const integrations: Integration[] = INTEGRATION_SPECS.map((spec) => ({
    id: id("int"),
    key: spec.key,
    name: spec.name,
    description: spec.description,
    category: spec.category,
    status: spec.status,
    connectedAt:
      spec.status === "connected"
        ? iso(offset(-intBetween(10, 300) * DAY))
        : null,
    accountLabel: spec.account ?? null,
    createdAt: iso(offset(-300 * DAY)),
    updatedAt: iso(offset(-intBetween(1, 40) * DAY)),
  }));

  /* --- AI chat history --------------------------------------------------- */

  const conversations: ChatConversation[] = [
    {
      id: id("cnv"),
      title: "What did we decide about analytics?",
      messages: [
        {
          id: id("msg"),
          role: "user",
          content: "What did we decide about the advanced analytics dashboard?",
          createdAt: iso(offset(-3 * HOUR)),
          sources: [],
        },
        {
          id: id("msg"),
          role: "assistant",
          content:
            "Advanced analytics was cut from Q3 and returned to the backlog for reassessment in Q4.\n\nThe decision was driven by usage data: the existing basic analytics view is opened by roughly 4% of weekly active accounts, and about half of those sessions last under thirty seconds. Cutting it freed approximately six engineer-weeks, which were reallocated to the operational transform fix and to de-risking the mobile timeline.\n\nSales raised no objection — analytics had not come up in any active deal.",
          createdAt: iso(offset(-3 * HOUR + 20000)),
          sources: [
            {
              id: meetings[0].id,
              label: "Q3 Product Roadmap Review",
              kind: "meeting",
              href: `/meetings/${meetings[0].id}`,
            },
            {
              id: documents[3].id,
              label: "Feature Usage Analysis.csv",
              kind: "document",
              href: "/documents",
            },
          ],
        },
      ],
      createdAt: iso(offset(-3 * HOUR)),
      updatedAt: iso(offset(-3 * HOUR)),
    },
    {
      id: id("cnv"),
      title: "Northwind renewal risks",
      messages: [
        {
          id: id("msg"),
          role: "user",
          content: "What are the risks on the Northwind renewal?",
          createdAt: iso(offset(-2 * DAY)),
          sources: [],
        },
        {
          id: id("msg"),
          role: "assistant",
          content:
            "Two risks stand out.\n\nFirst, support responsiveness is the primary threat to the renewal. Northwind experienced three transcription lag incidents this year, each taking more than a day for a substantive response against a four-hour tier target. A fourth incident before the renewal date could plausibly lose the account.\n\nSecond, the structure the customer actually wants — a two-year term with contractual SLA remedies — has not been approved internally. If finance declines, the offer on the table becomes materially less attractive than what was discussed.",
          createdAt: iso(offset(-2 * DAY + 24000)),
          sources: [
            {
              id: meetings[1].id,
              label: "Northwind Logistics — Renewal Discussion",
              kind: "meeting",
              href: `/meetings/${meetings[1].id}`,
            },
          ],
        },
      ],
      createdAt: iso(offset(-2 * DAY)),
      updatedAt: iso(offset(-2 * DAY)),
    },
  ];

  return {
    users,
    meetings,
    transcripts,
    summaries,
    actionItems,
    documents,
    knowledge,
    comments,
    notifications,
    activity,
    integrations,
    conversations,
  };
}

/* -------------------------------------------------------------------------- */
/* Entry point                                                                */
/* -------------------------------------------------------------------------- */

export const DEFAULT_PREFERENCES: Preferences = {
  theme: "system",
  sidebarCollapsed: false,
  meetingsView: "list",
  knowledgeView: "grid",
  calendarView: "month",
  tasksView: "list",
  language: "en",
  density: "comfortable",
  recentMeetingIds: [],
  recentSearches: [],
  notifications: {
    // Everything in-app by default; email is opt-in per kind so the workspace
    // doesn't start by mailing people about every transcript.
    inApp: [
      "transcript_ready",
      "summary_ready",
      "meeting_reminder",
      "task_assigned",
      "mention",
      "document_uploaded",
    ],
    email: ["task_assigned", "mention"],
  },
  ai: {
    summaryLength: "standard",
    autoSummarise: true,
    autoExtractActionItems: true,
    // Extraction is imperfect, so review is on by default.
    requireActionItemReview: true,
    outputLanguage: "en",
  },
};

/**
 * Seeds the store if it is empty or on an older schema. Safe to call on every
 * mount — it returns immediately once seeded.
 */
export function seedIfEmpty(): void {
  if (!needsSeed()) return;

  const data = build();

  collection<User>("users").replaceAll(data.users);
  collection<Meeting>("meetings").replaceAll(data.meetings);
  collection<TranscriptSegment>("transcripts").replaceAll(data.transcripts);
  collection<AISummary>("summaries").replaceAll(data.summaries);
  collection<ActionItem>("action_items").replaceAll(data.actionItems);
  collection<DocumentFile>("documents").replaceAll(data.documents);
  collection<KnowledgeItem>("knowledge").replaceAll(data.knowledge);
  collection<Comment>("comments").replaceAll(data.comments);
  collection<AppNotification>("notifications").replaceAll(data.notifications);
  collection<ActivityLog>("activity").replaceAll(data.activity);
  collection<Integration>("integrations").replaceAll(data.integrations);
  collection<ChatConversation>("conversations").replaceAll(data.conversations);
  write("preferences", DEFAULT_PREFERENCES);

  markSeeded();
}
