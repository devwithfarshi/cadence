/**
 * Dialogue script for the live meeting simulation.
 *
 * Written as a real architecture review with genuine disagreement, so the
 * transcript, the AI notes and the detected action items all reference the same
 * substance as it unfolds. `action` marks lines the detector should surface as
 * commitments; `note` marks lines that produce an AI observation.
 */

export interface ScriptLine {
  /** Index into the meeting's participant list. */
  speaker: number;
  text: string;
  /** Detected as a commitment and offered as an action item. */
  action?: {
    title: string;
    assignee: number;
    priority: "high" | "medium" | "urgent";
  };
  /** Produces an entry in the AI notes panel. */
  note?: string;
}

export const LIVE_SCRIPT: ScriptLine[] = [
  {
    speaker: 1,
    text: "Right, let's get into it. The proposal is to split the monolith into four services: ingestion, transcription, summarisation, and the API gateway. I want to pressure-test that before we commit any engineering time.",
    note: "Meeting opened on the proposed four-service split: ingestion, transcription, summarisation, API gateway.",
  },
  {
    speaker: 2,
    text: "My concern is transcription. It's the only component with a hard real-time constraint, and it's the one we'd be pulling furthest from the data it needs.",
  },
  {
    speaker: 1,
    text: "Say more about that.",
  },
  {
    speaker: 2,
    text: "Right now transcription reads speaker profiles and vocabulary hints directly from the same database as everything else. If we split it out, every one of those reads becomes a network call. We do about forty of them per minute of audio.",
    note: "Transcription performs ~40 profile and vocabulary reads per minute of audio, currently as local database reads.",
  },
  {
    speaker: 0,
    text: "Forty a minute doesn't sound like much on its own. What's the actual latency budget?",
  },
  {
    speaker: 2,
    text: "We're at about two hundred milliseconds end to end for a segment. Adding a network hop per read would put us somewhere north of five hundred. That's the difference between the transcript feeling live and feeling laggy.",
  },
  {
    speaker: 3,
    text: "From a user standpoint that's a real regression. The live transcript is the thing people watch during a call. If it drifts half a second behind the speaker, it stops feeling like a transcript and starts feeling like a replay.",
  },
  {
    speaker: 1,
    text: "So the question isn't whether to split, it's whether transcription is the right thing to split first.",
  },
  {
    speaker: 2,
    text: "That's exactly it. I'd argue ingestion is the obvious first candidate. It's stateless, it's bursty, and it's the thing that actually needs to scale independently. Transcription can come later, once we've solved the data locality problem properly.",
  },
  {
    speaker: 0,
    text: "What would solving it properly look like?",
  },
  {
    speaker: 2,
    text: "A read-through cache colocated with the transcription service, warmed at meeting start. We'd know the participant list before the first word is spoken, so we can prefetch every profile we'll need.",
    action: {
      title:
        "Prototype a colocated read-through cache for transcription profile lookups",
      assignee: 2,
      priority: "high",
    },
    note: "Proposed mitigation: colocated read-through cache, warmed from the participant list at meeting start.",
  },
  {
    speaker: 1,
    text: "I like that. It also gives us a measurable checkpoint — if the cache doesn't get us back under three hundred milliseconds, we don't split transcription at all.",
  },
  {
    speaker: 3,
    text: "Can we agree on that number now rather than arguing about it in three weeks? Three hundred milliseconds, measured at the ninety-fifth percentile, not the median.",
  },
  {
    speaker: 1,
    text: "Agreed. P95 under three hundred milliseconds, or transcription stays where it is.",
    note: "Decision: transcription is only extracted if P95 segment latency stays under 300ms behind the cache.",
  },
  {
    speaker: 0,
    text: "One thing nobody has raised — what does this do to our deploy story? Right now we ship one artefact. Four services means four pipelines, four rollback paths, and a versioning contract between them.",
  },
  {
    speaker: 1,
    text: "That's a fair hit. We've been treating this as a runtime architecture question and it's also an operational one.",
  },
  {
    speaker: 3,
    text: "And an on-call one. Who gets paged when summarisation is fine but ingestion is backed up?",
  },
  {
    speaker: 1,
    text: "Let's not hand-wave that. Can you write up what the on-call model looks like under the split, before we commit?",
    action: {
      title:
        "Document the on-call and incident ownership model under the service split",
      assignee: 3,
      priority: "medium",
    },
  },
  {
    speaker: 3,
    text: "I'll have it ready for next week's review.",
  },
  {
    speaker: 0,
    text: "I'd also want a rollback story written down. Splitting is easy to do and very hard to undo once four teams have built against four boundaries.",
    action: {
      title:
        "Write the rollback plan for reverting the service split if it underperforms",
      assignee: 0,
      priority: "urgent",
    },
    note: "Risk raised: the split is difficult to reverse once teams build against the new service boundaries.",
  },
  {
    speaker: 1,
    text: "Good. So where we've landed: ingestion splits first, transcription is gated on the cache prototype hitting P95 under three hundred, and we don't start until the on-call and rollback models are written down. Anyone unhappy with that?",
  },
  {
    speaker: 2,
    text: "That's a much better plan than the one we walked in with.",
  },
  {
    speaker: 0,
    text: "No objection. It's the first version of this that has an exit route.",
  },
  {
    speaker: 1,
    text: "Then let's close there and pick it up next week with those two documents in hand.",
    note: "Meeting concluded: ingestion splits first; transcription gated on a latency benchmark; two documents due before work begins.",
  },
];
