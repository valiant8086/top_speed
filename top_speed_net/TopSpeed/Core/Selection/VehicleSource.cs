using System;
using System.Collections.Generic;
using TopSpeed.Vehicles;
using TopSpeed.Vehicles.Parsing;
using TopSpeed.Localization;

namespace TopSpeed.Core
{
    internal sealed class VehicleSource : SourceBase<CustomVehicleInfo>
    {
        public VehicleSource()
            : base("Vehicles", "*.tsv")
        {
        }

        protected override string GetKey(CustomVehicleInfo info)
        {
            return info.Key;
        }

        protected override string GetDisplay(CustomVehicleInfo info)
        {
            return info.Display;
        }

        protected override (bool Success, CustomVehicleInfo Value) ParseCore(string file)
        {
            if (!VehicleTsvParser.TryLoadFromFile(file, out var parsed, out var issues))
            {
                AppendIssues(file, issues);
                return (false, default);
            }

            if (issues != null && issues.Count > 0)
                AppendIssues(file, issues);

            // The .tsv parsed, but it may reference sound files that are not on disk (a common packaging
            // mistake: the .tsv ships without its .wav). Catch that here, up front, instead of letting the
            // race loader hard-crash later. A missing *required* sound makes the car unplayable, so we treat
            // it like a failed load and drop it from the list; missing *optional* sounds are only a warning.
            var builtinRoot = System.IO.Path.Combine(AssetPaths.SoundsRoot, "Vehicles");
            var soundIssues = Vehicles.Loader.Sound.ValidateCustomSounds(parsed.Sounds, builtinRoot, parsed.SourceDirectory);
            if (soundIssues.Count > 0)
            {
                var hasMissingRequired = false;
                AddFileIssue(file);
                for (var i = 0; i < soundIssues.Count; i++)
                {
                    var issue = soundIssues[i];
                    if (issue.Required)
                        hasMissingRequired = true;
                    var label = issue.Required
                        ? LocalizationService.Translate(LocalizationService.Mark("Error"))
                        : LocalizationService.Translate(LocalizationService.Mark("Warning"));
                    AddIssue(LocalizationService.Format(LocalizationService.Mark("{0}: {1}"), label, issue.Message));
                }

                if (hasMissingRequired)
                    return (false, default);
            }

            var info = new CustomVehicleInfo(
                file,
                string.IsNullOrWhiteSpace(parsed.Meta.Name) ? LocalizationService.Mark("Custom vehicle") : parsed.Meta.Name,
                parsed.Meta.Version ?? string.Empty,
                parsed.Meta.Description ?? string.Empty);
            return (true, info);
        }

        private void AppendIssues(string file, IReadOnlyList<VehicleTsvIssue> issues)
        {
            AddFileIssue(file);

            if (issues == null || issues.Count == 0)
            {
                AddIssue(LocalizationService.Mark("Failed to load this vehicle file."));
                return;
            }

            for (var i = 0; i < issues.Count; i++)
            {
                var label = issues[i].Severity == VehicleTsvIssueSeverity.Error
                    ? LocalizationService.Translate(LocalizationService.Mark("Error"))
                    : LocalizationService.Translate(LocalizationService.Mark("Warning"));
                AddIssue(LocalizationService.Format(LocalizationService.Mark("{0}: {1}"), label, issues[i].ToString()));
            }
        }
    }
}

