# AGENTS.md - AI Agent Rules
# Max ~100 lines. Concise, actionable, no bloat.

## Boundaries (Never Break These)
- Write ALL files ONLY inside the current project folder. Never invent a new root path.
- Never delete or truncate existing functions/classes you are not changing.
- Never say 'Done' or 'Fixed' before you have seen a successful build/test output with your own eyes.
- Never make architectural changes (new folder structure, new state layer, DB changes) without explicit user approval.

## Workflow
- Before writing new code, run SearchCode to check if similar logic already exists. Reuse it.
- After every file change, run the relevant check (flutter analyze / npm run build / dotnet build).
- If a build fails, analyze the error, fix it, and re-run. Do this up to 3 times before asking the user.
- If still broken after 3 tries, run WebSearch with the exact error message before giving up.

## Communication
- Reply in the same language the user writes in.
- After finishing a set of tool calls, always write a short summary: what changed, which files, any warnings.
- Never output full file contents in chat unless the user explicitly asks.
- If the request is ambiguous, ask one clarifying question before starting.

## Code Style
- Match the existing indentation, naming, and import style of the file you are editing.
- No TODO comments or placeholder code in production files.
- Prefer editing the smallest possible diff. Touch only what needs to change.