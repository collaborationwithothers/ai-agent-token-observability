namespace TokenObservability.Ingestion;

internal static class OtlpProtobufEnvelopeValidator
{
    private delegate bool LengthDelimitedFieldHandler(uint fieldNumber, uint wireType, ReadOnlySpan<byte> value);

    private delegate bool ResourceValidator(ReadOnlySpan<byte> resource);

    public static bool HasValidExportRequest(ReadOnlySpan<byte> payload, string signalType)
    {
        return signalType switch
        {
            "logs" => HasValidTopLevelResource(payload, ResourceLogsContainsScopeLogRecord),
            "traces" => HasValidTopLevelResource(payload, ResourceSpansContainsScopeSpan),
            "metrics" => HasValidTopLevelResource(payload, ResourceMetricsContainsScopeMetric),
            _ => false
        };
    }

    private static bool HasValidTopLevelResource(
        ReadOnlySpan<byte> payload,
        ResourceValidator validateResource)
    {
        var hasResource = false;

        return WalkFields(payload, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 1)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !validateResource(value))
            {
                return false;
            }

            hasResource = true;
            return true;
        }) && hasResource;
    }

    private static bool ResourceLogsContainsScopeLogRecord(ReadOnlySpan<byte> resourceLogs)
    {
        var hasScopeLogs = false;

        return WalkFields(resourceLogs, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 2)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !ScopeLogsContainsLogRecord(value))
            {
                return false;
            }

            hasScopeLogs = true;
            return true;
        }) && hasScopeLogs;
    }

    private static bool ScopeLogsContainsLogRecord(ReadOnlySpan<byte> scopeLogs)
    {
        var hasLogRecord = false;

        return WalkFields(scopeLogs, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 2)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !LogRecordHasSeverityText(value))
            {
                return false;
            }

            hasLogRecord = true;
            return true;
        }) && hasLogRecord;
    }

    private static bool LogRecordHasSeverityText(ReadOnlySpan<byte> logRecord)
    {
        var hasSeverityText = false;

        return WalkFields(logRecord, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber == 3)
            {
                hasSeverityText = wireType == 2 && value.Length > 0;
                return hasSeverityText;
            }

            return true;
        }) && hasSeverityText;
    }

    private static bool ResourceSpansContainsScopeSpan(ReadOnlySpan<byte> resourceSpans)
    {
        var hasScopeSpans = false;

        return WalkFields(resourceSpans, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 2)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !ScopeSpansContainsSpan(value))
            {
                return false;
            }

            hasScopeSpans = true;
            return true;
        }) && hasScopeSpans;
    }

    private static bool ScopeSpansContainsSpan(ReadOnlySpan<byte> scopeSpans)
    {
        var hasSpan = false;

        return WalkFields(scopeSpans, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 2)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !SpanHasRequiredIds(value))
            {
                return false;
            }

            hasSpan = true;
            return true;
        }) && hasSpan;
    }

    private static bool SpanHasRequiredIds(ReadOnlySpan<byte> span)
    {
        var hasTraceId = false;
        var hasSpanId = false;

        return WalkFields(span, (fieldNumber, wireType, value) =>
        {
            if (wireType != 2)
            {
                return true;
            }

            if (fieldNumber == 1)
            {
                hasTraceId = HasRequiredIdValue(value, 16);
            }
            else if (fieldNumber == 2)
            {
                hasSpanId = HasRequiredIdValue(value, 8);
            }

            return true;
        }) && hasTraceId && hasSpanId;
    }

    private static bool HasRequiredIdValue(ReadOnlySpan<byte> value, int expectedLength)
    {
        if (value.Length != expectedLength)
        {
            return false;
        }

        foreach (var item in value)
        {
            if (item != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ResourceMetricsContainsScopeMetric(ReadOnlySpan<byte> resourceMetrics)
    {
        var hasScopeMetrics = false;

        return WalkFields(resourceMetrics, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 2)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !ScopeMetricsContainsNamedMetric(value))
            {
                return false;
            }

            hasScopeMetrics = true;
            return true;
        }) && hasScopeMetrics;
    }

    private static bool ScopeMetricsContainsNamedMetric(ReadOnlySpan<byte> scopeMetrics)
    {
        var hasMetric = false;

        return WalkFields(scopeMetrics, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber != 2)
            {
                return true;
            }

            if (wireType != 2 || value.Length == 0 || !MetricHasNameAndData(value))
            {
                return false;
            }

            hasMetric = true;
            return true;
        }) && hasMetric;
    }

    private static bool MetricHasNameAndData(ReadOnlySpan<byte> metric)
    {
        var hasName = false;
        var hasMetricData = false;

        return WalkFields(metric, (fieldNumber, wireType, value) =>
        {
            if (fieldNumber == 1)
            {
                hasName = wireType == 2 && value.Length > 0;
                return hasName;
            }

            if (fieldNumber is 5 or 7 or 9 or 10 or 11)
            {
                hasMetricData = wireType == 2 && IsValidMessage(value);
                return hasMetricData;
            }

            return true;
        }) && hasName && hasMetricData;
    }

    private static bool IsValidMessage(ReadOnlySpan<byte> message)
    {
        return WalkFields(message, (_, _, _) => true);
    }

    private static bool WalkFields(
        ReadOnlySpan<byte> message,
        LengthDelimitedFieldHandler onLengthDelimited)
    {
        var offset = 0;

        while (offset < message.Length)
        {
            if (!TryReadVarint(message, ref offset, out var key))
            {
                return false;
            }

            var fieldNumber = key >> 3;
            var wireType = key & 0x07;

            if (fieldNumber == 0 ||
                fieldNumber > uint.MaxValue ||
                wireType > uint.MaxValue)
            {
                return false;
            }

            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(message, ref offset, out _))
                    {
                        return false;
                    }

                    break;
                case 1:
                    if (!TrySkip(message, ref offset, 8))
                    {
                        return false;
                    }

                    break;
                case 2:
                    if (!TryReadVarint(message, ref offset, out var length) ||
                        length > int.MaxValue ||
                        offset + (int)length > message.Length)
                    {
                        return false;
                    }

                    var value = message.Slice(offset, (int)length);
                    offset += (int)length;

                    if (!onLengthDelimited((uint)fieldNumber, (uint)wireType, value))
                    {
                        return false;
                    }

                    break;
                case 5:
                    if (!TrySkip(message, ref offset, 4))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> payload, ref int offset, out ulong value)
    {
        value = 0;

        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= payload.Length)
            {
                return false;
            }

            var current = payload[offset++];
            value |= (ulong)(current & 0x7F) << shift;

            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySkip(ReadOnlySpan<byte> payload, ref int offset, int count)
    {
        if (count < 0 || offset + count > payload.Length)
        {
            return false;
        }

        offset += count;
        return true;
    }
}
