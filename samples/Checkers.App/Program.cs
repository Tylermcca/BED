using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Checkers;

// Checkers in the editor, in a window: the opening position as a document, the checkers plug-in (its commands, square
// inspector and Board panel), and the docked editor layout. The same setup as the tests' Editor helper (CheckersTests.cs).
var editor = new EditorModel<Game>(CheckersPlugin.Session(Game.Opening()), CheckersPlugin.Outline);
var inspector = new Inspector<Game>(editor);
var shortcuts = new Shortcuts();
if (!new PluginHost<Game>(editor, inspector, shortcuts).Add(new CheckersPlugin()))
    throw new InvalidOperationException("The checkers plug-in didn't load.");
var window = new EditorWindow<Game>(editor, inspector, shortcuts);

EditorApp.Run(new("Checkers"), io => EditorWindow<Game>.Configure(io), window.DrawDocked);
