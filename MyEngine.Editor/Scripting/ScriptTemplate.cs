using System.Text;

namespace MyEngine.Editor.Scripting;

public static class ScriptTemplate
{
    /// <summary>Turns whatever the user typed into a valid C# class name — since the file name must match
    /// the class name (same convention C# already enforces for public types), this keeps "Create Script"
    /// working no matter what they type in the rename box.</summary>
    public static string SanitizeClassName(string rawName)
    {
        var builder = new StringBuilder();
        foreach (var c in rawName)
        {
            if (char.IsLetterOrDigit(c) || c == '_') builder.Append(c);
        }

        var result = builder.ToString();
        if (result.Length == 0) result = "NewScript";
        if (char.IsDigit(result[0])) result = "_" + result;

        return char.ToUpperInvariant(result[0]) + result[1..];
    }

    public static string Generate(string className) =>
        $$"""
        using MyEngine.Core.InputSystem;
        using MyEngine.Core.Scripting;

        public class {{className}} : Script
        {
            protected override void Start()
            {
            }

            protected override void OnUpdate(float deltaTime)
            {
            }
        }

        """;
}
