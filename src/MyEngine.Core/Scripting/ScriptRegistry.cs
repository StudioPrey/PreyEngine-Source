namespace MyEngine.Core.Scripting;

/// <summary>
/// Core doesn't know how scripts got compiled (that's Roslyn-in-the-Editor today, possibly a
/// pre-built assembly in a standalone Runtime later) — it only needs a name-to-Type lookup so a saved
/// scene referencing "PlayerController" can find the actual compiled class. Whoever compiles/loads the
/// scripts calls Register() once; everything else (SceneSerializer, the Inspector's Add Component menu)
/// just reads from here.
/// </summary>
public static class ScriptRegistry
{
    private static Dictionary<string, Type> _typesByName = new();

    public static IReadOnlyCollection<Type> AllTypes => _typesByName.Values;

    /// <summary>Replaces the entire registry with a fresh set of script types — call this after every
    /// (re)compilation, not incrementally, so a removed/renamed script actually disappears from the registry too.</summary>
    public static void Register(IEnumerable<Type> scriptTypes)
    {
        _typesByName = scriptTypes
            .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract)
            .ToDictionary(t => t.Name, t => t);
    }

    public static Type? Find(string typeName) => _typesByName.GetValueOrDefault(typeName);

    public static void Clear() => _typesByName.Clear();
}
