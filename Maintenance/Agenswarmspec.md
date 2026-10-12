# Agent Swarm — System Specification

A reference spec for implementing a multi-agent AI debate system: agents debate a topic in
rounds, a layered diagram visualizes the debate live, a separate synthesis step produces a
final note, and an optional file-access subsystem lets agents propose (never silently apply)
changes to a real project. Written to be implementation-target-agnostic — the reference build
is an Obsidian plugin, but nothing here depends on Obsidian specifically.

---

## 1. Core concepts

| Concept | Definition |
|---|---|
| **Agent** | A named persona with its own system prompt, color, and AI provider/model. Speaks once per round, in a fixed order. |
| **Round** | One full pass through the agent list. A debate runs N rounds (configurable, typically 2–5). |
| **Transcript** | The ordered list of every agent message so far. Re-sent in full (or trimmed) to each agent on every turn so they can see and respond to prior turns. |
| **Synthesis** | A single, separate model call after the debate ends, which reads the full transcript and produces one coherent note. Uses its own provider/model, independent of the debating agents. |
| **Pending change** | A file write an agent proposed but that hasn't been written to disk yet — sits in a review queue until a human approves or rejects it. |

---

## 2. Data model

```ts
type Provider = "anthropic" | "openai" | "google" | "deepseek" | "ollama" | /* extensible */;

interface AgentConfig {
  id: string;              // stable identifier, used to key diagram nodes
  name: string;             // display name
  color: string;             // hex color, used for diagram nodes + message accents
  provider: Provider;
  model: string;             // empty string = use that provider's configured default model
  apiKeyOverride: string;    // empty string = use the shared per-provider key/key-pool
  systemPrompt: string;      // the agent's persona/instructions
}

interface TranscriptMessage {
  agentId: string;
  agentName: string;
  color: string;
  round: number;             // 1-indexed
  provider: Provider;
  content: string;           // the agent's raw reply, possibly with file-op results appended
}

interface PendingChange {
  id: string;                // unique per proposal, e.g. `${agentName}-r${round}-${i}-${timestamp}`
  agentName: string;
  round: number;
  relPath: string;           // path as the agent wrote it (relative)
  fullPath: string;          // resolved, safety-checked absolute/project-relative path
  newContent: string;
  oldContent: string | null; // null = this would create a new file
  timestamp: number;
}
```

Settings (persisted): per-provider API key(s) and default model, agent roster, round count,
synthesis provider/model/prompt, file-access toggle + project root + changelog filename +
autopilot flag, Coder Mode flag + backup of the pre-Coder-Mode roster.

---

## 3. The debate engine (algorithm)

```
function runDebate(settings, topic):
    transcript = []
    effectiveTopic = fileAccessEnabled
        ? topic + "\n\n" + FILE_ACCESS_INSTRUCTIONS
        : topic

    for round in 1..settings.rounds:
        for agent in settings.agents:
            if stopRequested: return transcript

            context = formatTranscript(effectiveTopic, transcript)
            instruction = transcript.isEmpty
                ? context + "\nYou go first. Respond to the topic above."
                : context + "\nNow respond as {agent.name}, building on, challenging, " +
                             "or adding to the discussion so far."

            content = callModel(agent.provider, agent.model, agent.systemPrompt, instruction)
                      // see §6 for retry/backoff/token-limit handling

            if fileAccessEnabled:
                content = resolveFileRequests(agent, round, content)  // see §5

            message = { agentId, agentName, color, round, provider, content }
            transcript.append(message)
            emit(message)   // UI renders it immediately, streaming-style per turn

    return transcript

function runSynthesis(settings, topic, transcript):
    context = formatTranscript(topic, transcript)
    return callModel(settings.synthesisProvider, settings.synthesisModel,
                      settings.synthesisPrompt, context + "\nWrite the synthesis note now.")
```

**`formatTranscript`** — the exact text sent to the model each turn:

```
Topic under discussion: {topic}

[Round 1] {AgentA.name}: {AgentA.content}

[Round 1] {AgentB.name}: {AgentB.content}

...
```

