using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MyEngine.Core.Scripting;

namespace MyEngine.Editor.Scripting;

public sealed class ScriptCompilationResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Type> ScriptTypes { get; init; } = Array.Empty<Type>();
}

/// <summary>
/// Compiles all .cs files under a project's Assets/ folder into a single in-memory assembly, using
/// Roslyn directly rather than shelling out to `dotnet build` — much faster for the "hit Play" iteration
/// loop, at the cost of not going through a real .csproj (no per-script NuGet packages; scripts can use
/// anything MyEngine.Core and the .NET BCL already provide, which covers everything Script/Input/Time expose).
/// </summary>
public static class ScriptCompiler
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    /// <summary>Compiles every .cs file under <paramref name="assetsRoot"/>, registers the resulting
    /// script types with ScriptRegistry, and returns the outcome (including any compiler errors).</summary>
    public static ScriptCompilationResult CompileProject(string assetsRoot)
    {
        var scriptFiles = Directory.Exists(assetsRoot)
            ? Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories)
            : Array.Empty<string>();

        if (scriptFiles.Length == 0)
        {
            ScriptRegistry.Clear();
            return new ScriptCompilationResult { Success = true };
        }

        var syntaxTrees = scriptFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions, path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GameScripts_" + Guid.NewGuid().ToString("N")[..8], // unique per compile, so repeated
                                                                               // Play-Mode recompiles don't collide
            syntaxTrees: syntaxTrees,
            references: BuildReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            return new ScriptCompilationResult { Success = false, Errors = errors };
        }

        assemblyStream.Seek(0, SeekOrigin.Begin);
        var assembly = System.Reflection.Assembly.Load(assemblyStream.ToArray());

        var scriptTypes = assembly.GetTypes()
            .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        ScriptRegistry.Register(scriptTypes);

        return new ScriptCompilationResult { Success = true, ScriptTypes = scriptTypes };
    }

    /// <summary>
    /// Assembles the reference set scripts compile against: the full BCL reference-assembly list the
    /// .NET runtime already knows about (TRUSTED_PLATFORM_ASSEMBLIES — more complete and reliable than
    /// only whatever happens to already be loaded in this process), plus every currently-loaded assembly
    /// not already covered by that (this is how MyEngine.Core and MonoGame.Framework get included, since
    /// they're app-specific and wouldn't be part of the shared runtime's reference set).
    /// </summary>
    private static List<MetadataReference> BuildReferences()
    {
        var references = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblyPaths)
        {
            foreach (var path in trustedAssemblyPaths.Split(Path.PathSeparator))
            {
                if (path.Length == 0 || !seenPaths.Add(path)) continue;
                TryAddReference(references, path);
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
            if (!seenPaths.Add(asm.Location)) continue;
            TryAddReference(references, asm.Location);
        }

        return references;
    }

    private static void TryAddReference(List<MetadataReference> references, string path)
    {
        try
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }
        catch (IOException)
        {
            // a handful of runtime/TPA entries aren't valid .NET metadata (native libs, resource-only
            // files) — skip those rather than fail the whole compile over an unrelated file.
        }
        catch (BadImageFormatException)
        {
        }
    }
}
