using AiAgentTokenObservability.Contracts.Sessions;
using AiAgentTokenObservability.Contracts.Insights;
using Npgsql;

namespace AiAgentTokenObservability.Storage;

public sealed class PostgresTelemetryStore(NpgsqlDataSource dataSource) : ITelemetryStore
{
    private bool _schemaInitialized;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);

    public async Task ReplaceImportAsync(TelemetryImportModel import, CancellationToken cancellationToken)
    {
        await EnsureSchemaCreatedAsync(cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM telemetry_import WHERE source_file_hash = @source_file_hash",
            cancellationToken,
            ("source_file_hash", import.SourceFileHash));

        await InsertImportAsync(connection, transaction, import, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DashboardSessionsResponse> ListSessionSummariesAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaCreatedAsync(cancellationToken);

        const string sql = """
            SELECT
                s.agent_session_id,
                s.harness,
                s.harness_source,
                s.harness_version,
                s.agent_name,
                s.started_at_utc,
                s.token_total_type,
                s.input_tokens,
                s.output_tokens,
                (SELECT COUNT(*) FROM agent_turn t WHERE t.agent_session_id = s.agent_session_id) AS turn_count,
                (SELECT COUNT(*) FROM tool_call tc WHERE tc.agent_session_id = s.agent_session_id) AS tool_call_count,
                (SELECT COUNT(*) FROM model_invocation mi WHERE mi.agent_session_id = s.agent_session_id) AS model_invocation_count,
                (SELECT COUNT(*) FROM workspace_repo wr WHERE wr.agent_session_id = s.agent_session_id) AS workspace_repo_count
            FROM agent_session s
            ORDER BY s.started_at_utc DESC NULLS LAST, s.agent_session_id DESC;
            """;

        var sessionRows = new List<DashboardSessionSummaryRow>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sessionRows.Add(new DashboardSessionSummaryRow(
                reader.GetGuid(0),
                reader.GetString(1),
                ReadString(reader, 2),
                ReadString(reader, 3),
                ReadString(reader, 4),
                ReadDateTimeOffset(reader, 5),
                new TokenSplitResponse(reader.GetString(6), ReadLong(reader, 7), ReadLong(reader, 8)),
                Convert.ToInt32(reader.GetInt64(9)),
                Convert.ToInt32(reader.GetInt64(10)),
                Convert.ToInt32(reader.GetInt64(11)),
                Convert.ToInt32(reader.GetInt64(12))));
        }

        await reader.DisposeAsync();

        var sessions = new List<DashboardSessionSummaryResponse>();
        foreach (var row in sessionRows)
        {
            var metrics = await ReadTokenMetricsAsync(connection, row.AgentSessionId, cancellationToken);
            sessions.Add(row.ToResponse(metrics));
        }

        return new DashboardSessionsResponse(sessions, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<WorkspaceRepoModel>> ListWorkspaceReposAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaCreatedAsync(cancellationToken);

        const string sql = """
            SELECT
                workspace_repo_id,
                agent_session_id,
                repo_friendly_name,
                repo_path_hash,
                repo_path
            FROM workspace_repo
            ORDER BY workspace_repo_id;
            """;

        var repos = new List<WorkspaceRepoModel>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            repos.Add(new WorkspaceRepoModel
            {
                WorkspaceRepoId = reader.GetGuid(0),
                AgentSessionId = reader.GetGuid(1),
                RepoFriendlyName = ReadString(reader, 2),
                RepoPathHash = reader.GetString(3),
                RepoPath = ReadString(reader, 4)
            });
        }

        return repos;
    }

    public async Task ReplaceRepoContextAsync(
        Guid workspaceRepoId,
        IReadOnlyList<ContextSourceModel> contextSources,
        IReadOnlyList<HotspotModel> hotspots,
        IReadOnlyList<RecommendationModel> recommendations,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaCreatedAsync(cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM recommendation
            WHERE hotspot_id IN (
                SELECT hotspot_id
                FROM hotspot
                WHERE workspace_repo_id = @workspace_repo_id
            )
            """,
            cancellationToken,
            ("workspace_repo_id", workspaceRepoId));

        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM hotspot WHERE workspace_repo_id = @workspace_repo_id",
            cancellationToken,
            ("workspace_repo_id", workspaceRepoId));

        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM context_source WHERE workspace_repo_id = @workspace_repo_id",
            cancellationToken,
            ("workspace_repo_id", workspaceRepoId));

        foreach (var contextSource in contextSources)
        {
            await InsertContextSourceAsync(connection, transaction, contextSource, cancellationToken);
        }

        foreach (var hotspot in hotspots)
        {
            await InsertHotspotAsync(connection, transaction, hotspot, cancellationToken);
        }

        foreach (var recommendation in recommendations)
        {
            await InsertRecommendationAsync(connection, transaction, recommendation, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DashboardInsightsResponse> ListInsightsAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaCreatedAsync(cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var contextSources = await ReadContextSourcesAsync(connection, cancellationToken);
        var recommendationsByHotspot = await ReadRecommendationsAsync(connection, cancellationToken);
        var hotspots = await ReadHotspotsAsync(connection, recommendationsByHotspot, cancellationToken);

        return new DashboardInsightsResponse(contextSources, hotspots, DateTimeOffset.UtcNow);
    }

    private static async Task<IReadOnlyList<TokenMetricResponse>> ReadTokenMetricsAsync(
        NpgsqlConnection connection,
        Guid agentSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                tm.metric_name,
                tm.metric_status,
                tm.metric_confidence,
                tm.value
            FROM token_metric tm
            LEFT JOIN model_invocation mi
                ON mi.model_invocation_id = tm.model_invocation_id
            WHERE tm.agent_session_id = @agent_session_id
                OR mi.agent_session_id = @agent_session_id
            ORDER BY tm.metric_name, tm.token_metric_id;
            """;

        var metrics = new List<TokenMetricResponse>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_session_id", agentSessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            metrics.Add(new TokenMetricResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadLong(reader, 3)));
        }

        return metrics;
    }

    private async Task InsertImportAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TelemetryImportModel import,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO telemetry_import (
                telemetry_import_id,
                harness,
                source_kind,
                source_file_hash,
                environment_name,
                started_at_utc,
                completed_at_utc,
                import_status,
                record_count,
                skipped_record_count,
                warning_count,
                error_count)
            VALUES (
                @telemetry_import_id,
                @harness,
                @source_kind,
                @source_file_hash,
                @environment_name,
                @started_at_utc,
                @completed_at_utc,
                @import_status,
                @record_count,
                @skipped_record_count,
                @warning_count,
                @error_count)
            """,
            cancellationToken,
            ("telemetry_import_id", import.TelemetryImportId),
            ("harness", import.Harness),
            ("source_kind", import.SourceKind),
            ("source_file_hash", import.SourceFileHash),
            ("environment_name", import.EnvironmentName),
            ("started_at_utc", import.StartedAtUtc),
            ("completed_at_utc", import.CompletedAtUtc),
            ("import_status", import.ImportStatus),
            ("record_count", import.RecordCount),
            ("skipped_record_count", import.SkippedRecordCount),
            ("warning_count", import.WarningCount),
            ("error_count", import.ErrorCount));

        await InsertSessionAsync(connection, transaction, import.Session, cancellationToken);

        foreach (var record in import.TelemetryRecords)
        {
            await InsertTelemetryRecordAsync(connection, transaction, record, cancellationToken);
        }

        foreach (var turn in import.AgentTurns)
        {
            await InsertAgentTurnAsync(connection, transaction, turn, cancellationToken);
        }

        foreach (var invocation in import.ModelInvocations)
        {
            await InsertModelInvocationAsync(connection, transaction, invocation, cancellationToken);
        }

        foreach (var metric in import.TokenMetrics)
        {
            await InsertTokenMetricAsync(connection, transaction, metric, cancellationToken);
        }

        foreach (var toolCall in import.ToolCalls)
        {
            await InsertToolCallAsync(connection, transaction, toolCall, cancellationToken);
        }

        foreach (var repo in import.WorkspaceRepos)
        {
            await InsertWorkspaceRepoAsync(connection, transaction, repo, cancellationToken);
        }

        foreach (var metricObservation in import.MetricObservations)
        {
            await InsertMetricObservationAsync(connection, transaction, metricObservation, cancellationToken);
        }
    }

    private static Task InsertSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AgentSessionModel session,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO agent_session (
                agent_session_id,
                telemetry_import_id,
                harness,
                harness_source,
                harness_version,
                agent_name,
                provider_session_id_hash,
                team_hash,
                user_hash,
                started_at_utc,
                token_total_type,
                input_tokens,
                output_tokens,
                content_captured)
            VALUES (
                @agent_session_id,
                @telemetry_import_id,
                @harness,
                @harness_source,
                @harness_version,
                @agent_name,
                @provider_session_id_hash,
                @team_hash,
                @user_hash,
                @started_at_utc,
                @token_total_type,
                @input_tokens,
                @output_tokens,
                @content_captured)
            """,
            cancellationToken,
            ("agent_session_id", session.AgentSessionId),
            ("telemetry_import_id", session.TelemetryImportId),
            ("harness", session.Harness),
            ("harness_source", session.HarnessSource),
            ("harness_version", session.HarnessVersion),
            ("agent_name", session.AgentName),
            ("provider_session_id_hash", session.ProviderSessionIdHash),
            ("team_hash", session.TeamHash),
            ("user_hash", session.UserHash),
            ("started_at_utc", session.StartedAtUtc),
            ("token_total_type", session.TokenTotalType),
            ("input_tokens", session.InputTokens),
            ("output_tokens", session.OutputTokens),
            ("content_captured", session.ContentCaptured));
    }

    private static Task InsertTelemetryRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TelemetryRecordModel record,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO telemetry_record (
                telemetry_record_id,
                telemetry_import_id,
                agent_session_id,
                record_index,
                record_kind,
                body_redacted_summary,
                event_name,
                trace_id_hash,
                span_id_hash,
                trace_flags,
                observed_at_utc,
                received_at_utc,
                instrumentation_scope_name,
                instrumentation_scope_version,
                attribute_count)
            VALUES (
                @telemetry_record_id,
                @telemetry_import_id,
                @agent_session_id,
                @record_index,
                @record_kind,
                @body_redacted_summary,
                @event_name,
                @trace_id_hash,
                @span_id_hash,
                @trace_flags,
                @observed_at_utc,
                @received_at_utc,
                @instrumentation_scope_name,
                @instrumentation_scope_version,
                @attribute_count)
            """,
            cancellationToken,
            ("telemetry_record_id", record.TelemetryRecordId),
            ("telemetry_import_id", record.TelemetryImportId),
            ("agent_session_id", record.AgentSessionId),
            ("record_index", record.RecordIndex),
            ("record_kind", record.RecordKind),
            ("body_redacted_summary", record.BodyRedactedSummary),
            ("event_name", record.EventName),
            ("trace_id_hash", record.TraceIdHash),
            ("span_id_hash", record.SpanIdHash),
            ("trace_flags", record.TraceFlags),
            ("observed_at_utc", record.ObservedAtUtc),
            ("received_at_utc", record.ReceivedAtUtc),
            ("instrumentation_scope_name", record.InstrumentationScopeName),
            ("instrumentation_scope_version", record.InstrumentationScopeVersion),
            ("attribute_count", record.AttributeCount));
    }

    private static Task InsertAgentTurnAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AgentTurnModel turn,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO agent_turn (
                agent_turn_id,
                agent_session_id,
                telemetry_record_id,
                turn_index,
                tool_call_count,
                success,
                started_at_utc)
            VALUES (
                @agent_turn_id,
                @agent_session_id,
                @telemetry_record_id,
                @turn_index,
                @tool_call_count,
                @success,
                @started_at_utc)
            """,
            cancellationToken,
            ("agent_turn_id", turn.AgentTurnId),
            ("agent_session_id", turn.AgentSessionId),
            ("telemetry_record_id", turn.TelemetryRecordId),
            ("turn_index", turn.TurnIndex),
            ("tool_call_count", turn.ToolCallCount),
            ("success", turn.Success),
            ("started_at_utc", turn.StartedAtUtc));
    }

    private static Task InsertModelInvocationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelInvocationModel invocation,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO model_invocation (
                model_invocation_id,
                agent_session_id,
                agent_turn_id,
                telemetry_record_id,
                operation_name,
                provider_name,
                request_model,
                response_model,
                provider_response_id_hash,
                finish_reasons_json,
                request_max_tokens,
                request_temperature,
                input_tokens,
                output_tokens,
                token_total_type)
            VALUES (
                @model_invocation_id,
                @agent_session_id,
                @agent_turn_id,
                @telemetry_record_id,
                @operation_name,
                @provider_name,
                @request_model,
                @response_model,
                @provider_response_id_hash,
                CAST(@finish_reasons_json AS jsonb),
                @request_max_tokens,
                @request_temperature,
                @input_tokens,
                @output_tokens,
                @token_total_type)
            """,
            cancellationToken,
            ("model_invocation_id", invocation.ModelInvocationId),
            ("agent_session_id", invocation.AgentSessionId),
            ("agent_turn_id", invocation.AgentTurnId),
            ("telemetry_record_id", invocation.TelemetryRecordId),
            ("operation_name", invocation.OperationName),
            ("provider_name", invocation.ProviderName),
            ("request_model", invocation.RequestModel),
            ("response_model", invocation.ResponseModel),
            ("provider_response_id_hash", invocation.ProviderResponseIdHash),
            ("finish_reasons_json", invocation.FinishReasonsJson),
            ("request_max_tokens", invocation.RequestMaxTokens),
            ("request_temperature", invocation.RequestTemperature),
            ("input_tokens", invocation.InputTokens),
            ("output_tokens", invocation.OutputTokens),
            ("token_total_type", invocation.TokenTotalType));
    }

    private static Task InsertTokenMetricAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TokenMetricModel metric,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO token_metric (
                token_metric_id,
                model_invocation_id,
                agent_session_id,
                metric_name,
                metric_status,
                metric_confidence,
                value,
                source)
            VALUES (
                @token_metric_id,
                @model_invocation_id,
                @agent_session_id,
                @metric_name,
                @metric_status,
                @metric_confidence,
                @value,
                @source)
            """,
            cancellationToken,
            ("token_metric_id", metric.TokenMetricId),
            ("model_invocation_id", metric.ModelInvocationId),
            ("agent_session_id", metric.AgentSessionId),
            ("metric_name", metric.MetricName),
            ("metric_status", metric.MetricStatus),
            ("metric_confidence", metric.MetricConfidence),
            ("value", metric.Value),
            ("source", metric.Source));
    }

    private static Task InsertToolCallAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolCallModel toolCall,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO tool_call (
                tool_call_id,
                agent_session_id,
                agent_turn_id,
                telemetry_record_id,
                tool_name,
                duration_ms,
                success,
                arguments_captured,
                result_captured)
            VALUES (
                @tool_call_id,
                @agent_session_id,
                @agent_turn_id,
                @telemetry_record_id,
                @tool_name,
                @duration_ms,
                @success,
                @arguments_captured,
                @result_captured)
            """,
            cancellationToken,
            ("tool_call_id", toolCall.ToolCallId),
            ("agent_session_id", toolCall.AgentSessionId),
            ("agent_turn_id", toolCall.AgentTurnId),
            ("telemetry_record_id", toolCall.TelemetryRecordId),
            ("tool_name", toolCall.ToolName),
            ("duration_ms", toolCall.DurationMs),
            ("success", toolCall.Success),
            ("arguments_captured", toolCall.ArgumentsCaptured),
            ("result_captured", toolCall.ResultCaptured));
    }

    private static Task InsertWorkspaceRepoAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WorkspaceRepoModel repo,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO workspace_repo (
                workspace_repo_id,
                agent_session_id,
                repo_friendly_name,
                repo_path_hash,
                repo_path)
            VALUES (
                @workspace_repo_id,
                @agent_session_id,
                @repo_friendly_name,
                @repo_path_hash,
                @repo_path)
            """,
            cancellationToken,
            ("workspace_repo_id", repo.WorkspaceRepoId),
            ("agent_session_id", repo.AgentSessionId),
            ("repo_friendly_name", repo.RepoFriendlyName),
            ("repo_path_hash", repo.RepoPathHash),
            ("repo_path", repo.RepoPath));
    }

    private static Task InsertMetricObservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MetricObservationModel observation,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO metric_observation (
                metric_observation_id,
                telemetry_import_id,
                agent_session_id,
                scope_name,
                scope_version,
                metric_name,
                metric_type,
                value_type,
                unit,
                description,
                aggregation_temporality,
                is_monotonic,
                data_point_type,
                start_time_utc,
                end_time_utc,
                attributes_json,
                value_json,
                bucket_boundaries_json)
            VALUES (
                @metric_observation_id,
                @telemetry_import_id,
                @agent_session_id,
                @scope_name,
                @scope_version,
                @metric_name,
                @metric_type,
                @value_type,
                @unit,
                @description,
                @aggregation_temporality,
                @is_monotonic,
                @data_point_type,
                @start_time_utc,
                @end_time_utc,
                CAST(@attributes_json AS jsonb),
                CAST(@value_json AS jsonb),
                CAST(@bucket_boundaries_json AS jsonb))
            """,
            cancellationToken,
            ("metric_observation_id", observation.MetricObservationId),
            ("telemetry_import_id", observation.TelemetryImportId),
            ("agent_session_id", observation.AgentSessionId),
            ("scope_name", observation.ScopeName),
            ("scope_version", observation.ScopeVersion),
            ("metric_name", observation.MetricName),
            ("metric_type", observation.MetricType),
            ("value_type", observation.ValueType),
            ("unit", observation.Unit),
            ("description", observation.Description),
            ("aggregation_temporality", observation.AggregationTemporality),
            ("is_monotonic", observation.IsMonotonic),
            ("data_point_type", observation.DataPointType),
            ("start_time_utc", observation.StartTimeUtc),
            ("end_time_utc", observation.EndTimeUtc),
            ("attributes_json", observation.AttributesJson),
            ("value_json", observation.ValueJson),
            ("bucket_boundaries_json", observation.BucketBoundariesJson));
    }

    private static Task InsertContextSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContextSourceModel source,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO context_source (
                context_source_id,
                workspace_repo_id,
                source_type,
                path_hash,
                display_path,
                file_category,
                spec_artifact_status,
                eligible_for_inferred_hotspot,
                size_bytes,
                line_count)
            VALUES (
                @context_source_id,
                @workspace_repo_id,
                @source_type,
                @path_hash,
                @display_path,
                @file_category,
                @spec_artifact_status,
                @eligible_for_inferred_hotspot,
                @size_bytes,
                @line_count)
            """,
            cancellationToken,
            ("context_source_id", source.ContextSourceId),
            ("workspace_repo_id", source.WorkspaceRepoId),
            ("source_type", source.SourceType),
            ("path_hash", source.PathHash),
            ("display_path", source.DisplayPath),
            ("file_category", source.FileCategory),
            ("spec_artifact_status", source.SpecArtifactStatus),
            ("eligible_for_inferred_hotspot", source.EligibleForInferredHotspot),
            ("size_bytes", source.SizeBytes),
            ("line_count", source.LineCount));
    }

    private static Task InsertHotspotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HotspotModel hotspot,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO hotspot (
                hotspot_id,
                agent_session_id,
                workspace_repo_id,
                source_type,
                source_ref,
                attribution_type,
                confidence,
                suspected_cause,
                evidence_refs_json,
                token_burn_score)
            VALUES (
                @hotspot_id,
                @agent_session_id,
                @workspace_repo_id,
                @source_type,
                @source_ref,
                @attribution_type,
                @confidence,
                @suspected_cause,
                CAST(@evidence_refs_json AS jsonb),
                @token_burn_score)
            """,
            cancellationToken,
            ("hotspot_id", hotspot.HotspotId),
            ("agent_session_id", hotspot.AgentSessionId),
            ("workspace_repo_id", hotspot.WorkspaceRepoId),
            ("source_type", hotspot.SourceType),
            ("source_ref", hotspot.SourceRef),
            ("attribution_type", hotspot.AttributionType),
            ("confidence", hotspot.Confidence),
            ("suspected_cause", hotspot.SuspectedCause),
            ("evidence_refs_json", hotspot.EvidenceRefsJson),
            ("token_burn_score", hotspot.TokenBurnScore));
    }

    private static Task InsertRecommendationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RecommendationModel recommendation,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO recommendation (
                recommendation_id,
                hotspot_id,
                recommendation_type,
                rule_id,
                trigger_condition,
                recommended_action,
                expected_benefit,
                confidence,
                evidence_refs_json)
            VALUES (
                @recommendation_id,
                @hotspot_id,
                @recommendation_type,
                @rule_id,
                @trigger_condition,
                @recommended_action,
                @expected_benefit,
                @confidence,
                CAST(@evidence_refs_json AS jsonb))
            """,
            cancellationToken,
            ("recommendation_id", recommendation.RecommendationId),
            ("hotspot_id", recommendation.HotspotId),
            ("recommendation_type", recommendation.RecommendationType),
            ("rule_id", recommendation.RuleId),
            ("trigger_condition", recommendation.TriggerCondition),
            ("recommended_action", recommendation.RecommendedAction),
            ("expected_benefit", recommendation.ExpectedBenefit),
            ("confidence", recommendation.Confidence),
            ("evidence_refs_json", recommendation.EvidenceRefsJson));
    }

    private static async Task<IReadOnlyList<ContextSourceResponse>> ReadContextSourcesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                context_source_id,
                workspace_repo_id,
                source_type,
                path_hash,
                display_path,
                file_category,
                spec_artifact_status,
                eligible_for_inferred_hotspot,
                size_bytes,
                line_count
            FROM context_source
            ORDER BY file_category, spec_artifact_status NULLS LAST, context_source_id;
            """;

        var sources = new List<ContextSourceResponse>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sources.Add(new ContextSourceResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                ReadString(reader, 4),
                reader.GetString(5),
                ReadString(reader, 6),
                reader.GetBoolean(7),
                ReadLong(reader, 8),
                ReadInt(reader, 9)));
        }

        return sources;
    }

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RecommendationResponse>>> ReadRecommendationsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                recommendation_id,
                hotspot_id,
                recommendation_type,
                rule_id,
                trigger_condition,
                recommended_action,
                expected_benefit,
                confidence,
                evidence_refs_json::text
            FROM recommendation
            ORDER BY recommendation_id;
            """;

        var recommendations = new List<RecommendationResponse>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            recommendations.Add(new RecommendationResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return recommendations
            .GroupBy(recommendation => recommendation.HotspotId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RecommendationResponse>)group.ToArray());
    }

    private static async Task<IReadOnlyList<HotspotResponse>> ReadHotspotsAsync(
        NpgsqlConnection connection,
        IReadOnlyDictionary<Guid, IReadOnlyList<RecommendationResponse>> recommendationsByHotspot,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                hotspot_id,
                agent_session_id,
                workspace_repo_id,
                source_type,
                source_ref,
                attribution_type,
                confidence,
                suspected_cause,
                evidence_refs_json::text,
                token_burn_score
            FROM hotspot
            ORDER BY token_burn_score DESC NULLS LAST, hotspot_id;
            """;

        var hotspots = new List<HotspotResponse>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var hotspotId = reader.GetGuid(0);
            hotspots.Add(new HotspotResponse(
                hotspotId,
                ReadGuid(reader, 1),
                ReadGuid(reader, 2),
                reader.GetString(3),
                ReadString(reader, 4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                ReadDecimal(reader, 9),
                recommendationsByHotspot.TryGetValue(hotspotId, out var recommendations) ? recommendations : []));
        }

        return hotspots;
    }

    private async Task EnsureSchemaCreatedAsync(CancellationToken cancellationToken)
    {
        if (_schemaInitialized)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaInitialized)
            {
                return;
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await ExecuteAsync(connection, null, SchemaSql, cancellationToken);
            _schemaInitialized = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? ReadString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? ReadLong(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static int? ReadInt(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static Guid? ReadGuid(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static decimal? ReadDecimal(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static DateTimeOffset? ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record DashboardSessionSummaryRow(
        Guid AgentSessionId,
        string Harness,
        string? HarnessSource,
        string? HarnessVersion,
        string? AgentName,
        DateTimeOffset? StartedAtUtc,
        TokenSplitResponse TokenSplit,
        int TurnCount,
        int ToolCallCount,
        int ModelInvocationCount,
        int WorkspaceRepoCount)
    {
        public DashboardSessionSummaryResponse ToResponse(IReadOnlyList<TokenMetricResponse> metrics)
        {
            return new DashboardSessionSummaryResponse(
                AgentSessionId,
                Harness,
                HarnessSource,
                HarnessVersion,
                AgentName,
                StartedAtUtc,
                TokenSplit with { Metrics = metrics },
                TurnCount,
                ToolCallCount,
                ModelInvocationCount,
                WorkspaceRepoCount);
        }
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS telemetry_import (
            telemetry_import_id uuid PRIMARY KEY,
            harness text NOT NULL,
            source_kind text NOT NULL,
            source_file_hash text NULL,
            environment_name text NULL,
            started_at_utc timestamptz NOT NULL,
            completed_at_utc timestamptz NULL,
            import_status text NOT NULL,
            record_count integer NOT NULL,
            skipped_record_count integer NOT NULL,
            warning_count integer NOT NULL,
            error_count integer NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_telemetry_import_source_file_hash
            ON telemetry_import(source_file_hash);

        CREATE TABLE IF NOT EXISTS agent_session (
            agent_session_id uuid PRIMARY KEY,
            telemetry_import_id uuid NOT NULL REFERENCES telemetry_import(telemetry_import_id) ON DELETE CASCADE,
            harness text NOT NULL,
            harness_source text NULL,
            harness_version text NULL,
            agent_name text NULL,
            provider_session_id_hash text NULL,
            team_hash text NULL,
            user_hash text NULL,
            started_at_utc timestamptz NULL,
            token_total_type text NOT NULL,
            input_tokens bigint NULL,
            output_tokens bigint NULL,
            content_captured boolean NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_agent_session_harness_started_at_utc
            ON agent_session(harness, started_at_utc);

        CREATE INDEX IF NOT EXISTS ix_agent_session_token_total_type
            ON agent_session(token_total_type);

        CREATE TABLE IF NOT EXISTS workspace_repo (
            workspace_repo_id uuid PRIMARY KEY,
            agent_session_id uuid NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            repo_friendly_name text NULL,
            repo_path_hash text NOT NULL,
            repo_path text NULL,
            branch_name_hash text NULL
        );

        CREATE INDEX IF NOT EXISTS ix_workspace_repo_repo_path_hash
            ON workspace_repo(repo_path_hash);

        CREATE TABLE IF NOT EXISTS telemetry_record (
            telemetry_record_id uuid PRIMARY KEY,
            telemetry_import_id uuid NOT NULL REFERENCES telemetry_import(telemetry_import_id) ON DELETE CASCADE,
            agent_session_id uuid NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            record_index integer NOT NULL,
            record_kind text NOT NULL,
            body_redacted_summary text NULL,
            event_name text NULL,
            trace_id_hash text NULL,
            span_id_hash text NULL,
            trace_flags integer NULL,
            observed_at_utc timestamptz NULL,
            received_at_utc timestamptz NULL,
            instrumentation_scope_name text NULL,
            instrumentation_scope_version text NULL,
            attribute_count integer NULL
        );

        CREATE TABLE IF NOT EXISTS agent_turn (
            agent_turn_id uuid PRIMARY KEY,
            agent_session_id uuid NOT NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            telemetry_record_id uuid NULL REFERENCES telemetry_record(telemetry_record_id) ON DELETE SET NULL,
            turn_index integer NULL,
            tool_call_count integer NULL,
            success boolean NULL,
            started_at_utc timestamptz NULL,
            ended_at_utc timestamptz NULL
        );

        CREATE INDEX IF NOT EXISTS ix_agent_turn_agent_session_id_turn_index
            ON agent_turn(agent_session_id, turn_index);

        CREATE TABLE IF NOT EXISTS model_invocation (
            model_invocation_id uuid PRIMARY KEY,
            agent_session_id uuid NOT NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            agent_turn_id uuid NULL REFERENCES agent_turn(agent_turn_id) ON DELETE SET NULL,
            telemetry_record_id uuid NOT NULL REFERENCES telemetry_record(telemetry_record_id) ON DELETE CASCADE,
            operation_name text NULL,
            provider_name text NULL,
            request_model text NULL,
            response_model text NULL,
            provider_response_id_hash text NULL,
            finish_reasons_json jsonb NULL,
            request_max_tokens integer NULL,
            request_temperature numeric NULL,
            input_tokens bigint NULL,
            output_tokens bigint NULL,
            token_total_type text NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_model_invocation_agent_session_id
            ON model_invocation(agent_session_id);

        CREATE TABLE IF NOT EXISTS token_metric (
            token_metric_id uuid PRIMARY KEY,
            model_invocation_id uuid NULL REFERENCES model_invocation(model_invocation_id) ON DELETE CASCADE,
            agent_session_id uuid NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            metric_name text NOT NULL,
            metric_status text NOT NULL,
            metric_confidence text NOT NULL,
            value bigint NULL,
            source text NOT NULL
        );

        CREATE TABLE IF NOT EXISTS tool_call (
            tool_call_id uuid PRIMARY KEY,
            agent_session_id uuid NOT NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            agent_turn_id uuid NULL REFERENCES agent_turn(agent_turn_id) ON DELETE SET NULL,
            telemetry_record_id uuid NULL REFERENCES telemetry_record(telemetry_record_id) ON DELETE SET NULL,
            tool_name text NULL,
            duration_ms bigint NULL,
            success boolean NULL,
            arguments_captured boolean NOT NULL,
            result_captured boolean NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_tool_call_agent_session_id_tool_name
            ON tool_call(agent_session_id, tool_name);

        CREATE TABLE IF NOT EXISTS metric_observation (
            metric_observation_id uuid PRIMARY KEY,
            telemetry_import_id uuid NOT NULL REFERENCES telemetry_import(telemetry_import_id) ON DELETE CASCADE,
            agent_session_id uuid NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            scope_name text NULL,
            scope_version text NULL,
            metric_name text NOT NULL,
            metric_type text NOT NULL,
            value_type integer NULL,
            unit text NULL,
            description text NULL,
            aggregation_temporality integer NULL,
            is_monotonic boolean NULL,
            data_point_type integer NULL,
            start_time_utc timestamptz NULL,
            end_time_utc timestamptz NULL,
            attributes_json jsonb NULL,
            value_json jsonb NULL,
            bucket_boundaries_json jsonb NULL
        );

        CREATE INDEX IF NOT EXISTS ix_metric_observation_agent_session_id_metric_name
            ON metric_observation(agent_session_id, metric_name);

        CREATE TABLE IF NOT EXISTS context_source (
            context_source_id uuid PRIMARY KEY,
            workspace_repo_id uuid NOT NULL REFERENCES workspace_repo(workspace_repo_id) ON DELETE CASCADE,
            source_type text NOT NULL,
            path_hash text NOT NULL,
            display_path text NULL,
            file_category text NOT NULL,
            spec_artifact_status text NULL,
            eligible_for_inferred_hotspot boolean NOT NULL,
            size_bytes bigint NULL,
            line_count integer NULL
        );

        CREATE INDEX IF NOT EXISTS ix_context_source_workspace_repo_id_file_category
            ON context_source(workspace_repo_id, file_category);

        CREATE INDEX IF NOT EXISTS ix_context_source_workspace_repo_id_spec_artifact_status
            ON context_source(workspace_repo_id, spec_artifact_status);

        CREATE TABLE IF NOT EXISTS hotspot (
            hotspot_id uuid PRIMARY KEY,
            agent_session_id uuid NULL REFERENCES agent_session(agent_session_id) ON DELETE CASCADE,
            workspace_repo_id uuid NULL REFERENCES workspace_repo(workspace_repo_id) ON DELETE CASCADE,
            source_type text NOT NULL,
            source_ref text NULL,
            attribution_type text NOT NULL,
            confidence text NOT NULL,
            suspected_cause text NOT NULL,
            evidence_refs_json jsonb NOT NULL,
            token_burn_score numeric NULL
        );

        CREATE INDEX IF NOT EXISTS ix_hotspot_agent_session_id_token_burn_score
            ON hotspot(agent_session_id, token_burn_score);

        CREATE INDEX IF NOT EXISTS ix_hotspot_workspace_repo_id_attribution_type
            ON hotspot(workspace_repo_id, attribution_type);

        CREATE TABLE IF NOT EXISTS recommendation (
            recommendation_id uuid PRIMARY KEY,
            hotspot_id uuid NOT NULL REFERENCES hotspot(hotspot_id) ON DELETE CASCADE,
            recommendation_type text NOT NULL,
            rule_id text NOT NULL,
            trigger_condition text NOT NULL,
            recommended_action text NOT NULL,
            expected_benefit text NOT NULL,
            confidence text NOT NULL,
            evidence_refs_json jsonb NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_recommendation_hotspot_id
            ON recommendation(hotspot_id);
        """;
}
