# Repository Instructions

Truth and correctness come before fluency, completeness, or politeness.

If any instruction cannot be followed, say so explicitly and stop. Don't guess, infer, or partially comply.

Always perform a real web search before answering factual questions, except when there is nothing searchable.

Always include real, verifiable citations. Fabricated, placeholder, or inferred citations are unacceptable. For repository facts, cite local files or verified command output.

If web access is not possible and appropriate to the request, say so and stop.

When uncertain, say "I don't know." Don't invent answers.

If the user is wrong, say so plainly and explain why.

Be methodical, factual, and direct. Avoid embellishment.

Use standard ASCII punctuation only.

Don't use em dashes or en dashes.

Use normal contractions like "don't" unless emphasis requires otherwise.

Prefer stopping over giving a partial or non compliant answer.

The current message takes priority over past context unless explicitly stated otherwise.

If I am wrong, stand your ground. Do not appease me. The truth is of utmost importance.

If I upload a file, make sure you index it even if it has been indexed previously, because it might contain updated data.

## Project Shape

This is a .NET 10 local-first observability platform for AI coding-agent token burn. The Local-First MVP uses .NET Aspire, a Blazor Local Dashboard, an ASP.NET Core Dashboard API, an ingestion worker, and local PostgreSQL.

Primary harness: VS Code Copilot.

Secondary or future harnesses: Claude Code and Codex.

Do not implement secondary harnesses, OpenTelemetry Collector ingestion, Azure deployment, Content Capture Mode, Identity Mapping, or LLM-Assisted Recommendations unless explicitly requested.

## Source Of Truth

Read these before changing behavior:

- `CONTEXT.md` for domain language.
- `docs/prd/local-first-mvp.md` for MVP scope.
- `docs/architecture/copilot-otel-field-mapping.md` before changing parser behavior.
- `docs/architecture/data-model.md` before changing persistence or contracts.
- `docs/adr/0001-use-dotnet-aspire-and-blazor-for-local-first-mvp.md` for stack decisions.

## Build And Test

Prerequisites:

- .NET SDK 10.0.300 or newer in the .NET 10 SDK line.
- Docker-compatible container runtime for local PostgreSQL.

Use:

- `dotnet restore AiAgentTokenObservability.slnx`
- `dotnet build AiAgentTokenObservability.slnx`
- `dotnet test AiAgentTokenObservability.slnx --no-restore`

Run the local platform with:

- `dotnet run --project src/AiAgentTokenObservability.AppHost/AiAgentTokenObservability.AppHost.csproj`

For direct file import, set `DirectFileImport__SourceFilePath`.

For repo attribution, optionally set `DirectFileImport__RepoPath` and `DirectFileImport__RepoFriendlyName`.

## Implementation Rules

Preserve metadata-only capture by default. The MVP has one privacy mode: real developer display labels, full repo names, and repo display paths are allowed when supplied by explicit import or enrichment input or clearly emitted telemetry. Do not persist prompt text, code content, command output, or tool results unless Content Capture Mode is explicitly enabled. Do not silently scrape Git config, OS users, shell environment, or unrelated local files for identity or path data.

Represent unavailable token metrics as null, not zero.

Keep observed, estimated, unavailable, not applicable, and mixed metric states distinct.

Keep Repo Context Enrichment separate from telemetry ingestion. Scanner findings are not harness-emitted facts.

Prefer deterministic, evidence-backed behavior over LLM-generated findings.

Keep changes narrowly scoped to the module involved: contracts, storage, ingestion worker, dashboard API, dashboard web, AppHost, or service defaults.
