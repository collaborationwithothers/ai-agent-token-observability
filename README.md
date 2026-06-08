# ai-agent-token-observability
A production-style observability platform that ingests telemetry from AI coding agents such as VS Code Copilot, Claude Code, and Codex, normalises token usage across harnesses, and identifies the files, tools, prompts, specs, MCP calls, and agent behaviours causing token burn.

## Local App Platform startup

Prerequisites:

* .NET SDK 10.0.300 or newer in the .NET 10 SDK line.
* A running Docker-compatible container runtime for the local PostgreSQL resource.

Restore and run the Aspire AppHost:

```bash
dotnet restore AiAgentTokenObservability.slnx
dotnet run --project src/AiAgentTokenObservability.AppHost/AiAgentTokenObservability.AppHost.csproj
```

The AppHost starts the Local Store and Local Pipeline Projects together:

* `postgres` with database `tokenobservability`
* `ingestion-worker`
* `dashboard-api`
* `dashboard-web`

Use the Aspire dashboard resource links to open `dashboard-web`. The Local Dashboard calls the Dashboard API and renders the current Local App Platform status.

To check the API directly, open the `dashboard-api` endpoint from the Aspire dashboard and request:

```text
/status
```

## Direct File Import

Run the local platform and trigger the ingestion worker with an explicit Copilot JSONL path:

```bash
DirectFileImport__SourceFilePath=.local/copilot-otel.jsonl \
  dotnet run --project src/AiAgentTokenObservability.AppHost/AiAgentTokenObservability.AppHost.csproj
```

The import replaces any previous import with the same source file content hash. Workspace repo attribution is created when a repo path is explicitly supplied:

```bash
DirectFileImport__SourceFilePath=.local/copilot-otel.jsonl \
DirectFileImport__RepoPath=/path/to/repo \
DirectFileImport__RepoFriendlyName=repo \
  dotnet run --project src/AiAgentTokenObservability.AppHost/AiAgentTokenObservability.AppHost.csproj
```

The worker also supports a standalone `--import` command, but it requires `ConnectionStrings__tokenobservability` to point at an existing PostgreSQL database because the AppHost is what normally supplies the local connection string.

Imported sessions are available from the Dashboard API:

```text
/sessions
```

## Repo Context Enrichment

After importing a session with `DirectFileImport__RepoPath`, run Repo Context Enrichment against the same repo path:

```bash
ConnectionStrings__tokenobservability="<postgres connection string>" \
  dotnet run --project src/AiAgentTokenObservability.Ingestion.Worker/AiAgentTokenObservability.Ingestion.Worker.csproj -- \
  --enrich-repo /path/to/repo
```

Repo Context Enrichment is separate from Direct File Import. It scans the explicitly supplied repo path, persists metadata-only context sources, classifies file categories and spec artifact status, and creates the Rule 2 superseded spec-bloat Token Hotspot plus Deterministic Recommendation when stale spec artifacts are present.

Context sources, Token Hotspots, and recommendations are available from the Dashboard API:

```text
/insights
```

Collector ingestion, Azure deployment, and the Manual Spec Kit demo runbook verification are not implemented in this slice.
