using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Meilisearch;

internal sealed record ParsedTagQuery(
    string Text,
    IReadOnlyList<string> RequiredTags,
    IReadOnlyList<string> ExcludedTags,
    long? MinimumDurationSeconds,
    long? MaximumDurationSeconds);

internal static class TagQueryParser
{
    private static readonly Regex TagTokenRegex = new(
        """(?<!\S)(?<exclude>-)?tag:(?:"(?<quoted>[^"]+)"|(?<bare>\S+))""",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    private static readonly Regex DurationTokenRegex = new(
        """(?<!\S)dur:(?<min>\d*)-(?<max>\d*)(?!\S)""",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public static ParsedTagQuery Parse(string? input)
    {
        var requiredTags = new List<string>();
        var excludedTags = new List<string>();

        long? minimumDurationSeconds = null;
        long? maximumDurationSeconds = null;

        var remainingText = input ?? string.Empty;

        /*
         * Duration syntax:
         *
         * dur:30-300  -> 30 through 300 seconds
         * dur:-120    -> up to 120 seconds
         * dur:600-    -> 600 seconds or longer
         *
         * If multiple duration expressions are supplied, the last
         * valid one wins.
         */
        remainingText = DurationTokenRegex.Replace(
            remainingText,
            match =>
            {
                var minText = match.Groups["min"].Value;
                var maxText = match.Groups["max"].Value;

                if (
                    minText.Length == 0 &&
                    maxText.Length == 0
                )
                {
                    return match.Value;
                }

                long? minimum = null;
                long? maximum = null;

                if (minText.Length > 0)
                {
                    if (!long.TryParse(minText, out var value))
                    {
                        return match.Value;
                    }

                    minimum = value;
                }

                if (maxText.Length > 0)
                {
                    if (!long.TryParse(maxText, out var value))
                    {
                        return match.Value;
                    }

                    maximum = value;
                }

                if (
                    minimum.HasValue &&
                    maximum.HasValue &&
                    minimum.Value > maximum.Value
                )
                {
                    (minimum, maximum) = (maximum, minimum);
                }

                minimumDurationSeconds = minimum;
                maximumDurationSeconds = maximum;

                return " ";
            });

        remainingText = TagTokenRegex.Replace(
            remainingText,
            match =>
            {
                var value = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["bare"].Value;

                value = value.Trim();

                if (value.Length == 0)
                {
                    return string.Empty;
                }

                if (match.Groups["exclude"].Success)
                {
                    excludedTags.Add(value);
                }
                else
                {
                    requiredTags.Add(value);
                }

                return " ";
            });

        remainingText = string.Join(
            " ",
            remainingText.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        return new ParsedTagQuery(
            remainingText,
            requiredTags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            excludedTags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            minimumDurationSeconds,
            maximumDurationSeconds);
    }
}
