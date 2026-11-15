# Playable Gum starter

This is the runtime source for NewGameProject. It uses the already-pinned Gum.SkiaSharp.Standalone package directly and never references BED's editor or AI connection. The Windows pixel-window route follows the existing self-authored sample; the small click-and-collect game is original.

Build: `dotnet build samples/StarterGame/StarterGame.csproj -c Release`.

Check without a window: `samples/StarterGame/bin/Release/net10.0/win-x64/StarterGame.exe --project samples/NewGameProject --headless --script start,collect,back`.

For an observation session, --observe reads one JSON action per line (start, collect, back, restart, frame, stop) and reports the screen, score and rendered picture hash. Game settings and saved Gum screens are reread in the same process. Ordinary Play opens the Windows game; headless options never create a window.

The launcher and CLI builds bundle this runtime once into their New game template. Its copied source project builds independently with the .NET 10 SDK. New packages are not introduced by the starter.
