using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Streams;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Projections;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

namespace Hexalith.Works.Recovery;

/// <summary>Reads complete, scoped, gap-free Works evidence for reminder and process domain adapters.</summary>
internal sealed class WorksSourceStreamReader(
    IEventStoreGatewayClient gateway,
    IOptions<WorksRecoveryOptions> options)
{
    private readonly IEventStoreGatewayClient _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    /// <summary>Reads and validates every committed envelope under one SDK admission/deadline before returning any domain context.</summary>
    internal async Task<IReadOnlyList<WorksCommittedEvent>> ReadAsync(
        string tenant, string aggregate, CancellationToken cancellationToken = default)
    {
        WorksReadModelKeys.ThrowIfReservedTenantId(tenant);
        using var deadline = new AuthoritativeStreamReadDeadline(_options.ContextReadTimeout, TimeProvider.System,
            cancellationToken, TimeProvider.System.GetTimestamp());
        try
        {
            // The SDK worker owns the complete acquisition/fold, including synchronous decoding.
            // Abandonment retains admission until the fold and its non-cooperative provider really terminate.
            return await deadline.ReadWithAdmissionAsync(token => ReadCoreAsync(tenant, aggregate, token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsExpired)
        {
            throw new ProcessEvidenceUnavailableException(exception);
        }
        catch (AuthoritativeStreamReadAdmissionException exception)
        {
            throw new ProcessEvidenceUnavailableException(exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ProcessEvidenceUnavailableException(exception);
        }
        catch (EventStoreGatewayException exception) when (exception.StatusCode is 408 or 429 or >= 500
            && exception.ReasonCode is not (StreamReplayReasonCodes.MissingEvent or StreamReplayReasonCodes.CorruptEvent)
            && exception.Code is not (StreamReplayReasonCodes.MissingEvent or StreamReplayReasonCodes.CorruptEvent))
        {
            throw new ProcessEvidenceUnavailableException(exception);
        }
    }

    private async Task<IReadOnlyList<WorksCommittedEvent>> ReadCoreAsync(string tenant, string aggregate, CancellationToken cancellationToken)
    {
        var events = new List<WorksCommittedEvent>();
        var expiryEvidence = new WorksExpiryEvidence();
        long bytes = 0;
        long from = 0;
        for (int pageNumber = 0; pageNumber < _options.EffectiveMaxStreamPagesPerAggregate; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new StreamReadRequest(tenant, WorkCommandSubmission.WorkDomain, aggregate, from, PageSize: 200);
            StreamReadPage page = await _gateway.ReadWorkloadStreamAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            long next = StreamReadPageValidator.ValidateAndGetNextSequence(request, page);
            foreach (StreamReadEvent envelope in page.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Bound raw variable-size fields before serialized sizing, copying or decoding.
                // Every encoded envelope, including metadata and base64 overhead, spends one cumulative budget.
                long rawBytes = (long)(envelope.Payload?.Length ?? 0) + TextBytes(envelope.EventTypeName)
                    + TextBytes(envelope.SerializationFormat) + TextBytes(envelope.MessageId) + TextBytes(envelope.CorrelationId)
                    + TextBytes(envelope.CausationId) + TextBytes(envelope.UserId);
                if (envelope.ProtectionMetadata is not null
                    && !EventStorePayloadProtectionMetadataCarrier.TryValidate(envelope.ProtectionMetadata, out _))
                    throw new InvalidOperationException("Works source protection metadata is unsupported.");
                if (envelope.Payload is null || rawBytes > _options.MaxContextBytes - bytes
                    || (bytes = checked(bytes + JsonSerializer.SerializeToUtf8Bytes(envelope).Length)) > _options.MaxContextBytes)
                    throw new ProcessEvidenceUnavailableException();
                if (envelope.SequenceNumber != checked(from + 1)
                    || envelope.MetadataVersion != 1 || envelope.PayloadVersion is not (null or 1)
                    || !string.Equals(envelope.SerializationFormat, "json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Works source evidence has a gap or unsupported encoding.");
                }

                IEventPayload payload = WorksRecoveryEventDecoder.Decode(envelope.EventTypeName, envelope.Payload);
                cancellationToken.ThrowIfCancellationRequested();
                if (!WorksEventIdentity.Matches(payload, tenant, aggregate))
                {
                    throw new InvalidOperationException("Works source payload identity differs from its stream.");
                }

                expiryEvidence.Apply(payload, tenant, aggregate);
                events.Add(new WorksCommittedEvent(envelope, payload));
                from = envelope.SequenceNumber;
            }

            if (!page.Metadata.IsTruncated)
            {
                if (next != page.Metadata.LatestSequence)
                {
                    throw new InvalidOperationException("Works source evidence does not reach the authoritative head.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                return events;
            }

            from = next;
        }

        throw new ProcessEvidenceUnavailableException();
    }

    private static int TextBytes(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);
}
