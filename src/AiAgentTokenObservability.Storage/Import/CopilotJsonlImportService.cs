using System.Globalization;
using System.Text.Json;
using AiAgentTokenObservability.Storage.Infrastructure;

namespace AiAgentTokenObservability.Storage.Import;

public sealed class CopilotJsonlImportService(ITelemetryStore store, ISystemClock clock)
{
    public async Task<CopilotJsonlImportResult> ImportAsync(
        CopilotJsonlImportRequest request,
        CancellationToken cancellationToken)
    {
        var sourceFilePath = ResolveSourceFilePath(request.SourceFilePath);
        var sourceBytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
        var sourceHash = PrivacyHash.ContentHash(sourceBytes);
        var startedAtUtc = clock.UtcNow;
        var importId = Guid.NewGuid();
        var session = new AgentSessionModel
        {
            TelemetryImportId = importId,
            Harness = "copilot",
            TokenTotalType = "unavailable",
            UserHash = string.IsNullOrWhiteSpace(request.DeveloperIdentity)
                ? null
                : PrivacyHash.UserIdentity(request.DeveloperIdentity),
            ContentCaptured = false
        };
        var import = new TelemetryImportModel
        {
            TelemetryImportId = importId,
            Harness = "copilot",
            SourceKind = "direct_file",
            SourceFileHash = sourceHash,
            StartedAtUtc = startedAtUtc,
            ImportStatus = "succeeded",
            Session = session
        };

        if (!string.IsNullOrWhiteSpace(request.RepoPath))
        {
            import.WorkspaceRepos.Add(new WorkspaceRepoModel
            {
                AgentSessionId = session.AgentSessionId,
                RepoFriendlyName = string.IsNullOrWhiteSpace(request.RepoFriendlyName) ? null : request.RepoFriendlyName,
                RepoPathHash = PrivacyHash.RepoPath(request.RepoPath),
                RepoPath = null
            });
        }

        AgentTurnModel? lastTurn = null;
        var recordIndex = 0;

        await foreach (var line in File.ReadLinesAsync(sourceFilePath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                import.SkippedRecordCount++;
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Any())
            {
                import.SkippedRecordCount++;
                continue;
            }

            import.RecordCount++;
            recordIndex++;

            if (root.TryGetProperty("resource", out var resourceElement))
            {
                ApplyResourceAttributes(import, resourceElement);
            }

            if (root.TryGetProperty("_body", out _))
            {
                var record = CreateLogRecord(import, root, recordIndex);
                import.TelemetryRecords.Add(record);
                var eventName = record.EventName;
                var attributes = TryGetObject(root, "attributes");

                ApplyLogAttributes(session, record, attributes);

                if (eventName == "copilot_chat.session.start" && session.StartedAtUtc is null)
                {
                    session.StartedAtUtc = record.ObservedAtUtc;
                }
                else if (eventName == "copilot_chat.agent.turn")
                {
                    lastTurn = CreateAgentTurn(session.AgentSessionId, record, attributes);
                    import.AgentTurns.Add(lastTurn);
                }
                else if (eventName == "copilot_chat.tool.call")
                {
                    import.ToolCalls.Add(CreateToolCall(session.AgentSessionId, lastTurn?.AgentTurnId, record, attributes));
                }
                else if (eventName == "gen_ai.client.inference.operation.details")
                {
                    var invocation = CreateModelInvocation(session.AgentSessionId, lastTurn?.AgentTurnId, record, attributes);
                    import.ModelInvocations.Add(invocation);
                    import.TokenMetrics.Add(CreateInvocationTokenMetric(invocation.ModelInvocationId, "input_tokens", invocation.InputTokens));
                    import.TokenMetrics.Add(CreateInvocationTokenMetric(invocation.ModelInvocationId, "output_tokens", invocation.OutputTokens));
                }

                continue;
            }

            if (root.TryGetProperty("scopeMetrics", out var scopeMetrics))
            {
                var record = CreateMetricRecord(import, recordIndex);
                import.TelemetryRecords.Add(record);
                import.MetricObservations.AddRange(CreateMetricObservations(import.TelemetryImportId, session.AgentSessionId, scopeMetrics));
                continue;
            }

            if (root.TryGetProperty("resource", out _))
            {
                import.TelemetryRecords.Add(new TelemetryRecordModel
                {
                    TelemetryImportId = import.TelemetryImportId,
                    AgentSessionId = session.AgentSessionId,
                    RecordIndex = recordIndex,
                    RecordKind = "resource"
                });
            }
        }

        ApplySessionTokenTotals(import);
        import.CompletedAtUtc = clock.UtcNow;
        await store.ReplaceImportAsync(import, cancellationToken);

        return new CopilotJsonlImportResult(
            import.TelemetryImportId,
            import.SourceFileHash,
            import.ImportStatus,
            import.RecordCount,
            import.SkippedRecordCount,
            import.WarningCount,
            import.ErrorCount);
    }

