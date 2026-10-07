using MyEngine.Runtime;

// Everything the game needs (game.manifest.json, Assets/, the compiled scripts DLL) is copied by the
// Editor's Build step to sit right next to this executable — so "where am I running from" is exactly
// "where is the game's data", with no separate install path or working-directory assumption to get wrong.
string gameDirectory = AppContext.BaseDirectory;
RuntimeGame? game = null;

// An exception on a thread other than the main one (a script's Task, say) bypasses the try/catch below and
// would end the process without a trace. The event can't stop that, but it can leave a crash report.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    CrashHandler.Report(e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()), game, gameDirectory);

try
{
    game = new RuntimeGame(gameDirectory);
    game.Run();
    return 0;
}
catch (Exception ex)
{
    // Startup problems (missing manifest, bad scene, script that won't load) and anything thrown from a
    // script's Update/Draw all end up here.
    CrashHandler.Report(ex, game, gameDirectory);
    return 1;
}
finally
{
    try
    {
        game?.Dispose();
    }
    catch (Exception)
    {
        // Disposing after a crash can fail too (a lost graphics device, for instance). The crash itself is
        // already reported above; a second failure during teardown adds nothing.
    }
}
