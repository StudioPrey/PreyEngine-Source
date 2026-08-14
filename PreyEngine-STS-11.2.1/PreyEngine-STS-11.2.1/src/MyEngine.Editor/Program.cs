using MyEngine.Editor;

// The Launcher calls this exe with the project's folder path as the first argument.
// If launched directly (e.g. from Visual Studio), fall back to a default folder next to the exe.
string projectPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "DefaultProject");

Directory.CreateDirectory(projectPath);

using var editor = new EditorApp(projectPath);
editor.Run();