    private static void ApplyResourceAttributes(TelemetryImportModel import, JsonElement resource)
    {
        if (!resource.TryGetProperty("_rawAttributes", out var rawAttributes) || rawAttributes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var attribute in rawAttributes.EnumerateArray())
        {
            if (attribute.ValueKind != JsonValueKind.Array || attribute.GetArrayLength() < 2)
            {
                continue;
            }

            var name = attribute[0].GetString();
            var value = attribute[1].ValueKind == JsonValueKind.String ? attribute[1].GetString() : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (name)
            {
                case "deployment.environment":
                    import.EnvironmentName = value;
                    break;
                case "service.name":
                    import.Session.HarnessSource = value;
                    break;
                case "service.version":
                    import.Session.HarnessVersion = value;
                    break;
                case "session.id":
                    import.Session.ProviderSessionIdHash ??= PrivacyHash.OpaqueIdentifier(value);
                    break;
                case "team":
                    import.Session.TeamHash = PrivacyHash.OpaqueIdentifier(value);
                    break;
            }
        }
    }

    private static TelemetryRecordModel CreateLogRecord(TelemetryImportModel import, JsonElement root, int recordIndex)
    {
        var attributes = TryGetObject(root, "attributes");
        var eventName = GetString(attributes, "event.name");
        var spanContext = TryGetObject(root, "spanContext");
        var scope = TryGetObject(root, "instrumentationScope");

        return new TelemetryRecordModel
        {
            TelemetryImportId = import.TelemetryImportId,
            AgentSessionId = import.Session.AgentSessionId,
            RecordIndex = recordIndex,
            RecordKind = "log",
            BodyRedactedSummary = eventName ?? "log",
            EventName = eventName,
            TraceIdHash = HashString(spanContext, "traceId"),
            SpanIdHash = HashString(spanContext, "spanId"),
            TraceFlags = GetInt(spanContext, "traceFlags"),
            ObservedAtUtc = GetHrTime(root, "hrTime"),
            ReceivedAtUtc = GetHrTime(root, "hrTimeObserved"),
            InstrumentationScopeName = GetString(scope, "name"),
            InstrumentationScopeVersion = GetString(scope, "version"),
            AttributeCount = GetInt(root, "totalAttributesCount")
        };
    }

    private static TelemetryRecordModel CreateMetricRecord(TelemetryImportModel import, int recordIndex)
    {
        return new TelemetryRecordModel
        {
            TelemetryImportId = import.TelemetryImportId,
            AgentSessionId = import.Session.AgentSessionId,
            RecordIndex = recordIndex,
            RecordKind = "metric"
        };
    }

    private static void ApplyLogAttributes(
        AgentSessionModel session,
        TelemetryRecordModel record,
        JsonElement? attributes)
    {
        session.AgentName ??= GetString(attributes, "gen_ai.agent.name");
        session.ProviderSessionIdHash ??= HashString(attributes, "session.id");
        session.HarnessVersion ??= record.InstrumentationScopeVersion;
    }

    private static AgentTurnModel CreateAgentTurn(Guid agentSessionId, TelemetryRecordModel record, JsonElement? attributes)
    {
        return new AgentTurnModel
        {
            AgentSessionId = agentSessionId,
            TelemetryRecordId = record.TelemetryRecordId,
            TurnIndex = GetInt(attributes, "turn.index"),
            ToolCallCount = GetInt(attributes, "tool_call_count"),
            Success = GetBool(attributes, "success"),
            StartedAtUtc = record.ObservedAtUtc
        };
    }

