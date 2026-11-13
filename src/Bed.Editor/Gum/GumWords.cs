using Bed.Data;
using Gum.DataTypes;
using Gum.Managers;
using Gum.ProjectServices;

namespace Bed.Editor.Gum;

// GUM-FILES: turns what Gum's own checker says (Gum.ProjectServices.ErrorResult: a code such as GUM0004, a severity and a
// technical message) into BED's plain-words findings. BORROWED: the codes, their severities and the help page for each
// code (ErrorDocsRegistry). INVENTED: the sentences, the "what to do next" lines, and the light clean-up of a message
// for a code BED has no sentence for yet.

/// <summary>Plain words for Gum's own error codes.</summary>
internal sealed class GumWords(GumProjectSave project, string folder, string? screen)
{
    private static readonly ErrorDocsRegistry Docs = new();
    private readonly Dictionary<string, ElementSave> elements = project.AllElements
        .Where(e => !string.IsNullOrEmpty(e.Name)).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The help page Gum keeps for <paramref name="code"/>, or null when it has none.</summary>
    public static string? Help(string? code) => string.IsNullOrEmpty(code) ? null : Docs.GetUrl(code);

    /// <summary>How a screen, component, built-in type or behaviour is named in a sentence ("the component Button").</summary>
    public string Describe(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "(project)") return "the Gum project";
        var dot = name.IndexOf('.');
        if (dot > 0 && !elements.ContainsKey(name) && elements.ContainsKey(name[..dot]))
            return $"the part {name[(dot + 1)..]} in {Describe(name[..dot])}";
        if (elements.TryGetValue(name, out var element))
            return element switch
            {
                ScreenSave => $"the screen {element.Name}",
                ComponentSave => $"the component {element.Name}",
                _ => $"Gum's built-in {element.Name}",
            };
        if (project.Behaviors.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))) return $"the behaviour {name}";
        // A file Gum couldn't read isn't loaded, but the project still lists it.
        bool Listed(IEnumerable<ElementReference> references) => references.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (Listed(project.ScreenReferences)) return $"the screen {name}";
        if (Listed(project.ComponentReferences)) return $"the component {name}";
        if (Listed(project.StandardElementReferences)) return $"Gum's built-in {name}";
        return name;
    }

    /// <summary>The file Gum reads <paramref name="name"/> from, relative to the project folder.</summary>
    private string? ElementFile(string name) => GumCatalog.ElementFile(folder, project, name) is { } file ? GumCatalog.Relative(folder, file) : null;

    /// <summary>The setting a linked value disagrees with: "Width", or "Width of its part Label".</summary>
    private static string Setting(VariableReferenceConflict conflict) =>
        conflict.InstanceName is { Length: > 0 } part && conflict.VariableName.StartsWith(part + ".", StringComparison.Ordinal)
            ? $"{conflict.VariableName[(part.Length + 1)..]} of its part {part}"
            : conflict.VariableName;

    /// <summary>A value as Gum would show it: 255, not 255.0.</summary>
    private static string Value(object? value) =>
        value is null ? "nothing" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "nothing";

    /// <summary>A sentence that starts from the screen being checked, when there is one.</summary>
    private string Start(string what, string rest) =>
        screen is null || what == $"the screen {screen}" ? Capital(what) + " " + rest : $"The screen {screen} uses {what}, and {what} {rest}";

    /// <summary>One plain-words finding for <paramref name="error"/>, keeping Gum's code and help page.</summary>
    public CheckFinding Finding(ErrorResult error, string file)
    {
        var what = Describe(error.ElementName);
        var name = error.ElementName;
        var severity = error.Severity == ErrorSeverity.Error ? Severity.Error : Severity.Warning;
        string message, next;
        switch (error.Code)
        {
            case "GUM0001":
                message = Start(what, "has a behaviour whose required parts are missing or of the wrong kind.");
                next = "Open it in Gum and add the parts its behaviour asks for.";
                break;
            case "GUM0002":
                message = error.ReferenceConflict is { } conflict
                    ? Start(what, $"sets {Setting(conflict)} to {Value(conflict.OldValue)} by hand"
                        + (conflict.State?.Name is { Length: > 0 } look && look != "Default" ? $" in its look {look}" : "")
                        + $", but one of its linked values would set it to {Value(conflict.NewValue)}.")
                    : Start(what, "sets a value by hand that one of its linked values would change.");
                next = "Open it in Gum and keep either the value or the link, not both.";
                break;
            case "GUM0003":
                message = Start(what, "has a look that chooses another look from its own group, which Gum can't do.");
                next = "Open it in Gum and remove that choice from the look.";
                break;
            case "GUM0004":
                var expected = ElementFile(name) ?? name;
                message = Start(what, $"is in the project, but its file {expected} isn't in the project's folder.");
                next = $"Put the file back, or open the project in Gum and save {name} so Gum writes the file again.";
                break;
            case "GUM0006":
                var detail = error.Details as MissingExternalFileDetails;
                var (owner, named) = (detail?.ReferencedFrom, detail?.ReferencedPath);
                var by = owner is null ? what : Describe(owner);
                named ??= "a file";
                message = Start(by, $"names the file {named}, but that file isn't in the project's folder.");
                next = "Put the file back, or change it in Gum.";
                break;
            case "GUM0007":
                message = Start(what, "has a setting with a choice Gum doesn't know.");
                next = "Open it in Gum and pick that setting again.";
                break;
            case "GUM0008":
                var asked = (error.Details as FileNameCaseMismatchDetails)?.ReferencedPath ?? "a file";
                var onDisk = (error.Details as FileNameCaseMismatchDetails)?.OnDiskPath;
                message = Start(what, $"names the file {asked}, but on disk it's spelled {onDisk ?? "with different capitals"}. "
                    + (severity == Severity.Error ? "It won't load." : "Windows doesn't mind, but Linux and Steam Deck do, so it won't load there."));
                next = "Rename the file, or change the name in Gum, so the capital letters match exactly.";
                break;
            case "GUM0009":
                message = Start(what, "has a linked value that reads something that isn't there, so it never applies.");
                next = "Open it in Gum and fix or remove that linked value.";
                break;
            case "GUM0010":
                message = "A font range in the Gum project matches no font that any text uses, so it does nothing.";
                next = "Open the project in Gum and remove the range, or fix its font name.";
                break;
            case "GUM0011":
                message = "A font range in the Gum project isn't written in a way Gum's font maker can read.";
                next = "Open the project in Gum and write the range again, for example 32-126.";
                break;
            case "GUM0012":
                message = Start(what, "has text with letters its font leaves out, so those letters won't show.");
                next = "In Gum, add the letters to the font's range, or change the text.";
                break;
            case "GUM0013":
                message = Start(what, "has linked values that go round in a loop, so Gum never works them out.");
                next = "Open it in Gum and break the loop by removing one of the links.";
                break;
            case "GUM0014":
                message = Start(what, $"is based on itself in a loop: {Loop(error, " is based on ")}. Gum treats them as based on nothing until the loop is broken.");
                next = "In Gum, change what one of them is based on.";
                break;
            case "GUM0015":
                message = Start(what, "isn't based on anything, so the code Gum makes for it has nothing to build on.");
                next = "In Gum, choose what it's based on (Container, if unsure).";
                break;
            case "GUM0016":
                message = Start(what, "is missing a part from what it's based on. Gum adds the part when it opens the project, but the saved file is out of date.");
                next = "Open the project in Gum and save it.";
                break;
            case "GUM0018":
                message = Start(what, $"contains itself: {Loop(error, ", which uses ")}. Gum can't build something that contains itself.");
                next = "In Gum, remove one of these so they don't contain each other.";
                break;
            case "GUM0019":
                message = Start(what, "has a value that doesn't fit its setting.");
                next = "Open it in Gum and set that value again.";
                break;
            case "GUM0020" or "GUM0021" or "GUM0022":
                message = Start(what, "has two parts, looks or groups with the same name.");
                next = "Open it in Gum and rename or delete all but one.";
                break;
            case "GUM0023":
                message = $"The Gum project lists {what} more than once, so it loads as two things with one name.";
                next = "Remove the extra line from the Gum project file, or ask Gum to save the project again.";
                break;
            case "GUM0024":
                message = Start(what, "has parts placed inside each other in a loop.");
                next = "Open it in Gum and move one of the parts out of the loop.";
                break;
            case "GUM0025":
                message = Start(what, "uses a screen as one of its parts, which Gum doesn't allow.");
                next = "Open it in Gum and use a component there instead.";
                break;
            case "GUM0026":
                message = Start(what, "has a look that chooses a look its group doesn't have.");
                next = "Open it in Gum and choose one of the group's looks.";
                break;
            case "GUM0027":
                message = Start(what, "has settings for a part it no longer has.");
                next = "Open it in Gum and save it, which drops the leftover settings.";
                break;
            case "GUM0028":
                message = Start(what, "has a setting of a kind Gum doesn't know.");
                next = "Open it in Gum and check that setting.";
                break;
            case "GUM0030":
                var baseName = elements.TryGetValue(name, out var element) ? element.BaseType : null;
                message = Start(what, $"is based on {baseName ?? "something"}, but the Gum project has nothing called {baseName ?? "that"}.");
                next = $"Add {baseName ?? "it"} back to the project in Gum, or choose another base.";
                break;
            case "GUM0031":
                message = $"The behaviour {name} is in the Gum project, but its file {BehaviourFile(name)} isn't in the project's folder, so Gum loads it empty.";
                next = $"Put the file back, or remove the behaviour {name} in Gum.";
                break;
            case "GUM0035":
                var type = error.Details as TypeReferenceDetails;
                message = string.IsNullOrEmpty(type?.Type)
                    ? Start(what, $"has a part, {type?.InstanceName ?? "unnamed"}, that doesn't say what it's made from.")
                    : Start(what, $"has a part, {type.InstanceName}, made from {type.Type}, but the Gum project has nothing called {type.Type}.");
                next = "Open it in Gum and choose what the part is.";
                break;
            case "GUM0038":
                message = Start(what, "has a file that isn't laid out the way Gum saves one, so Gum can't read it.");
                next = "Open the file in Gum to check it, or restore a saved copy.";
                break;
            case "GUM0041":
                message = Start(what, "has a file with unfinished merge marks (<<<<<<<) from version control, so Gum can't read it.");
                next = "Finish the merge in your version control tool, then check again.";
                break;
            default:
                return Uncoded(error, file, what, severity);
        }
        return Make(severity, file, name is "" or "(project)" ? null : name, message, next, error.Code);
    }

    private CheckFinding Uncoded(ErrorResult error, string file, string what, Severity severity)
    {
        var text = error.Message ?? "";
        var said = error.Code is { } code ? $"has a problem Gum's checker marks as {code}." : "has a problem Gum's checker found.";
        var then = Help(error.Code) is null ? "Open the project in Gum and look at its Errors tab." : $"Gum's help page for {error.Code} says what to change.";
        return Make(severity, file, error.ElementName is "" or "(project)" ? null : error.ElementName, Start(what, said), $"{then} Gum's own note: {EndWithStop(text)}", error.Code);
    }

    /// <summary>A finding in BED's form, with Gum's code and help page when there is a code.</summary>
    public static CheckFinding Make(Severity severity, string file, string? item, string message, string next, string? code) =>
        new(new Issue(severity, EndWithStop(message), item), new CheckPlace(file, item), EndWithStop(next), code, Help(code));

    private static string EndWithStop(string text)
    {
        text = text.TrimEnd();
        return text.EndsWith('.') || text.EndsWith('?') || text.EndsWith('!') ? text : text + ".";
    }

    /// <summary>The text with its first letter made a capital.</summary>
    internal static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Where Gum expects the behaviour <paramref name="name"/>'s file, from the project's own list.</summary>
    private string BehaviourFile(string name) =>
        project.BehaviorReferences.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.GetRelativeFilePath(GumProjectSave.IsJsonFormat(project.FullFileName ?? "")) ?? name;

    /// <summary>The loop named by a GUM0014 or GUM0018 finding, joined with <paramref name="joiner"/>.</summary>
    private static string Loop(ErrorResult error, string joiner)
    {
        var names = LoopOf(error);
        return names.Count > 0 ? string.Join(joiner, names) : "see Gum's help page";
    }

    /// <summary>The loop in Gum's structured details, in order, or none.</summary>
    public static IReadOnlyList<string> LoopOf(ErrorResult error) =>
        error.Details is CycleDetails cycle ? cycle.Path : [];

    /// <summary>The screen being checked, or null for findings about the project as a whole.</summary>
    public string? Screen => screen;
}

// Fatal project-load ErrorMessage still has no structured row. Keep only these root-load classifications.

/// <summary>Reads the few things Gum puts only in its message text.</summary>
internal static class GumMessages
{
    /// <summary>True for Gum's "unresolved git conflict markers" message (a project or screen file mid-merge).</summary>
    public static bool IsConflictMarkers(string? text) => text?.Contains("unresolved git conflict markers", StringComparison.Ordinal) == true;

    /// <summary>True for Gum's "Malformed XML: ..." or "Malformed JSON: ..." load error (a file Gum couldn't read).</summary>
    public static bool IsUnreadableFile(string? text) => text?.StartsWith("Malformed ", StringComparison.Ordinal) == true;

}
