namespace ViscerealityCompanion.Core.Services;

internal static class TwinLslSourceId
{
    internal const string QuestSourcePrefix = "viscereality.quest.";
    internal const string CompanionSourcePrefix = "viscereality.companion.";

    internal static string BuildCompanionSourceId(
        string streamName,
        string streamType,
        string? machineName = null)
    {
        var machineToken = SanitizeToken(machineName);
        var nameToken = SanitizeToken(streamName);
        var typeToken = SanitizeToken(streamType);

        if (string.IsNullOrWhiteSpace(machineToken))
        {
            machineToken = "host";
        }

        if (string.IsNullOrWhiteSpace(nameToken))
        {
            nameToken = "stream";
        }

        if (string.IsNullOrWhiteSpace(typeToken))
        {
            typeToken = "lsl";
        }

        return $"{CompanionSourcePrefix}{machineToken}.{nameToken}.{typeToken}";
    }

    internal static string BuildQuestStateSourceId(string packageId, string streamName, string streamType)
    {
        var packageToken = SanitizeToken(packageId);
        var nameToken = SanitizeToken(streamName);
        var typeToken = SanitizeToken(streamType);

        if (string.IsNullOrWhiteSpace(packageToken))
        {
            packageToken = "runtime";
        }

        if (string.IsNullOrWhiteSpace(nameToken))
        {
            nameToken = "quest-twin-state";
        }

        if (string.IsNullOrWhiteSpace(typeToken))
        {
            typeToken = "quest-twin-state";
        }

        return $"{QuestSourcePrefix}{packageToken}.{nameToken}.{typeToken}";
    }

    internal static string? NormalizeOptionalToken(string? value)
    {
        var token = SanitizeToken(value);
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static string SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;
        var lastWasSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[count++] = character;
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
            {
                continue;
            }

            buffer[count++] = '-';
            lastWasSeparator = true;
        }

        return new string(buffer[..count]).Trim('-');
    }
}