    private static ToolCallModel CreateToolCall(
        Guid agentSessionId,
        Guid? agentTurnId,
        TelemetryRecordModel record,
        JsonElement? attributes)
    {
        return new ToolCallModel
        {
            AgentSessionId = agentSessionId,
            AgentTurnId = agentTurnId,
            TelemetryRecordId = record.TelemetryRecordId,
            ToolName = GetString(attributes, "gen_ai.tool.name"),
            DurationMs = GetLong(attributes, "duration_ms"),
            Success = GetBool(attributes, "success"),
            ArgumentsCaptured = false,
            ResultCaptured = false
        };
    }

    private static ModelInvocationModel CreateModelInvocation(
        Guid agentSessionId,
        Guid? agentTurnId,
        TelemetryRecordModel record,
        JsonElement? attributes)
    {
        var inputTokens = GetLong(attributes, "gen_ai.usage.input_tokens");
        var outputTokens = GetLong(attributes, "gen_ai.usage.output_tokens");

        return new ModelInvocationModel
        {
            AgentSessionId = agentSessionId,
            AgentTurnId = agentTurnId,
            TelemetryRecordId = record.TelemetryRecordId,
            OperationName = GetString(attributes, "gen_ai.operation.name"),
            ProviderName = GetString(attributes, "gen_ai.provider.name"),
            RequestModel = GetString(attributes, "gen_ai.request.model"),
            ResponseModel = GetString(attributes, "gen_ai.response.model"),
            ProviderResponseIdHash = HashString(attributes, "gen_ai.response.id"),
            FinishReasonsJson = GetRawJson(attributes, "gen_ai.response.finish_reasons"),
            RequestMaxTokens = GetInt(attributes, "gen_ai.request.max_tokens"),
            RequestTemperature = GetDecimal(attributes, "gen_ai.request.temperature"),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TokenTotalType = inputTokens.HasValue && outputTokens.HasValue ? "observed" : "unavailable"
        };
    }

    private static TokenMetricModel CreateInvocationTokenMetric(Guid modelInvocationId, string metricName, long? value)
    {
        var observed = value.HasValue;

        return new TokenMetricModel
        {
            ModelInvocationId = modelInvocationId,
            MetricName = metricName,
            MetricStatus = observed ? "observed" : "unavailable",
            MetricConfidence = observed ? "observed" : "unavailable",
            Value = value,
            Source = observed ? "log_attribute" : "missing_log_attribute"
        };
    }

    private static IEnumerable<MetricObservationModel> CreateMetricObservations(
        Guid telemetryImportId,
        Guid agentSessionId,
        JsonElement scopeMetrics)
    {
        if (scopeMetrics.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var scopeMetric in scopeMetrics.EnumerateArray())
        {
            var scope = TryGetObject(scopeMetric, "scope");
            if (!scopeMetric.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var metric in metrics.EnumerateArray())
            {
                var descriptor = TryGetObject(metric, "descriptor");
                var metricName = GetString(descriptor, "name");
                var metricType = GetString(descriptor, "type");

                if (string.IsNullOrWhiteSpace(metricName) || string.IsNullOrWhiteSpace(metricType))
                {
                    continue;
                }

                if (!metric.TryGetProperty("dataPoints", out var dataPoints) || dataPoints.ValueKind != JsonValueKind.Array)
                {
                    yield return CreateMetricObservation(telemetryImportId, agentSessionId, scope, descriptor, metric, metricName, metricType, null);
                    continue;
                }

                foreach (var dataPoint in dataPoints.EnumerateArray())
                {
                    yield return CreateMetricObservation(telemetryImportId, agentSessionId, scope, descriptor, metric, metricName, metricType, dataPoint);
                }
            }
        }
    }