Key properties worth preserving in a port:
- **Sequential, not parallel.** Agents take turns; each sees every prior turn including ones
  earlier in the *same* round. This is what makes later agents in a round able to react to
  earlier ones, not just to previous rounds.
- **The full transcript is re-sent every turn** (not a running conversation with the provider's
  native multi-turn API) — this keeps the provider abstraction uniform across very different
  APIs (see §6) at the cost of resending context repeatedly. A production port at scale should
  consider prompt caching where the provider supports it.
- **Persona diversity is the single biggest lever on debate quality.** Identical or generic
  system prompts across agents reliably converge to consensus by round 2–3 regardless of model
  count. Distinct, opposed roles (advocate/skeptic/wildcard/analyst, or file-ownership roles for
  coding — see §7) sustain real disagreement much longer.

---

## 4. The layered diagram

A "deep neural network"-style visualization: one input node (**Topic**), one column of nodes
per round (one node per agent, positioned in the same row across rounds so an agent's identity
reads vertically), and one output node (**Synthesis**). Edges connect every node in one layer to
every node in the next (a full mesh), matching how every agent in round *N* is influenced by
every message from round *N-1*.

### Layout
```
columns = rounds + 2                     // topic column + one per round + synthesis column
colX(i) = margin + i * columnGap
rowY(i, layerSize) = verticalCenter ± (rowGap * position within a centered stack)
```
Topic and Synthesis columns have exactly one node, vertically centered relative to the tallest
(agent) column. Each round's column has one node per agent, in the same vertical order as the
agent roster, so agent identity is readable by row across the whole diagram.

### Node states
`idle → active → done | error`
- **idle**: default gray.
- **active**: the agent's API call for that round is in flight — pulses, glows in the agent's
  own color.
- **done**: call completed — dims to a flat fill in the agent's color, shows a small round
  badge (`R1`, `R2`, …).
- **error**: call failed — red.

### Edge ("layer transition") states
Edges are grouped per column-transition (topic→round1, round*r*→round*r+1*, roundN→synthesis),
not tracked per individual line — with a full mesh, individual-line precision has no visual
meaning. A transition bundle goes:
- **idle**: static, dim.
- **active**: while *any* agent in the destination layer is still working. Rendered as
  jittering, tapered "lightning bolt" paths (see below) rather than static lines, with a soft
  glow filter, so the diagram reads as "something is happening here right now."
- **done**: once every agent in that round has finished — dims to a flat, faint line.

**Lightning-bolt animation** (the specific effect, if replicating it): for each active edge,
run a short interval (~70ms) that regenerates the path as a jagged polyline between the two
fixed endpoints — split into ~6 segments, each intermediate point offset perpendicular to the
straight line by a random amount, tapered to zero at both ends via `sin(t·π)` so the bolt stays
anchored exactly at the node centers regardless of how much it wobbles in the middle. Add a
slight per-tick random flicker to stroke width/opacity for an "electric" feel. Stop and revert
to a plain straight line the moment the transition leaves the active state — this animation is
deliberately expensive-looking so it should only run when something is actually in flight.

### Practical notes for a port
- Rebuild the diagram fresh at the start of every debate from the *current* agent roster and
  round count — don't try to diff/reuse a previous diagram's DOM, it's cheaper and less bug-prone
  to just regenerate.
- With more than ~6-7 agents or ~4-5 rounds, a full mesh gets visually dense fast (mesh edge
  count grows as agents², layer transitions grow linearly with rounds). Render at a fixed
  legible size and let the container scroll, rather than scaling everything down to fit —
  cramped nodes/labels are worse than needing to scroll.

---

## 5. File-access subsystem

Off by default. When on, agents are told (via a block appended to the topic) that they have
three capabilities, expressed as fenced blocks in their normal reply text:

```
​```agent-file-list
relative/path/to/folder      (empty string = project root)
​```

​```agent-file-read
relative/path/to/file.ext
​```

​```agent-file-write path="relative/path/to/file.ext"
<the full new file contents>
​```
```

