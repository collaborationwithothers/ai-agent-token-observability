# Manual Spec Kit Spec-Bloat Demo Runbook

Status: historical demo runbook. This document is not part of the Azure Production MVP implementation contract.

The active production implementation scope is defined by [Azure Production MVP PRD](../../prd/azure-production-mvp.md), [Production Target State Spec](../../specs/production-target-state.md), and [Production Implementation Roadmap](../../planning/production-implementation-roadmap.md). This runbook remains background material for a future demonstration scenario only.

## Purpose

Run the live presentation flow for the Spec Kit spec-bloat scenario.

Scenario details live in `docs/archive/demos/spec-kit-spec-bloat.md`. This runbook only describes the manual presentation path.

## Preconditions

* The presenter uses the scenario target from `docs/archive/demos/spec-kit-spec-bloat.md`.
* Telemetry capture is ready before the live Copilot session starts.
* The observability app can import the captured Copilot telemetry.
* Repo Context Enrichment can run against the same repository used during the live session.

## Demo Flow

1. Start telemetry capture.
2. Run Spec Kit manually to create or evolve the deployment approval tracker specs.
3. Show the resulting spec, plan, and task artifacts.
4. Add or leave older specs and plans visible in the repository.
5. Run a Copilot session against the current workflow while stale artifacts remain visible.
6. Import the captured Copilot telemetry into the observability app.
7. Run Repo Context Enrichment against the same repository.
8. Show the resulting Token Hotspot for stale spec artifacts.
9. Show the recommendation to create `active-specs.md`, move superseded specs under `specs/archive/`, and configure agent instructions to load only active specs by default.

## Boundary

This runbook is for presentation only. It does not define fixture requirements or product behavior beyond the scenario documented in `docs/archive/demos/spec-kit-spec-bloat.md`.

Once implementation exists, MVP acceptance requires this runbook to be executable end to end against the implemented app.
