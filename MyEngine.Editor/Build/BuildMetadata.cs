using System.Text;
using System.Text.RegularExpressions;

namespace MyEngine.Editor.Build;

/// <summary>
/// The identity a built game carries in its executable's Windows "Details" tab (product name, company,
/// copyright, file version), turned into the MSBuild properties `dotnet publish` needs. Pure logic with no
/// file or process access, so it is tested directly.
///
/// <para>Two things make this less trivial than passing the strings through, and both are why it exists as a
/// class:</para>
/// <list type="number">
/// <item><b>Versions.</b> The game's version is free text ("1.2", "v0.3-beta", "Alpha 4"), but the executable's
/// AssemblyVersion/FileVersion must be numeric or the build fails outright (CS7034). So the numeric part is
/// extracted for those two, and the text as typed is kept verbatim as the product version.</item>
/// <item><b>Escaping.</b> A property passed as <c>-p:Name=value</c> is split by MSBuild at every ';' and ',' and
/// expands '%XX' and '$(...)' — so a game called "Rock, Paper; Scissors" or a copyright line containing
/// "$(...)" would corrupt the build or inject properties. Every value is escaped.</item>
/// </list>
/// </summary>
public sealed record BuildMetadata(string GameName, string Version, string Company, string Copyright)
{
    public const string FallbackNumericVersion = "1.0.0.0";
    private const int MaxTextLength = 128;

    // Assembly/file version components are 16-bit; 65535 is reserved for "unspecified" in assembly versions.
    private const int MaxVersionComponent = 65534;

    private static readonly Regex LeadingVersion = new(@"^\s*[vV]?([0-9]{1,5})(?:\.([0-9]{1,5}))?(?:\.([0-9]{1,5}))?(?:\.([0-9]{1,5}))?", RegexOptions.Compiled);

    /// <summary>A four-part numeric version for AssemblyVersion/FileVersion: the leading dotted numbers of
    /// <paramref name="version"/> ("1.2" → "1.2.0.0", "v0.3-beta" → "0.3.0.0"), or
    /// <see cref="FallbackNumericVersion"/> when there are none or one is out of range.</summary>
    public static string ToNumericVersion(string? version)
    {
        var match = LeadingVersion.Match(version ?? string.Empty);
        if (!match.Success) return FallbackNumericVersion;

        var parts = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var group = match.Groups[i + 1];
            if (!group.Success) continue;
            if (!int.TryParse(group.Value, out var value) || value > MaxVersionComponent) return FallbackNumericVersion;
            parts[i] = value;
        }
        return string.Join('.', parts);
    }

    /// <summary>Makes free text safe to embed in an executable's metadata and on a command line: control
    /// characters (including line breaks) removed, whitespace trimmed, length capped.</summary>
    public static string CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (!char.IsControl(c)) sb.Append(c);

        var cleaned = sb.ToString().Trim();
        return cleaned.Length > MaxTextLength ? cleaned[..MaxTextLength].TrimEnd() : cleaned;
    }

    /// <summary>Escapes a value for use after <c>-p:Name=</c> on an MSBuild command line, using MSBuild's own
    /// %XX escapes for every character it treats specially. MSBuild decodes them again, so the property ends
    /// up holding exactly the original text.</summary>
    public static string EscapeMsBuildValue(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '%': sb.Append("%25"); break;
                case ';': sb.Append("%3B"); break;
                case ',': sb.Append("%2C"); break;
                case '$': sb.Append("%24"); break;
                case '@': sb.Append("%40"); break;
                case '(': sb.Append("%28"); break;
                case ')': sb.Append("%29"); break;
                case '*': sb.Append("%2A"); break;
                case '?': sb.Append("%3F"); break;
                case '\'': sb.Append("%27"); break;
                case '"': sb.Append("%22"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>The <c>-p:</c> arguments to add to `dotnet publish`. The Windows "Details" tab then shows the game's
    /// name as Product and File description, its numeric version as File/Product version (with the text as
    /// typed kept as the product version), the company, and the copyright line.
    ///
    /// <para>The company is never empty: the SDK's own default would be the executable's internal assembly name
    /// ("Game"), which is just noise in Explorer; when none is configured the game's name is used instead. The
    /// copyright is left out entirely when empty rather than inventing a holder for the game's author.</para></summary>
    public IReadOnlyList<string> ToMsBuildArguments()
    {
        var name = CleanText(GameName);
        if (name.Length == 0) name = "Game";
        var version = CleanText(Version);
        var numeric = ToNumericVersion(version);
        var company = CleanText(Company);
        if (company.Length == 0) company = name;
        var copyright = CleanText(Copyright);

        var args = new List<string>
        {
            $"-p:AssemblyVersion={numeric}",
            $"-p:FileVersion={numeric}",
            $"-p:InformationalVersion={EscapeMsBuildValue(version.Length > 0 ? version : numeric)}",
            $"-p:Product={EscapeMsBuildValue(name)}",
            $"-p:AssemblyTitle={EscapeMsBuildValue(name)}",
            $"-p:Company={EscapeMsBuildValue(company)}",
        };
        if (copyright.Length > 0) args.Add($"-p:Copyright={EscapeMsBuildValue(copyright)}");
        return args;
    }
}