### Processing order (after each agent's raw reply, before it's pushed to the transcript)
1. **List requests** are resolved immediately: list the folder's *immediate* children only (not
   recursive — keeps output compact), folders sorted first then alphabetically, folders marked
   with a trailing `/`. Result text is appended to the agent's message.
2. **Read requests** are resolved immediately: file contents (or a "not found/unsafe" message)
   appended to the agent's message.
3. **Write requests** are *never* applied here. Each becomes a `PendingChange` (capturing the
   pre-existing file content, if any, for later diffing) and is handed to a callback — the
   agent's message gets a placeholder note ("waiting for your approval") instead of the actual
   write happening.

Because steps 1–2 happen synchronously within the same turn, their results become part of that
agent's transcript message — visible to the person immediately, and to every subsequent agent
turn, without any special-casing in the transcript format.

### Path safety (independent of what the model does — a real check, not a prompt instruction)
```
resolveSafePath(root, relPath):
    reject if relPath is empty and this isn't a "list root" request
    reject if relPath starts with "/" or matches a drive letter (C:\...)
    split into segments, reject if any segment === ".."
    join remaining segments under root
```
This runs in code, not via instructing the model to behave — an adversarial or malformed
proposal cannot escape the project root even if the model ignores its instructions.

### Approval queue
- Every `PendingChange` renders as a card: agent name, round, target path, create-vs-modify,
  a truncated content preview, **Approve** / **Reject** buttons.
- **Approve** → write to disk, append a row to a changelog file (see below), remove from queue.
- **Reject** → discard, remove from queue. Not logged (keeps the changelog a clean record of
  what actually happened, not a debate transcript).
- The debate does **not** pause for review — agents keep going through rounds while changes sit
  pending. This avoids a multi-agent loop stalling on a UI click, but means a full debate (and
  even its synthesis) can finish before a person has reviewed everything in the queue.

### Changelog
A single markdown table, appended to (or created in) a configurable file inside the project
root, one row per **approved** change only:
```
| Date | Agent | Round | Action | File |
| --- | --- | --- | --- | --- |
| 2026-08-17 09:49:12 | Backend | round 3 | Modified | `backend/app/database.py` |
```

### Known limitations to carry into a port (don't silently "fix" these without flagging them)
- **No merge/conflict resolution.** If two agents propose writes to the same path, they become
  two independent pending cards. Approving both in order means the second silently overwrites
  the first — there is no diff/merge, and no warning that they target the same file.
- **Unapproved changes are invisible to other agents.** A read/list request always reflects
  what's *actually on disk*, never a still-pending proposal. Agents can end up debating against
  a stale view of the project if several proposals are queued up unreviewed.
- **No execution.** This subsystem can create/modify text files. It cannot install dependencies,
  run a server, execute code, or verify that anything it wrote actually works. Treat output as
  a scaffold to run/test outside the system, not a deployed result.

