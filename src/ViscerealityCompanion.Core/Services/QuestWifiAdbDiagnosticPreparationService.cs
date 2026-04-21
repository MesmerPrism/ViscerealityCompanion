using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record QuestWifiAdbDiagnosticPreparationResult(
    HeadsetAppStatus InitialHeadset,
    HeadsetAppStatus EffectiveHeadset,
    string? RequestedSelector,
    OperationOutcome? BootstrapOutcome,
    OperationOutcome? ReconnectOutcome)
{
    public bool Attempted
        => BootstrapOutcome is not null || ReconnectOutcome is not null;

    public bool Succeeded
        => EffectiveHeadset.IsConnected && EffectiveHeadset.IsWifiAdbTransport;

    public string Guidance
    {
        get
        {
            if (!Attempted)
            {
                return string.Empty;
            }

            var detailParts = new List<string>();
            if (BootstrapOutcome is not null)
            {
                detailParts.Add($"Automatic Wi-Fi ADB switch attempt: {FormatOutcome(BootstrapOutcome)}");
            }

            if (ReconnectOutcome is not null)
            {
                detailParts.Add($"Automatic Connect Quest retry: {FormatOutcome(ReconnectOutcome)}");
            }

            if (Succeeded)
            {
                detailParts.Add(
                    $"The diagnostic recovered onto Wi-Fi ADB at {FormatOptionalValue(EffectiveHeadset.ConnectionLabel, "the current Quest endpoint")}.");
                return string.Join(" ", detailParts.Where(static part => !string.IsNullOrWhiteSpace(part)));
            }

            var suggestedEndpoint = ResolveSuggestedEndpoint();
            detailParts.Add("This is the stopper for the diagnostic; it cannot turn green until the active Quest transport is Wi-Fi ADB.");
            detailParts.Add(string.IsNullOrWhiteSpace(suggestedEndpoint)
                ? "Keep USB attached, accept any in-headset debugging prompt, rerun the diagnostic, and if needed use Connect Quest with the current headset Wi-Fi IP plus port 5555."
                : $"Keep USB attached, accept any in-headset debugging prompt, rerun the diagnostic, and if needed use Connect Quest with {suggestedEndpoint}.");
            return string.Join(" ", detailParts.Where(static part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    public QuestWifiTransportDiagnosticsResult ApplyTo(QuestWifiTransportDiagnosticsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!Attempted)
        {
            return result;
        }

        var bootstrapDetail = Guidance;
        var combinedDetail = string.IsNullOrWhiteSpace(result.Detail)
            ? bootstrapDetail
            : $"{bootstrapDetail} {result.Detail}".Trim();

        if (!Succeeded)
        {
            return result with
            {
                Level = result.Level == OperationOutcomeKind.Failure ? OperationOutcomeKind.Failure : OperationOutcomeKind.Warning,
                Summary = "Quest is still not on Wi-Fi ADB, so this diagnostic cannot turn green yet.",
                Detail = combinedDetail,
                BootstrapAttempted = true,
                BootstrapSucceeded = false,
                Bootstrap = bootstrapDetail
            };
        }

        return result with
        {
            Detail = combinedDetail,
            BootstrapAttempted = true,
            BootstrapSucceeded = true,
            Bootstrap = bootstrapDetail
        };
    }

    private string ResolveSuggestedEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(ReconnectOutcome?.Endpoint))
        {
            return ReconnectOutcome.Endpoint.Trim();
        }

        if (!string.IsNullOrWhiteSpace(BootstrapOutcome?.Endpoint))
        {
            return BootstrapOutcome.Endpoint.Trim();
        }

        var ipAddress = !string.IsNullOrWhiteSpace(EffectiveHeadset.HeadsetWifiIpAddress)
            ? EffectiveHeadset.HeadsetWifiIpAddress
            : InitialHeadset.HeadsetWifiIpAddress;
        return string.IsNullOrWhiteSpace(ipAddress)
            ? string.Empty
            : $"{ipAddress.Trim()}:5555";
    }

    private static string FormatOutcome(OperationOutcome outcome)
    {
        var level = outcome.Kind switch
        {
            OperationOutcomeKind.Success => "OK",
            OperationOutcomeKind.Warning => "WARN",
            OperationOutcomeKind.Failure => "FAIL",
            OperationOutcomeKind.Preview => "PREVIEW",
            _ => "INFO"
        };
        var parts = new[]
        {
            $"[{level}]",
            outcome.Summary,
            outcome.Detail
        };
        return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private static string FormatOptionalValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class QuestWifiAdbDiagnosticPreparationService
{
    private static readonly TimeSpan ConnectRetrySettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly IQuestControlService _questService;

    public QuestWifiAdbDiagnosticPreparationService(IQuestControlService questService)
    {
        _questService = questService ?? throw new ArgumentNullException(nameof(questService));
    }

    public async Task<QuestWifiAdbDiagnosticPreparationResult> PrepareAsync(
        QuestAppTarget? target,
        string? requestedSelector = null,
        HeadsetAppStatus? initialHeadset = null,
        CancellationToken cancellationToken = default)
    {
        var initial = initialHeadset
            ?? await _questService
                .QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        if (!initial.IsConnected || initial.IsWifiAdbTransport)
        {
            return new QuestWifiAdbDiagnosticPreparationResult(
                initial,
                initial,
                requestedSelector,
                BootstrapOutcome: null,
                ReconnectOutcome: null);
        }

        var bootstrapOutcome = await _questService
            .EnableWifiFromUsbAsync(cancellationToken)
            .ConfigureAwait(false);
        var effective = await _questService
            .QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        OperationOutcome? reconnectOutcome = null;
        if (!effective.IsConnected || !effective.IsWifiAdbTransport)
        {
            var fallbackEndpoint = ResolveFallbackEndpoint(bootstrapOutcome, effective, initial);
            if (!string.IsNullOrWhiteSpace(fallbackEndpoint))
            {
                reconnectOutcome = await _questService
                    .ConnectAsync(fallbackEndpoint, cancellationToken)
                    .ConfigureAwait(false);
                if (reconnectOutcome.Kind != OperationOutcomeKind.Failure)
                {
                    await Task.Delay(ConnectRetrySettleDelay, cancellationToken).ConfigureAwait(false);
                }

                effective = await _questService
                    .QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new QuestWifiAdbDiagnosticPreparationResult(
            initial,
            effective,
            requestedSelector,
            bootstrapOutcome,
            reconnectOutcome);
    }

    private static string ResolveFallbackEndpoint(
        OperationOutcome bootstrapOutcome,
        HeadsetAppStatus effectiveHeadset,
        HeadsetAppStatus initialHeadset)
    {
        if (!string.IsNullOrWhiteSpace(bootstrapOutcome.Endpoint))
        {
            return bootstrapOutcome.Endpoint.Trim();
        }

        if (!string.IsNullOrWhiteSpace(effectiveHeadset.HeadsetWifiIpAddress))
        {
            return $"{effectiveHeadset.HeadsetWifiIpAddress.Trim()}:5555";
        }

        return string.IsNullOrWhiteSpace(initialHeadset.HeadsetWifiIpAddress)
            ? string.Empty
            : $"{initialHeadset.HeadsetWifiIpAddress.Trim()}:5555";
    }
}
