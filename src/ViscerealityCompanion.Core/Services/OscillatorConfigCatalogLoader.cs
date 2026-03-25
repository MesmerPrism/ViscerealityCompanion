using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class OscillatorConfigCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public async Task<OscillatorConfigCatalog> LoadAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var fullRoot = Path.GetFullPath(rootPath);
        var manifestPath = Path.Combine(fullRoot, "profiles.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Oscillator config manifest not found.", manifestPath);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ManifestDto>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException("Could not deserialize oscillator config manifest.");

        var profiles = new List<OscillatorConfigProfile>();
        foreach (var item in manifest.Profiles)
        {
            if (string.IsNullOrWhiteSpace(item.File))
            {
                continue;
            }

            var profilePath = Path.Combine(fullRoot, item.File);
            if (!File.Exists(profilePath))
            {
                throw new FileNotFoundException("Oscillator config profile file not found.", profilePath);
            }

            var profileJson = await File.ReadAllTextAsync(profilePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<OscillatorConfigDocument>(profileJson, JsonOptions)
                ?? throw new InvalidDataException($"Could not deserialize oscillator config `{item.File}`.");

            profiles.Add(new OscillatorConfigProfile(
                item.Id ?? Path.GetFileNameWithoutExtension(item.File),
                item.Label ?? item.Id ?? Path.GetFileNameWithoutExtension(item.File),
                item.File,
                item.Description ?? string.Empty,
                item.PackageIds ?? Array.Empty<string>(),
                Sanitize(document)));
        }

        return new OscillatorConfigCatalog(
            new OscillatorConfigSource(manifest.Label ?? "Repo sample oscillator configs", fullRoot),
            profiles);
    }

    private static OscillatorConfigDocument Sanitize(OscillatorConfigDocument document)
    {
        var dimensions = document.Dimensions ?? new OscillatorDimensionSettings(
            1,
            [[0f]],
            1f,
            new DimensionRoutingSettings(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        var dimensionCount = Math.Max(1, dimensions.OscillatorDimensionCount);

        return document with
        {
            SchemaVersion = string.IsNullOrWhiteSpace(document.SchemaVersion) ? "1.0" : document.SchemaVersion,
            Stretch = NormalizeStretch(document.Stretch),
            Dimensions = dimensions with
            {
                OscillatorDimensionCount = dimensionCount,
                CrossCouplingMatrix = NormalizeMatrix(dimensions.CrossCouplingMatrix, dimensionCount),
                Routing = NormalizeRouting(dimensions.Routing, dimensionCount)
            },
            NaturalFrequency = NormalizeNaturalFrequency(document.NaturalFrequency),
            Debug = document.Debug ?? new DebugSettings(false, 1f),
            BandGap = document.BandGap ?? new BandGapSettings(0.4f, 1f),
            Sphere = NormalizeSphere(document.Sphere),
            DriverOverrides = document.DriverOverrides ?? new DriverOverrideSettings(false, 0f, false, 0f, false, 0f),
            Color = NormalizeColor(document.Color),
            Size = NormalizeSize(document.Size),
            DepthWave = NormalizeDepthWave(document.DepthWave),
            Transparency = NormalizeVisualEnvelope(document.Transparency),
            Saturation = NormalizeVisualEnvelope(document.Saturation),
            Brightness = NormalizeVisualEnvelope(document.Brightness),
            SpinSpeed = NormalizeMotionEnvelope(document.SpinSpeed),
            Orbit = NormalizeOrbit(document.Orbit),
            PairOffset = NormalizePairOffset(document.PairOffset),
            AnimationPhase = NormalizeAnimationPhase(document.AnimationPhase),
            Coupling = NormalizeCoupling(document.Coupling)
        };
    }

    private static StretchSettings NormalizeStretch(StretchSettings? settings)
        => settings is null
            ? new StretchSettings(false, LinearCurve(), LinearCurve())
            : settings with
            {
                OblatenessByRadiusCurve = NormalizeCurve(settings.OblatenessByRadiusCurve),
                AxisProfileCurve = NormalizeCurve(settings.AxisProfileCurve)
            };

    private static NaturalFrequencySettings NormalizeNaturalFrequency(NaturalFrequencySettings? settings)
        => settings ?? new NaturalFrequencySettings(new FloatRange(0.05f, 0.25f), 1f, new Vector3Value(0f, 0f, 0f), 1);

    private static SphereSettings NormalizeSphere(SphereSettings? settings)
        => settings ?? new SphereSettings("fibonacci-512", "Fibonacci", 512, new FloatRange(1f, 1f));

    private static ColorSettings NormalizeColor(ColorSettings? settings)
        => settings is null
            ? new ColorSettings(DefaultGradient(), 1, LinearCurve(), true, RadiusWeights())
            : settings with
            {
                Gradient = NormalizeGradient(settings.Gradient),
                DriverCurve = NormalizeCurve(settings.DriverCurve)
            };

    private static SizeSettings NormalizeSize(SizeSettings? settings)
        => settings is null
            ? new SizeSettings(true, new FloatRange(0.01f, 0.02f), LinearCurve(), true, RadiusWeights(), 1)
            : settings with
            {
                EnvelopeCurve = NormalizeCurve(settings.EnvelopeCurve)
            };

    private static DepthWaveSettings NormalizeDepthWave(DepthWaveSettings? settings)
        => settings is null
            ? new DepthWaveSettings(new FloatRange(0.1f, 0.1f), LinearCurve(), true, RadiusWeights(), 1)
            : settings with
            {
                EnvelopeCurve = NormalizeCurve(settings.EnvelopeCurve)
            };

    private static VisualEnvelopeSettings NormalizeVisualEnvelope(VisualEnvelopeSettings? settings)
        => settings is null
            ? new VisualEnvelopeSettings(new FloatRange(1f, 1f), LinearCurve(), false, RadiusWeights(), 1)
            : settings with
            {
                EnvelopeCurve = NormalizeCurve(settings.EnvelopeCurve)
            };

    private static MotionEnvelopeSettings NormalizeMotionEnvelope(MotionEnvelopeSettings? settings)
        => settings is null
            ? new MotionEnvelopeSettings(new FloatRange(0f, 1f), LinearCurve(), false, RadiusWeights(), 1)
            : settings with
            {
                EnvelopeCurve = NormalizeCurve(settings.EnvelopeCurve)
            };

    private static OrbitSettings NormalizeOrbit(OrbitSettings? settings)
        => settings is null
            ? new OrbitSettings(
                false,
                RadiusWeights(),
                1,
                true,
                RadiusWeights(),
                1,
                false,
                new FloatRange(0f, 0.05f),
                new FloatRange(0f, 6.2831855f),
                LinearCurve(),
                LinearCurve())
            : settings with
            {
                OrbitRadiusEnvelopeCurve = NormalizeCurve(settings.OrbitRadiusEnvelopeCurve),
                OrbitAngleEnvelopeCurve = NormalizeCurve(settings.OrbitAngleEnvelopeCurve)
            };

    private static PairOffsetSettings NormalizePairOffset(PairOffsetSettings? settings)
        => settings is null
            ? new PairOffsetSettings(
                false,
                RadiusWeights(),
                1,
                new FloatRange(0f, 1f),
                LinearCurve(),
                new FloatRange(0f, 6.2831855f),
                LinearCurve(),
                false,
                RadiusWeights(),
                1)
            : settings with
            {
                PairOffsetEnvelopeCurve = NormalizeCurve(settings.PairOffsetEnvelopeCurve),
                PairOffsetAngleEnvelopeCurve = NormalizeCurve(settings.PairOffsetAngleEnvelopeCurve)
            };

    private static AnimationPhaseSettings NormalizeAnimationPhase(AnimationPhaseSettings? settings)
        => settings is null
            ? new AnimationPhaseSettings(false, RadiusWeights(), 1, LinearCurve())
            : settings with
            {
                Curve = NormalizeCurve(settings.Curve)
            };

    private static CouplingSettings NormalizeCoupling(CouplingSettings? settings)
        => settings is null
            ? new CouplingSettings(
                0.5f,
                1,
                LinearCoupling(new FloatRange(-1f, 1f)),
                LinearCoupling(new FloatRange(-1f, 1f)),
                LinearCoupling(new FloatRange(-1f, 1f)),
                new SmallWorldCouplingSettings(false, new FloatRange(-1f, 1f), OscillatorCouplingDriver.RadiusProgress, LinearCurve()),
                LinearCoupling(new FloatRange(0.6f, 0.8f)))
            : settings with
            {
                NeighborDistance1 = NormalizeCouplingCurve(settings.NeighborDistance1, new FloatRange(-1f, 1f)),
                NeighborDistance2 = NormalizeCouplingCurve(settings.NeighborDistance2, new FloatRange(-1f, 1f)),
                NeighborDistance3 = NormalizeCouplingCurve(settings.NeighborDistance3, new FloatRange(-1f, 1f)),
                SmallWorld = NormalizeSmallWorld(settings.SmallWorld),
                NaturalFrequencyMultiplier = NormalizeCouplingCurve(settings.NaturalFrequencyMultiplier, new FloatRange(0.6f, 0.8f))
            };

    private static DimensionRoutingSettings NormalizeRouting(DimensionRoutingSettings? routing, int dimensionCount)
    {
        var maxIndex = Math.Max(0, dimensionCount - 1);
        routing ??= new DimensionRoutingSettings(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        return routing with
        {
            ColorDimensionIndex = Math.Clamp(routing.ColorDimensionIndex, 0, maxIndex),
            SizeDimensionIndex = Math.Clamp(routing.SizeDimensionIndex, 0, maxIndex),
            RotationDimensionIndex = Math.Clamp(routing.RotationDimensionIndex, 0, maxIndex),
            OrbitDimensionIndex = Math.Clamp(routing.OrbitDimensionIndex, 0, maxIndex),
            PairDimensionIndex = Math.Clamp(routing.PairDimensionIndex, 0, maxIndex),
            WaveDimensionIndex = Math.Clamp(routing.WaveDimensionIndex, 0, maxIndex),
            AnimationDimensionIndex = Math.Clamp(routing.AnimationDimensionIndex, 0, maxIndex),
            TransparencyDimensionIndex = Math.Clamp(routing.TransparencyDimensionIndex, 0, maxIndex),
            SaturationDimensionIndex = Math.Clamp(routing.SaturationDimensionIndex, 0, maxIndex),
            BrightnessDimensionIndex = Math.Clamp(routing.BrightnessDimensionIndex, 0, maxIndex)
        };
    }

    private static float[][] NormalizeMatrix(float[][]? matrix, int dimensionCount)
    {
        var normalized = new float[dimensionCount][];
        for (var rowIndex = 0; rowIndex < dimensionCount; rowIndex++)
        {
            normalized[rowIndex] = new float[dimensionCount];
            var sourceRow = rowIndex < (matrix?.Length ?? 0) ? matrix![rowIndex] : null;
            for (var columnIndex = 0; columnIndex < dimensionCount; columnIndex++)
            {
                if (columnIndex < (sourceRow?.Length ?? 0))
                {
                    normalized[rowIndex][columnIndex] = sourceRow![columnIndex];
                }
            }
        }

        return normalized;
    }

    private static CouplingCurveSettings LinearCoupling(FloatRange limits)
        => new(limits, OscillatorCouplingDriver.RadiusProgress, LinearCurve());

    private static CouplingCurveSettings NormalizeCouplingCurve(CouplingCurveSettings? settings, FloatRange fallbackLimits)
        => settings is null
            ? LinearCoupling(fallbackLimits)
            : settings with
            {
                Curve = NormalizeCurve(settings.Curve)
            };

    private static SmallWorldCouplingSettings NormalizeSmallWorld(SmallWorldCouplingSettings? settings)
        => settings is null
            ? new SmallWorldCouplingSettings(false, new FloatRange(-1f, 1f), OscillatorCouplingDriver.RadiusProgress, LinearCurve())
            : settings with
            {
                Curve = NormalizeCurve(settings.Curve)
            };

    private static CurveDefinition NormalizeCurve(CurveDefinition? curve)
    {
        if (curve?.Keys is not { Length: > 0 })
        {
            return LinearCurve();
        }

        return new CurveDefinition(curve.Keys
            .OrderBy(point => point.Time)
            .Select(point => new CurvePoint(point.Time, point.Value))
            .ToArray());
    }

    private static GradientDefinition NormalizeGradient(GradientDefinition? gradient)
    {
        if (gradient?.Stops is not { Length: > 0 })
        {
            return DefaultGradient();
        }

        return new GradientDefinition(gradient.Stops
            .OrderBy(stop => stop.Position)
            .Select(stop => new GradientStop(stop.Position, string.IsNullOrWhiteSpace(stop.Color) ? "#FFFFFF" : stop.Color))
            .ToArray());
    }

    private static CurveDefinition LinearCurve()
        => new([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]);

    private static GradientDefinition DefaultGradient()
        => new([
            new GradientStop(0f, "#1A1A1A"),
            new GradientStop(0.5f, "#C6552F"),
            new GradientStop(1f, "#F6E3D1")
        ]);

    private static Vector3Value RadiusWeights() => new(1f, 0f, 0f);

    private sealed class ManifestDto
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("profiles")]
        public ProfileDto[] Profiles { get; init; } = Array.Empty<ProfileDto>();
    }

    private sealed class ProfileDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("file")]
        public string? File { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("packageIds")]
        public string[]? PackageIds { get; init; }
    }
}
