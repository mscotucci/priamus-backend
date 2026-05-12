# Session Opener Templates

Templates for starting new conversations with an AI assistant about Priamus Trips. Copy the appropriate template into your first message, attach the indicated files, and paste your question.

The goal is to bootstrap the AI's context quickly without re-explaining the project from scratch each time.

---

## Template A — Strategic discussion or decision

Use this when you want to discuss a decision, weigh options, or get advice on architecture/process. NOT for executing tasks (those go to Claude Code).

**Files to attach:**
- `docs/context.md`
- `docs/CURRENT_STATE.md`
- `docs/decisions/README.md` (the ADR index — gives the AI an overview of decisions already made)
- Any specific ADR or appendix relevant to your question

**Message template:**

```
I'm working on Priamus Trips, a .NET 10 backend for freight trip management.
Architectural decisions and current state are in the attached documents.

Before answering my question, please:
1. Read docs/context.md to understand the architecture
2. Read docs/CURRENT_STATE.md to know where we are
3. Skim the ADR index to know what has been decided

Then, my question is:

[YOUR QUESTION HERE]
```

---

## Template B — Resuming a paused workstream

Use this when you want to continue work that was interrupted, and you need the AI to pick up where the previous session left off.

**Files to attach:**
- `docs/context.md`
- `docs/CURRENT_STATE.md`
- The most recent ADR(s) relevant to the workstream
- Optionally, the last commit message or a brief note on what was done last

**Message template:**

```
I'm resuming work on Priamus Trips after a pause. Here is the context:

- Project type: .NET 10 modular monolith backend, see docs/context.md
- Current state: see docs/CURRENT_STATE.md
- I was last working on: [BRIEF DESCRIPTION OR LINK TO LAST COMMIT]

I need your help with:

[WHAT YOU NEED]

Please confirm you have the context before suggesting next steps.
```

---

## Template C — Deep question about a specific concern

Use this for narrow technical questions where one or two appendices are the relevant reference.

**Files to attach:**
- `docs/context.md`
- The one or two specific appendices that bear on the question
- Relevant ADR(s) if the question involves a previous decision

**Message template:**

```
Priamus Trips backend, .NET 10. Context attached.

The question is narrow: [TOPIC IN ONE SENTENCE]

The relevant rules and constraints are in the attached appendices.

[FULL QUESTION HERE]
```

---

## Template D — Reviewing AI-generated work outside Claude Code

Use this when an agent inside Claude Code has produced code or a plan, and you want a second opinion before accepting it.

**Files to attach:**
- `docs/context.md`
- The output from the Claude Code agent (paste as text)
- Any ADRs or appendices the agent's output touches

**Message template:**

```
I'm working on Priamus Trips. An agent in Claude Code has just produced the
following [code / plan / summary]. Before I accept it, I'd like a second opinion.

Context for the project is in docs/context.md.

Here is what the agent produced:

[PASTE OUTPUT]

Please review for:
1. Consistency with the documented patterns
2. Compliance with ADRs (see decisions index attached)
3. Anything the agent might have missed or got wrong

Be honest. I'd rather catch a problem now than after committing.
```

---

## Notes

**Why attach files instead of summarizing?** Summaries lose nuance. The documents are the source of truth and contain the language and tone the project uses. An AI reading them tunes itself to the project's vocabulary.

**Why not just attach the entire repository?** Token cost. The four core documents (`context.md`, `CURRENT_STATE.md`, ADR index, glossary) total under 20 KB and cover almost every strategic question. Heavier appendices are loaded only when relevant.

**What to do when the AI gets it wrong?** Push back. Tell it what it missed, attach the missing context if needed, and ask it to revise. A good AI assistant should accept correction and adjust without defensiveness.

**Should you update these templates over time?** Yes. After a few sessions, you'll discover what context gets asked for repeatedly. Add it to the appropriate template. Treat these templates as living tools.