### Autopilot (optional, explicitly opt-in)
A toggle that, when on, skips the approval queue entirely — every proposed write applies
immediately. Because this removes the one safety mechanism the whole subsystem is built around,
gate it behind its own confirmation step (don't let it be a casual checkbox), and keep logging
every applied change regardless of whether Autopilot did it or a human approved it.

---

## 6. Provider abstraction

A single `callModel(provider, model, systemPrompt, userContent) → string` interface, implemented
per-provider (Anthropic, OpenAI-compatible chat/completions — reusable for OpenAI, DeepSeek, and
most hosted look-alikes — Google Generative Language API, local Ollama). Design points worth
preserving:

- **Errors are classified, not just thrown.** At minimum distinguish:
  - `rate_limit` (HTTP 429) and `server` (5xx, e.g. "503 overloaded") — **transient**, worth an
    automatic retry with exponential backoff (e.g. 700ms × 2^attempt, capped at ~3 attempts, with
    jitter).
  - `token_limit` (400/413 with a context-length-shaped error message) — **not transient**;
    retrying identically just fails again. Instead, shrink the transcript sent (keep roughly the
    most recent half of messages, note that older ones were trimmed) and retry once before
    giving up with a clear, actionable error.
  - `auth` (401/403) and `other` — not retried.
- **Multi-key pooling per provider**, for providers with tight per-key rate limits (Google AI
  Studio especially): accept a newline/comma-separated list of keys, round-robin between them,
  and on a `rate_limit`/`server` response from one key, advance to the next key in the same
  request before giving up (a full lap of the pool, not just one retry).
- **Per-agent key override**, independent of the pool: if an agent has its own key/endpoint set,
  it always uses that, bypassing the shared pool entirely. Useful for pinning specific agents to
  specific quotas rather than leaving it to rotation.
- **Surface which agent/provider failed** in any thrown error (`"{agentName} ({providerLabel})
  failed: {reason}"`) — a bare API error string is much harder to debug in a multi-agent,
  multi-provider context than a single-agent chat.

---

## 7. Coding-specific roster ("Dev Team" preset)

For file-access debates specifically, general debate personas (advocate/skeptic/etc.) don't map
well to file-writing work and make the same-file-collision problem worse — more agents with no
sense of file ownership means more chances of two of them proposing writes to the same path in
one run. A role split by *ownership lane* both debates better and collides less:

| Role | Owns | Never touches |
|---|---|---|
| **Architect** | Overall structure, interfaces between pieces | Implementation details |
| **Backend** | Server/API/database-schema files | Frontend files |
| **Frontend** | HTML/CSS/client-side JS | Backend files |
| **Reviewer** | Nothing (never proposes writes) | — reads current on-disk state and critiques it |

The Reviewer role matters specifically because of the "unapproved changes are invisible"
limitation above — a review pass that re-reads actual file state each round is the cheapest
available mitigation for drift between what agents assume and what's really been applied.

A one-click toggle to swap the active roster for this preset (snapshotting whatever roster was
active first, so switching back restores exactly what was there, including custom edits — not
just "whatever it started as") makes this practical to use without hand-retyping prompts every
time the task type changes between general debate and coding work.

---

## 8. UI layout summary

Three-tab shell: **Swarm** (the debate + diagram + pending-changes panel + topic composer),
**Chat** (a persistent one-on-one assistant, conversation auto-saved to a note, reloadable from
history), **Code** (optional — a project-style file tree + text editor, if the target app doesn't
already have its own; not needed if porting into something that's already a code editor, like an
IDE, where the file tree and editor already exist natively).

Composer pattern used throughout: an auto-growing textarea, a paperclip/attach icon to pull in
current context (active note / active file), and a single round send button that toggles to a
Stop state (different icon/color) while a request is in flight, rather than separate Send/Stop
buttons.

---

## 9. Settings surface (minimum viable)

- Per-provider: API key(s) (supports multi-key pool string), default model.
- Debate: round count, "include current context by default" toggle.
- Synthesis: provider, model override, system prompt — independent of the agent roster.
- Agent roster: id/name/color/provider/model-override/key-override/system-prompt per agent, in
  a fixed speaking order (no reordering UI needed for v1 — remove and re-add to reorder).
- File access: master enable toggle, project root, changelog filename, Autopilot toggle (with
  its own confirmation), Coder Mode toggle.

---

## 10. Things a review should specifically check

1. **Debounce settings writes.** If settings persist via a naive "save on every keystroke"
   pattern, rapid typing (e.g. pasting/typing an API key) can queue overlapping async writes
   with no guaranteed completion order — a stale partial value can finish writing *after* the
   correct final one and silently overwrite it. Debounce to a single write ~250ms after the last
   change, and flush immediately on app close so nothing in the debounce window is lost.
2. **Never let read/write path resolution trust the model.** The fenced-block convention is a
   prompt-level contract, not a security boundary by itself — the path-safety check must be real
   code that runs regardless of what the model outputs.
3. **Decide deliberately whether writes need per-file collision detection** before shipping
   Autopilot or heavy file-access use — right now this spec describes a system *without* it (see
   §5's limitations); adding "warn/merge when two pending changes target the same path" is a
   natural v2, not present in the baseline described here.