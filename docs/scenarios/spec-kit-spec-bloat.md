# Spec Kit Spec-Bloat Scenario

Status: historical scenario. This document is not part of the Azure Production MVP implementation contract.

The active production implementation scope is defined by [Azure Production MVP PRD](../prd/azure-production-mvp.md), [Production Target State Spec](../specs/production-target-state.md), and [Production Implementation Roadmap](../planning/production-implementation-roadmap.md). This scenario remains background material for a future demonstration of spec bloat and context hygiene.

## Purpose

Describe the primary spec-bloat product scenario used to exercise Token Hotspot detection for specification-driven development artifacts.

This scenario is not a maintained fixture corpus. The observability platform must not depend on Spec Kit as a runtime service, and the repo does not maintain an expected hotspot fixture for this scenario.

## Scenario Target

The scenario target is an internal deployment approval tracker.

The team uses Spec Kit over several iterations:

* manual deployment approvals
* Slack notification workflow
* Azure environment gates
* audit log requirements
* RBAC requirements
* emergency override workflow

The current task is to work on the latest deployment approval workflow. Older specs, plans, and task files remain visible in the repo.

## Artifact Classification

Active context:

* current feature spec
* current plan
* current tasks
* project principles or constitution if present

Bloat:

* superseded feature specs
* old plans
* completed task files
* stale design logs
* duplicate generated artifacts
* archived specs still visible to the agent

Neutral context:

* generated task breakdowns or checklists that are not currently referenced

Neutral context counts toward repo context size but should not become a Token Hotspot unless it is repeated or visible in the expensive session.

## Expected Story

The scenario should not claim that Spec Kit itself is bad. The story is:

> Spec Kit creates valuable structure. Without repo hygiene and agent instructions, old specification artifacts can remain visible and become repeated context burn.

The expected recommendation is:

> Create `active-specs.md`, move superseded specs under `specs/archive/`, and configure agent instructions to load only active specs by default.

Expected benefit:

* reduce repeated context from stale specs and plans
* preserve audit history by archiving instead of deleting
* make the current spec workflow explicit to the agent

## Boundary

The repo should not maintain a separate Spec Kit fixture corpus or expected hotspot fixture for this scenario.

Parser and missing-metric behavior can still use small telemetry fixtures, but the spec-bloat use case is demonstrated through live Spec Kit artifact generation and captured telemetry.