    private static MetricObservationModel CreateMetricObservation(
        Guid telemetryImportId,
        Guid agentSessionId,
        JsonElement? scope,
        JsonElement? descriptor,
        JsonElement metric,
        string metricName,
        string metricType,
        JsonElement? dataPoint)
    {
        return new MetricObservationModel
        {
            TelemetryImportId = telemetryImportId,
            AgentSessionId = agentSessionId,
            ScopeName = GetString(scope, "name"),
            ScopeVersion = GetString(scope, "version"),
            MetricName = metricName,
            MetricType = metricType,
            ValueType = GetInt(descriptor, "valueType"),
            Unit = GetString(descriptor, "unit"),
            Description = GetString(descriptor, "description"),
            AggregationTemporality = GetInt(metric, "aggregationTemporality"),
            IsMonotonic = GetBool(metric, "isMonotonic"),
            DataPointType = GetInt(metric, "dataPointType"),
            StartTimeUtc = dataPoint is null ? null : GetHrTime(dataPoint.Value, "startTime"),
            EndTimeUtc = dataPoint is null ? null : GetHrTime(dataPoint.Value, "endTime"),
            AttributesJson = GetRawJson(dataPoint, "attributes"),
            ValueJson = GetRawJson(dataPoint, "value"),
            BucketBoundariesJson = GetRawJson(descriptor, "advice.explicitBucketBoundaries")
        };
    }

    private static void ApplySessionTokenTotals(TelemetryImportModel import)
    {
        long input = 0;
        long output = 0;
        var allInputObserved = true;
        var allOutputObserved = true;

        foreach (var invocation in import.ModelInvocations)
        {
            if (invocation.InputTokens.HasValue)
            {
                input += invocation.InputTokens.Value;
            }
            else
            {
                allInputObserved = false;
            }

            if (invocation.OutputTokens.HasValue)
            {
                output += invocation.OutputTokens.Value;
            }
            else
            {
                allOutputObserved = false;
            }
        }

        if (import.ModelInvocations.Count > 0 && allInputObserved && allOutputObserved)
        {
            import.Session.InputTokens = input;
            import.Session.OutputTokens = output;
            import.Session.TokenTotalType = "observed";
        }
        else
        {
            import.Session.InputTokens = allInputObserved && import.ModelInvocations.Count > 0 ? input : null;
            import.Session.OutputTokens = allOutputObserved && import.ModelInvocations.Count > 0 ? output : null;
            import.Session.TokenTotalType = "unavailable";
        }
    }

    private static JsonElement? TryGetObject(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Object ? element : null;
    }

    private static string? GetString(JsonElement? element, string name)
    {
        if (element is null || !element.Value.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? HashString(JsonElement? element, string name)
    {
        var value = GetString(element, name);
        return string.IsNullOrWhiteSpace(value) ? null : PrivacyHash.OpaqueIdentifier(value);
    }

    private static int? GetInt(JsonElement? element, string name)
    {
        if (element is null || !element.Value.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    }

    private static long? GetLong(JsonElement? element, string name)
    {
        if (element is null || !element.Value.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    }

    private static decimal? GetDecimal(JsonElement? element, string name)
    {
        if (element is null || !element.Value.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
    }

    private static bool? GetBool(JsonElement? element, string name)
    {
        if (element is null || !element.Value.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? GetRawJson(JsonElement? element, string name)
    {
        if (element is null || !TryGetPropertyPath(element.Value, name, out var value))
        {
            return null;
        }

        return value.GetRawText();
    }

    private static DateTimeOffset? GetHrTime(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2)
        {
            return null;
        }

        var seconds = value[0].TryGetInt64(out var parsedSeconds) ? parsedSeconds : (long?)null;
        var nanos = value[1].TryGetInt64(out var parsedNanos) ? parsedNanos : 0;

        return seconds is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(seconds.Value).AddTicks(nanos / 100);
    }

    private static bool TryGetPropertyPath(JsonElement element, string path, out JsonElement value)
    {
        if (element.TryGetProperty(path, out value))
        {
            return true;
        }

        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                value = default;
                return false;
            }
        }

        value = current;
        return true;
    }

    private static string ResolveSourceFilePath(string sourceFilePath)
    {
        if (Path.IsPathRooted(sourceFilePath) || File.Exists(sourceFilePath))
        {
            return sourceFilePath;
        }

        var currentDirectoryCandidate = FindInCurrentOrParentDirectories(Directory.GetCurrentDirectory(), sourceFilePath);
        if (currentDirectoryCandidate is not null)
        {
            return currentDirectoryCandidate;
        }

        var appBaseCandidate = FindInCurrentOrParentDirectories(AppContext.BaseDirectory, sourceFilePath);
        return appBaseCandidate ?? sourceFilePath;
    }

    private static string? FindInCurrentOrParentDirectories(string startDirectory, string relativePath)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
