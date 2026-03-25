namespace ViscerealityCompanion.Core.Models;

public sealed record OscillatorConfigCatalog(
    OscillatorConfigSource Source,
    IReadOnlyList<OscillatorConfigProfile> Profiles);

public sealed record OscillatorConfigSource(string Label, string RootPath)
{
    public override string ToString() => Label;
}

public sealed record OscillatorConfigProfile(
    string Id,
    string Label,
    string File,
    string Description,
    string[] PackageIds,
    OscillatorConfigDocument Document)
{
    public override string ToString() => Label;

    public bool MatchesPackage(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        return PackageIds.Any(candidate => string.Equals(candidate, packageId, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record OscillatorConfigDocument(
    string SchemaVersion,
    StretchSettings Stretch,
    OscillatorDimensionSettings Dimensions,
    NaturalFrequencySettings NaturalFrequency,
    DebugSettings Debug,
    BandGapSettings BandGap,
    SphereSettings Sphere,
    DriverOverrideSettings DriverOverrides,
    ColorSettings Color,
    SizeSettings Size,
    DepthWaveSettings DepthWave,
    VisualEnvelopeSettings Transparency,
    VisualEnvelopeSettings Saturation,
    VisualEnvelopeSettings Brightness,
    MotionEnvelopeSettings SpinSpeed,
    OrbitSettings Orbit,
    PairOffsetSettings PairOffset,
    AnimationPhaseSettings AnimationPhase,
    CouplingSettings Coupling);

public sealed record StretchSettings(
    bool UseSphereDeformation,
    CurveDefinition OblatenessByRadiusCurve,
    CurveDefinition AxisProfileCurve);

public sealed record OscillatorDimensionSettings(
    int OscillatorDimensionCount,
    float[][] CrossCouplingMatrix,
    float CrossCouplingStrength,
    DimensionRoutingSettings Routing);

public sealed record DimensionRoutingSettings(
    int ColorDimensionIndex,
    int SizeDimensionIndex,
    int RotationDimensionIndex,
    int OrbitDimensionIndex,
    int PairDimensionIndex,
    int WaveDimensionIndex,
    int AnimationDimensionIndex,
    int TransparencyDimensionIndex,
    int SaturationDimensionIndex,
    int BrightnessDimensionIndex);

public sealed record NaturalFrequencySettings(
    FloatRange HzLimits,
    float NoiseScale,
    Vector3Value NoiseOffset,
    int NoiseSeed);

public sealed record DebugSettings(
    bool DebugLogDriverMixes,
    float DebugLogIntervalSeconds);

public sealed record BandGapSettings(
    float GapBlackHalfWidth,
    float GapCenterBlack);

public sealed record SphereSettings(
    string SphereDataId,
    string Layout,
    int OscillatorCount,
    FloatRange RadiusLimits);

public sealed record DriverOverrideSettings(
    bool ManualOverrideCoherence,
    float ManualCoherence01,
    bool ManualOverrideHeartbeatPulse,
    float ManualHeartbeatPulse01,
    bool ManualOverrideBreathing,
    float ManualBreath01);

public sealed record ColorSettings(
    GradientDefinition Gradient,
    int CycleMultiplier,
    CurveDefinition DriverCurve,
    bool UsePerOscillatorPhase,
    Vector3Value ExternalDriverWeights);

public sealed record SizeSettings(
    bool UsePercentSize,
    FloatRange Limits,
    CurveDefinition EnvelopeCurve,
    bool UsePerOscillatorPhase,
    Vector3Value ExternalDriverWeights,
    int CycleMultiplier);

public sealed record DepthWaveSettings(
    FloatRange PercentLimits,
    CurveDefinition EnvelopeCurve,
    bool UsePerOscillatorPhase,
    Vector3Value ExternalDriverWeights,
    int CycleMultiplier);

public sealed record VisualEnvelopeSettings(
    FloatRange Limits,
    CurveDefinition EnvelopeCurve,
    bool UsePerOscillatorPhase,
    Vector3Value ExternalDriverWeights,
    int CycleMultiplier);

public sealed record MotionEnvelopeSettings(
    FloatRange Limits,
    CurveDefinition EnvelopeCurve,
    bool UsePerOscillatorPhase,
    Vector3Value ExternalDriverWeights,
    int CycleMultiplier);

public sealed record OrbitSettings(
    bool OrbitRadiusUsePerOscillatorPhase,
    Vector3Value OrbitRadiusExternalDriverWeights,
    int OrbitRadiusDriverCycleMultiplier,
    bool OrbitAngleUsePerOscillatorPhase,
    Vector3Value OrbitAngleExternalDriverWeights,
    int OrbitAngleDriverCycleMultiplier,
    bool DualSpinAnimation,
    FloatRange OrbitRadiusMultiplierLimits,
    FloatRange OrbitAngleLimits,
    CurveDefinition OrbitRadiusEnvelopeCurve,
    CurveDefinition OrbitAngleEnvelopeCurve);

public sealed record PairOffsetSettings(
    bool PairOffsetUsePerOscillatorPhase,
    Vector3Value PairOffsetExternalDriverWeights,
    int PairOffsetDriverCycleMultiplier,
    FloatRange PairOffsetMultiplierLimits,
    CurveDefinition PairOffsetEnvelopeCurve,
    FloatRange PairOffsetAngleLimits,
    CurveDefinition PairOffsetAngleEnvelopeCurve,
    bool PairOffsetAngleUsePerOscillatorPhase,
    Vector3Value PairOffsetAngleExternalDriverWeights,
    int PairOffsetAngleDriverCycleMultiplier);

public sealed record AnimationPhaseSettings(
    bool UsePerOscillatorPhase,
    Vector3Value ExternalDriverWeights,
    int CycleMultiplier,
    CurveDefinition Curve);

public sealed record CouplingSettings(
    float BaseCouplingStrength,
    int MaxNeighborTier,
    CouplingCurveSettings NeighborDistance1,
    CouplingCurveSettings NeighborDistance2,
    CouplingCurveSettings NeighborDistance3,
    SmallWorldCouplingSettings SmallWorld,
    CouplingCurveSettings NaturalFrequencyMultiplier);

public sealed record CouplingCurveSettings(
    FloatRange Limits,
    OscillatorCouplingDriver Driver,
    CurveDefinition Curve);

public sealed record SmallWorldCouplingSettings(
    bool Enabled,
    FloatRange Limits,
    OscillatorCouplingDriver Driver,
    CurveDefinition Curve);

public enum OscillatorCouplingDriver
{
    RadiusProgress,
    Coherence,
    HeartbeatPulse,
    CouplingPhase
}

public readonly record struct FloatRange(float Minimum, float Maximum)
{
    public override string ToString() => $"{Minimum:0.###} .. {Maximum:0.###}";
}

public readonly record struct Vector3Value(float X, float Y, float Z)
{
    public override string ToString() => $"{X:0.###}, {Y:0.###}, {Z:0.###}";
}

public sealed record CurveDefinition(CurvePoint[] Keys)
{
    public string ToDisplayText()
        => string.Join("; ", Keys.Select(point => $"{point.Time:0.###}:{point.Value:0.###}"));
}

public readonly record struct CurvePoint(float Time, float Value);

public sealed record GradientDefinition(GradientStop[] Stops)
{
    public string ToDisplayText()
        => string.Join("; ", Stops.Select(stop => $"{stop.Position:0.###}:{stop.Color}"));
}

public readonly record struct GradientStop(float Position, string Color);
