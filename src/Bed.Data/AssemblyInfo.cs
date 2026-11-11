using System.Runtime.CompilerServices;

// The managed workspace completes its own synchronous/background SaveCheck before entering
// DataFile's existing internal write path. Keep that completion bridge out of the public data API.
[assembly: InternalsVisibleTo("Bed.Editor.ImGui")]

// The typed document host supplies the same grouped-write coordinator without exporting a save extension API.
[assembly: InternalsVisibleTo("Bed.Editor")]
