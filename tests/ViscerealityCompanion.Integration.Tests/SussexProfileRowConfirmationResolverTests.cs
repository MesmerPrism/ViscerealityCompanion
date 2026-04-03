using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class SussexProfileRowConfirmationResolverTests
{
    [Fact]
    public void Visual_resolver_only_marks_reset_row_as_edited()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var fields = compiler.TemplateDocument.Controls
            .Take(3)
            .Select(control => new SussexVisualProfileFieldViewModel(control, static () => { }))
            .ToArray();

        var resetField = fields[0];
        var unchangedConfirmedField = fields[1];
        var unchangedMismatchField = fields[2];
        var resetControl = compiler.TemplateDocument.Controls.First(control => string.Equals(control.Id, resetField.Id, StringComparison.OrdinalIgnoreCase));
        var requestedValues = fields.ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase);
        requestedValues[resetField.Id] = GetAlternateVisualValue(resetControl);

        resetField.SetValue(requestedValues[resetField.Id], notify: false);
        resetField.ResetToBaseline(notify: false);

        var confirmationRows = new Dictionary<string, SussexVisualConfirmationRow>(StringComparer.OrdinalIgnoreCase)
        {
            [resetField.Id] = new SussexVisualConfirmationRow(resetField.Id, resetField.Label, requestedValues[resetField.Id], requestedValues[resetField.Id], SussexVisualConfirmationState.Confirmed),
            [unchangedConfirmedField.Id] = new SussexVisualConfirmationRow(unchangedConfirmedField.Id, unchangedConfirmedField.Label, requestedValues[unchangedConfirmedField.Id], requestedValues[unchangedConfirmedField.Id], SussexVisualConfirmationState.Confirmed),
            [unchangedMismatchField.Id] = new SussexVisualConfirmationRow(unchangedMismatchField.Id, unchangedMismatchField.Label, requestedValues[unchangedMismatchField.Id], requestedValues[unchangedMismatchField.Id] + 0.01d, SussexVisualConfirmationState.Mismatch)
        };

        var result = SussexVisualRowConfirmationResolver.Compute(
            fields,
            new SussexVisualProfileApplyRecord(
                "visual-profile",
                "Visual Profile",
                "HASH",
                "JSONHASH",
                DateTimeOffset.UtcNow,
                requestedValues,
                PreviousReportedValues: null),
            confirmationRows);

        Assert.Equal(1, result.ChangedSinceApplyCount);
        Assert.Equal(fields.Length - 1, result.UnchangedSinceApplyCount);
        Assert.Equal("Edited", result.States[resetField.Id].Label);
        Assert.Equal(OperationOutcomeKind.Warning, result.States[resetField.Id].Level);
        Assert.Equal("Confirmed", result.States[unchangedConfirmedField.Id].Label);
        Assert.Equal(OperationOutcomeKind.Success, result.States[unchangedConfirmedField.Id].Level);
        Assert.Equal("Mismatch", result.States[unchangedMismatchField.Id].Label);
        Assert.Equal(OperationOutcomeKind.Warning, result.States[unchangedMismatchField.Id].Level);
    }

    [Fact]
    public void Controller_breathing_resolver_only_marks_edited_row_as_unconfirmed()
    {
        var templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");
        var compiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(templatePath));
        var fields = compiler.TemplateDocument.Controls
            .Take(3)
            .Select(control => new SussexControllerBreathingProfileFieldViewModel(control, static () => { }))
            .ToArray();

        var editedField = fields[0];
        var unchangedConfirmedField = fields[1];
        var unchangedWaitingField = fields[2];
        var editedControl = compiler.TemplateDocument.Controls.First(control => string.Equals(control.Id, editedField.Id, StringComparison.OrdinalIgnoreCase));
        var requestedValues = fields.ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase);

        editedField.SetValue(GetAlternateControllerValue(editedControl), notify: false);

        var confirmationRows = new Dictionary<string, SussexControllerBreathingConfirmationRow>(StringComparer.OrdinalIgnoreCase)
        {
            [editedField.Id] = new SussexControllerBreathingConfirmationRow(editedField.Id, editedField.Label, requestedValues[editedField.Id], requestedValues[editedField.Id], SussexControllerBreathingConfirmationState.Confirmed),
            [unchangedConfirmedField.Id] = new SussexControllerBreathingConfirmationRow(unchangedConfirmedField.Id, unchangedConfirmedField.Label, requestedValues[unchangedConfirmedField.Id], requestedValues[unchangedConfirmedField.Id], SussexControllerBreathingConfirmationState.Confirmed),
            [unchangedWaitingField.Id] = new SussexControllerBreathingConfirmationRow(unchangedWaitingField.Id, unchangedWaitingField.Label, requestedValues[unchangedWaitingField.Id], null, SussexControllerBreathingConfirmationState.Waiting)
        };

        var result = SussexControllerBreathingRowConfirmationResolver.Compute(
            fields,
            new SussexControllerBreathingProfileApplyRecord(
                "controller-profile",
                "Controller Profile",
                "HASH",
                "CSVHASH",
                DateTimeOffset.UtcNow,
                requestedValues,
                PreviousReportedValues: null),
            confirmationRows);

        Assert.Equal(1, result.ChangedSinceApplyCount);
        Assert.Equal(fields.Length - 1, result.UnchangedSinceApplyCount);
        Assert.Equal("Edited", result.States[editedField.Id].Label);
        Assert.Equal(OperationOutcomeKind.Warning, result.States[editedField.Id].Level);
        Assert.Equal("Confirmed", result.States[unchangedConfirmedField.Id].Label);
        Assert.Equal(OperationOutcomeKind.Success, result.States[unchangedConfirmedField.Id].Level);
        Assert.Equal("Waiting", result.States[unchangedWaitingField.Id].Label);
        Assert.Equal(OperationOutcomeKind.Warning, result.States[unchangedWaitingField.Id].Level);
    }

    [Fact]
    public void Visual_startup_snapshot_resolver_prefers_saved_snapshot_values()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var startupState = new SussexVisualProfileStartupState(
            "visual-profile",
            "Saved Startup",
            DateTimeOffset.UtcNow,
            null,
            BuildModifiedVisualValues(compiler.TemplateDocument));

        var resolved = SussexVisualStartupSnapshotResolver.ResolveDocument(
            compiler,
            startupState,
            compiler.TemplateDocument);

        var modifiedControl = compiler.TemplateDocument.Controls.First();
        Assert.Equal(
            startupState.ControlValues![modifiedControl.Id],
            resolved.ControlValues[modifiedControl.Id],
            6);
    }

    [Fact]
    public void Visual_startup_snapshot_match_requires_current_values_to_match_saved_snapshot()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var modified = compiler.CreateDocument(
            "Selected",
            null,
            BuildModifiedVisualValues(compiler.TemplateDocument));
        var startupState = new SussexVisualProfileStartupState(
            "visual-profile",
            "Saved Startup",
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase));

        var matches = SussexVisualStartupSnapshotResolver.MatchesCurrentSelection(
            compiler,
            startupState,
            "visual-profile",
            compiler.TemplateDocument,
            compiler.TemplateDocument);
        var mismatches = SussexVisualStartupSnapshotResolver.MatchesCurrentSelection(
            compiler,
            startupState,
            "visual-profile",
            modified,
            compiler.TemplateDocument);

        Assert.True(matches);
        Assert.False(mismatches);
    }

    [Fact]
    public void Controller_breathing_startup_snapshot_resolver_prefers_saved_snapshot_values()
    {
        var templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");
        var compiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(templatePath));
        var startupState = new SussexControllerBreathingProfileStartupState(
            "controller-profile",
            "Saved Startup",
            DateTimeOffset.UtcNow,
            null,
            BuildModifiedControllerValues(compiler.TemplateDocument));

        var resolved = SussexControllerBreathingStartupSnapshotResolver.ResolveDocument(
            compiler,
            startupState,
            compiler.TemplateDocument);

        var modifiedControl = compiler.TemplateDocument.Controls.First();
        Assert.Equal(
            startupState.ControlValues![modifiedControl.Id],
            resolved.ControlValues[modifiedControl.Id],
            6);
    }

    [Fact]
    public void Controller_breathing_startup_snapshot_match_requires_current_values_to_match_saved_snapshot()
    {
        var templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");
        var compiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(templatePath));
        var modified = compiler.CreateDocument(
            "Selected",
            null,
            BuildModifiedControllerValues(compiler.TemplateDocument));
        var startupState = new SussexControllerBreathingProfileStartupState(
            "controller-profile",
            "Saved Startup",
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase));

        var matches = SussexControllerBreathingStartupSnapshotResolver.MatchesCurrentSelection(
            compiler,
            startupState,
            "controller-profile",
            compiler.TemplateDocument,
            compiler.TemplateDocument);
        var mismatches = SussexControllerBreathingStartupSnapshotResolver.MatchesCurrentSelection(
            compiler,
            startupState,
            "controller-profile",
            modified,
            compiler.TemplateDocument);

        Assert.True(matches);
        Assert.False(mismatches);
    }

    [Fact]
    public void Visual_current_document_resolver_returns_error_for_invalid_pair_without_throwing()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var fields = compiler.TemplateDocument.Controls
            .Select(control => new SussexVisualProfileFieldViewModel(control, static () => { }))
            .ToArray();

        var minimumField = fields.First(field => string.Equals(field.Id, "particle_size_min", StringComparison.OrdinalIgnoreCase));
        var maximumField = fields.First(field => string.Equals(field.Id, "particle_size_max", StringComparison.OrdinalIgnoreCase));
        minimumField.SetValue(0.2d, notify: false);
        maximumField.SetValue(0.1d, notify: false);

        var ok = SussexVisualCurrentDocumentResolver.TryCreate(
            compiler,
            "Invalid",
            string.Empty,
            fields,
            out var document,
            out var error);

        Assert.False(ok);
        Assert.Null(document);
        Assert.Contains("particle_size_min.value", error, StringComparison.OrdinalIgnoreCase);
    }

    private static double GetAlternateVisualValue(SussexVisualTuningControl control)
    {
        if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return control.Value >= 0.5d ? 0d : 1d;
        }

        var step = Math.Max(0.01d, (control.SafeMaximum - control.SafeMinimum) / 4d);
        var candidate = control.Value + step;
        if (candidate <= control.SafeMaximum && Math.Abs(candidate - control.Value) > 0.000001d)
        {
            return candidate;
        }

        candidate = control.Value - step;
        if (candidate >= control.SafeMinimum && Math.Abs(candidate - control.Value) > 0.000001d)
        {
            return candidate;
        }

        return Math.Abs(control.BaselineValue - control.Value) > 0.000001d
            ? control.BaselineValue
            : control.SafeMinimum;
    }

    private static double GetAlternateControllerValue(SussexControllerBreathingTuningControl control)
    {
        if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return control.Value >= 0.5d ? 0d : 1d;
        }

        if (string.Equals(control.Type, "int", StringComparison.OrdinalIgnoreCase))
        {
            var current = (int)Math.Round(control.Value, MidpointRounding.AwayFromZero);
            var minimum = (int)Math.Round(control.SafeMinimum, MidpointRounding.AwayFromZero);
            var maximum = (int)Math.Round(control.SafeMaximum, MidpointRounding.AwayFromZero);
            if (current < maximum)
            {
                return current + 1;
            }

            if (current > minimum)
            {
                return current - 1;
            }

            return current;
        }

        var step = Math.Max(0.01d, (control.SafeMaximum - control.SafeMinimum) / 4d);
        var candidate = control.Value + step;
        if (candidate <= control.SafeMaximum && Math.Abs(candidate - control.Value) > 0.000001d)
        {
            return candidate;
        }

        candidate = control.Value - step;
        if (candidate >= control.SafeMinimum && Math.Abs(candidate - control.Value) > 0.000001d)
        {
            return candidate;
        }

        return Math.Abs(control.BaselineValue - control.Value) > 0.000001d
            ? control.BaselineValue
            : control.SafeMinimum;
    }

    private static IReadOnlyDictionary<string, double> BuildModifiedVisualValues(SussexVisualTuningDocument document)
    {
        var values = new Dictionary<string, double>(document.ControlValues, StringComparer.OrdinalIgnoreCase);
        var control = document.Controls.First();
        values[control.Id] = GetAlternateVisualValue(control);
        return values;
    }

    private static IReadOnlyDictionary<string, double> BuildModifiedControllerValues(
        SussexControllerBreathingTuningDocument document)
    {
        var values = new Dictionary<string, double>(document.ControlValues, StringComparer.OrdinalIgnoreCase);
        var control = document.Controls.First();
        values[control.Id] = GetAlternateControllerValue(control);
        return values;
    }

}
