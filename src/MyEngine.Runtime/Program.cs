using MyEngine.Runtime;

// Everything the game needs (game.manifest.json, Assets/, the compiled scripts DLL) is copied by the
// Editor's Build step to sit right next to this executable — so "where am I running from" is exactly
// "where is the game's data", with no separate install path or working-directory assumption to get wrong.
string gameDirectory = AppContext.BaseDirectory;

using var game = new RuntimeGame(gameDirectory);
game.Run();
