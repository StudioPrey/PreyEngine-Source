using System.Globalization;
using System.Reflection;
using MyEngine.Core.Scripting;

namespace MyEngine.Core.SceneSystem;

/// <summary>
/// Captures and restores the public instance fields of a Script for scene/prefab persistence, using
/// plain string values so ScriptComponentData.Fields stays a simple, JSON-friendly Dictionary&lt;string,string&gt;.
///
/// Supported field types for this first version: float, int, bool, string. This deliberately does NOT
/// attempt Vector2/arrays/enums/object references yet — better to clearly support a few types correctly
/// than to half-support everything. Extend SupportedFieldTypes + the two convert methods together when
/// adding a new type; nothing else needs to change (the Inspector's script UI reads the same field list).
/// </summary>
public static class ScriptSerialization
{
    public static readonly IReadOnlyList<Type> SupportedFieldTypes = new[]
    {
        typeof(float), typeof(int), typeof(bool), typeof(string),
    };

    /// <summary>Public instance fields of <paramref name="scriptType"/> that can be persisted/edited — used
    /// by both serialization here and the Inspector's generic script field UI, so they always agree on
    /// exactly which fields are exposed.</summary>
    public static IEnumerable<FieldInfo> GetEditableFields(Type scriptType) =>
        scriptType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => SupportedFieldTypes.Contains(f.FieldType));

    public static ScriptComponentData Capture(Script script)
    {
        // FullName (namespace-qualified), not the bare class name — see ScriptRegistry's doc comment for
        // why: two scripts named the same thing in different namespaces is a normal thing for a project to
        // have, and only the qualified name can tell them apart reliably on load.
        var data = new ScriptComponentData { TypeName = script.GetType().FullName ?? script.GetType().Name };
        foreach (var field in GetEditableFields(script.GetType()))
            data.Fields[field.Name] = ToStringValue(field.GetValue(script));
        return data;
    }

    public static void Apply(Script script, ScriptComponentData data)
    {
        foreach (var field in GetEditableFields(script.GetType()))
        {
            if (!data.Fields.TryGetValue(field.Name, out var raw)) continue;
            var value = FromStringValue(raw, field.FieldType);
            if (value != null) field.SetValue(script, value);
        }
    }

    private static string ToStringValue(object? value) => value switch
    {
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => "",
    };

    private static object? FromStringValue(string raw, Type targetType)
    {
        try
        {
            if (targetType == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
            if (targetType == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return bool.Parse(raw);
            if (targetType == typeof(string)) return raw;
        }
        catch (FormatException)
        {
            // a hand-edited scene file with a corrupt value shouldn't crash the load — just keep the
            // field's compiled-in default instead of applying garbage.
        }
        return null;
    }
}
