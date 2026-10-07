namespace MyEngine.Core.Scripting;

/// <summary>
/// Core doesn't know how scripts got compiled (that's Roslyn-in-the-Editor today, possibly a
/// pre-built assembly in a standalone Runtime later) — it only needs a name-to-Type lookup so a saved
/// scene referencing a script by name can find the actual compiled class. Whoever compiles/loads the
/// scripts calls Register() once; everything else (SceneSerializer, the Inspector's Add Component menu)
/// just reads from here.
///
/// Scripts are keyed by <see cref="Type.FullName"/> (namespace-qualified, e.g. "Game.PlayerController"),
/// not the bare class name — two different scripts with the same bare name in different namespaces
/// (Game.PlayerController and UI.PlayerController) are a perfectly normal thing for a project to have, and
/// a bare-name-keyed dictionary would throw building the registry the moment that happened. Bare names are
/// still accepted on lookup, as a fallback, purely for scenes saved before this change — see Find.
/// </summary>
public static class ScriptRegistry
{
    private static Dictionary<string, Type> _typesByFullName = new();

    // null value = ambiguous: two or more registered types share this bare name, so it can't be resolved
    // safely — see Find.
    private static Dictionary<string, Type?> _typesByBareName = new();

    public static IReadOnlyCollection<Type> AllTypes => _typesByFullName.Values;

    /// <summary>Replaces the entire registry with a fresh set of script types — call this after every
    /// (re)compilation, not incrementally, so a removed/renamed script actually disappears from the registry too.</summary>
    public static void Register(IEnumerable<Type> scriptTypes)
    {
        var types = scriptTypes
            .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        _typesByFullName = types.ToDictionary(t => t.FullName ?? t.Name, t => t);

        _typesByBareName = new Dictionary<string, Type?>();
        foreach (var t in types)
            _typesByBareName[t.Name] = _typesByBareName.ContainsKey(t.Name) ? null : t;
    }

    /// <summary>Resolves a script type by the name a saved scene references it by. Tries an exact,
    /// namespace-qualified match first (what newly-saved scenes use — see ScriptSerialization.Capture),
    /// then falls back to a bare class-name match for scenes saved before namespace-qualified names were
    /// introduced. The bare-name fallback only succeeds when exactly one registered script has that name;
    /// if two scripts now share a bare name, an old bare-named reference can't be resolved safely and is
    /// treated the same as any other missing script (SceneSerializer skips it rather than failing the
    /// whole load) — silently guessing which one was meant would be worse than asking the project to
    /// re-save the scene once, now that the ambiguity actually exists.</summary>
    public static Type? Find(string typeName)
    {
        if (_typesByFullName.TryGetValue(typeName, out var exact)) return exact;
        return _typesByBareName.GetValueOrDefault(typeName);
    }

    public static void Clear()
    {
        _typesByFullName.Clear();
        _typesByBareName.Clear();
    }
}
