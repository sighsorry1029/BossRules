using System;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace BossRules;

internal sealed class FloatRangeDefinition : IYamlConvertible
{
    public float? Min { get; set; }
    public float? Max { get; set; }

    void IYamlConvertible.Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        if (parser.TryConsume<Scalar>(out Scalar? scalar))
        {
            (Min, Max) = RangeFormatting.ParseFloatRange(scalar.Value);
            return;
        }

        parser.Consume<MappingStart>();
        while (!parser.Accept<MappingEnd>(out _))
        {
            string key = (parser.Consume<Scalar>().Value ?? "").Trim();
            switch (key.ToLowerInvariant())
            {
                case "min":
                    Min = RangeFormatting.ParseNullableFloat(parser.Consume<Scalar>().Value);
                    break;
                case "max":
                    Max = RangeFormatting.ParseNullableFloat(parser.Consume<Scalar>().Value);
                    break;
                default:
                    throw new YamlException($"Unsupported range key '{key}'. Only 'min' and 'max' are supported.");
            }
        }

        parser.Consume<MappingEnd>();
    }

    void IYamlConvertible.Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
    {
        emitter.Emit(new Scalar(RangeFormatting.FormatShorthand(this)));
    }
}

internal static class RangeFormatting
{
    internal static FloatRangeDefinition? FromReference(float actualMin, float actualMax, float defaultMin, float defaultMax)
    {
        if (Math.Abs(actualMin - defaultMin) < 0.0001f && Math.Abs(actualMax - defaultMax) < 0.0001f)
        {
            return null;
        }

        return new FloatRangeDefinition
        {
            Min = actualMin,
            Max = actualMax
        };
    }

    internal static string FormatShorthand(FloatRangeDefinition? range)
    {
        if (range == null || (!range.Min.HasValue && !range.Max.HasValue))
        {
            return "";
        }

        float? min = range.Min;
        float? max = range.Max;
        if (min.HasValue && max.HasValue && min.Value > max.Value)
        {
            (min, max) = (max, min);
        }

        if (min.HasValue && max.HasValue && min.Value.Equals(max.Value))
        {
            return FormatYamlFloat(min.Value);
        }

        return $"{(min.HasValue ? FormatYamlFloat(min.Value) : "")}~{(max.HasValue ? FormatYamlFloat(max.Value) : "")}";
    }

    internal static (float? min, float? max) ParseFloatRange(string? raw)
    {
        string trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0 || trimmed == "~")
        {
            return (null, null);
        }

        int separatorIndex = trimmed.IndexOf('~');
        if (separatorIndex < 0)
        {
            float value = ParseRequiredFloat(trimmed);
            return (value, value);
        }

        string left = trimmed.Substring(0, separatorIndex).Trim();
        string right = trimmed.Substring(separatorIndex + 1).Trim();
        return (ParseNullableFloat(left), ParseNullableFloat(right));
    }

    internal static float? ParseNullableFloat(string? raw)
    {
        string trimmed = (raw ?? "").Trim();
        return trimmed.Length == 0 ? null : ParseRequiredFloat(trimmed);
    }

    private static float ParseRequiredFloat(string raw)
    {
        if (!float.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float value))
        {
            throw new YamlException($"'{raw}' is not a valid float range value.");
        }

        return value;
    }

    private static string FormatYamlFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
