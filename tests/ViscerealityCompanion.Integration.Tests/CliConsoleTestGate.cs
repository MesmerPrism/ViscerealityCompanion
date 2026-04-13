namespace ViscerealityCompanion.Integration.Tests;

internal static class CliConsoleTestGate
{
    internal static readonly SemaphoreSlim Instance = new(1, 1);
}
