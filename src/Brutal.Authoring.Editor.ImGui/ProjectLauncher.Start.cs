using System.Diagnostics;
using Brutal.Authoring.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class ProjectLauncher
{
    private enum StartPage { Home, New, Open }
    private StartPage startPage;
    private readonly ProjectCreator projectCreator;
    private readonly string personalProjects;
    private ImInputString recentSearch = new(512, ""u8);
    private ImInputString projectName = new(256, "My project"u8);
    private ImInputString projectLocation;
    private ImInputString projectFolder = new(2048, ""u8);
    private int templateIndex;
    private ProjectPaths? selectedRecent;
    private bool showPreferences, showGuide, showAbout, exampleGuideShown;
    private bool rootPicker, locationPicker, focusNewName, focusGuideOpener, focusOpenFolder;

    /// <summary>The recent rows matching name, folder or current display kind, newest first.</summary>
    public IReadOnlyList<ProjectPaths> SearchRecent(string text) => Recent.Items.Where(p =>
        text.Length == 0 || Name(p).Contains(text, StringComparison.OrdinalIgnoreCase)
        || (p.Folder ?? p.Data).Contains(text, StringComparison.OrdinalIgnoreCase)
        || Recent.Metadata(p).Kind.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Creates and opens a personal example; the installed sample stays unchanged.</summary>
    public bool TryExample()
    {
        if (!ConfirmBefore(() => TryExample())) return false;
        if (personalProjects.Length == 0) return Say(false, "Choose a project location with New project first; Documents is unavailable.");
        var result = projectCreator.TryExampleAt(personalProjects);
        if (result.Status != ProjectCreationStatus.Created) return Say(false, result.Reason);
        if (!Open(result.Paths!)) return Say(false, $"Created your example in {result.Folder}, but it couldn't open: {Said}");
        if (!exampleGuideShown && Workspace?.Window is { } window)
        {
            GettingStartedPanel.Show(window.Editor);
            exampleGuideShown = true;
        }
        return Say(true, $"Created your example in {result.Folder}.");
    }

    /// <summary>Creates a new project from one built-in template, then opens it without changing an installed sample.</summary>
    public bool CreateProject(string templateId, string location, string name)
    {
        if (!ConfirmBefore(() => CreateProject(templateId, location, name))) return false;
        var result = projectCreator.Create(templateId, location, name);
        if (result.Status != ProjectCreationStatus.Created) return Say(false, result.Reason);
        if (!Open(result.Paths!)) return Say(false, $"Created {result.Folder}, but it couldn't open: {Said}");
        return Say(true, $"Created your project in {result.Folder}.");
    }

    private void DrawStart()
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.O, false)) BeginOpenProject();
        if (io.KeyCtrl && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.N, false)) { startPage = StartPage.New; focusNewName = true; }
        if (ImGui.IsKeyPressed(ImGuiKey.F1, false)) showGuide = !showGuide;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            if (showGuide || showPreferences || showAbout) { focusGuideOpener = showGuide; showGuide = showPreferences = showAbout = false; }
            else if (startPage != StartPage.Home) startPage = StartPage.Home;
            else recentSearch = new ImInputString(512, ""u8);
        }
        ImGui.Text("BED"u8);
        if (Loaded.Count > 0)
        {
            var nameProblems = new List<string>();
            var names = Loaded.Select(adapter => NameOf(adapter, nameProblems)).ToArray();
            ImGui.TextWrapped("Available game connections: " + string.Join(", ", names));
            foreach (var problem in nameProblems) Ui.TextWrapped(problem);
        }
        // Opening failures must remain visible before the page can consume the window height.
        foreach (var problem in AdapterProblems) ImGui.TextWrapped(problem);
        if (Said.Length > 0) Ui.TextWrapped(Said);
        if (startPage == StartPage.New) DrawNewProject();
        else if (startPage == StartPage.Open) DrawOpenProject();
        else DrawHome();
        if (Workspace is not null) startPage = StartPage.Home;
        ImGui.Separator();
        if (focusGuideOpener) { ImGui.SetKeyboardFocusHere(); focusGuideOpener = false; }
        if (ImGui.SmallButton("Getting started"u8)) showGuide = !showGuide;
        ImGui.SameLine();
        if (ImGui.SmallButton("Preferences"u8)) showPreferences = !showPreferences;
        ImGui.SameLine();
        if (ImGui.SmallButton("About BED"u8)) showAbout = !showAbout;
        if (showGuide)
        {
            ImGui.SeparatorText("Getting started"u8);
            ImGui.TextWrapped(GettingStartedPanel.Heading);
            foreach (var step in GettingStartedPanel.Steps) ImGui.TextWrapped(step);
            if (ImGui.SmallButton("Close guide"u8)) { showGuide = false; focusGuideOpener = true; }
        }
        if (showPreferences)
        {
            ImGui.SetNextItemWidth(160);
            if (Ui.BeginCombo("Theme"u8, EditorTheme.Preference.ToString()))
            {
                foreach (var choice in Enum.GetValues<EditorThemePreference>())
                {
                    var selected = EditorTheme.Preference == choice;
                    if (Ui.Selectable(choice.ToString(), selected)) EditorTheme.Preference = choice;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                Ui.EndCombo();
            }
            ImGui.Text($"Text size {EditorWindow<DataDocument>.TextScale:P0}");
            if (ImGui.SmallButton("Smaller"u8)) EditorWindow<DataDocument>.SetTextScale(EditorWindow<DataDocument>.TextScale - EditorWindow<DataDocument>.TextStep);
            ImGui.SameLine();
            if (ImGui.SmallButton("Larger"u8)) EditorWindow<DataDocument>.SetTextScale(EditorWindow<DataDocument>.TextScale + EditorWindow<DataDocument>.TextStep);
        }
        if (showAbout) ImGui.TextWrapped(BedAbout.Heading + " is Copyright (c) 2026 Tyler McCauley, released under the MIT licence (LICENSE.txt).");
        ImGui.TextWrapped(BrutalCredit.Copyright);
        ImGui.TextWrapped("Built with BRUTAL, a game development framework by Ahwoo Limited. See NOTICE.txt beside this program."u8);
    }

    private void DrawHome()
    {
        if (!ImGui.BeginTable("##start-columns"u8, 2, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Recent"u8, ImGuiTableColumnFlags.WidthStretch, 0.70f);
        ImGui.TableSetupColumn("Start"u8, ImGuiTableColumnFlags.WidthStretch, 0.30f);
        ImGui.TableNextColumn();
        ImGui.SeparatorText("Open recent"u8);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Search recent projects"u8, recentSearch);
        var rows = SearchRecent(Text(recentSearch));
        if (rows.Count == 0) ImGui.TextWrapped(Text(recentSearch).Length > 0 ? "No matches. Clear the search to see your recent projects." : "None yet. Open an existing folder or create a project to begin.");
        if (rows.Count > 0 && ImGui.BeginTable("##recents"u8, 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new float2(0, Math.Max(180, ImGui.GetContentRegionAvail().Y - Math.Max(220, ImGui.GetTextLineHeight() * 10)))))
        {
            ImGui.TableSetupColumn("Name"u8, ImGuiTableColumnFlags.WidthStretch, 0.27f);
            ImGui.TableSetupColumn("Folder"u8, ImGuiTableColumnFlags.WidthStretch, 0.53f);
            ImGui.TableSetupColumn("Last opened"u8, ImGuiTableColumnFlags.WidthStretch, 0.12f);
            ImGui.TableSetupColumn("Kind"u8, ImGuiTableColumnFlags.WidthStretch, 0.08f); ImGui.TableHeadersRow();
            for (var i = 0; i < rows.Count; i++)
            {
                var p = rows[i]; var metadata = Recent.Metadata(p); var folder = p.Folder ?? p.Data;
                ImGui.PushID(i); ImGui.TableNextRow(); ImGui.TableNextColumn();
                var at = ImGui.GetCursorScreenPos();
                var width = Math.Max(50, ImGui.GetContentRegionAvail().X);
                var rowHeight = Math.Max(ImGui.CalcTextSize(Name(p), false, width).Y, ImGui.CalcTextSize(WrapFolder(folder, width), false, width).Y);
                var activated = Ui.Selectable("##recent"u8, selectedRecent == p,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick, new float2(0, rowHeight));
                var enter = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter, false);
                var keyboardMenu = ImGui.IsItemFocused() && (ImGui.IsKeyPressed(ImGuiKey.Menu, false)
                    || (ImGui.GetIO().KeyShift && ImGui.IsKeyPressed(ImGuiKey.F10, false)));
                var pointerMenu = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) && ImGui.IsMouseReleased(ImGuiMouseButton.Right);
                if (activated || keyboardMenu || pointerMenu) selectedRecent = p;
                // Keep the native row ID and hit rectangle. Its index-based identity is positional, not a dispatch key.
                var rowNode = Ui.Tree.Count - 1;
                Ui.Tree.Update(rowNode, Ui.Tree.At(rowNode) with { Label = Name(p), Selected = selectedRecent == p, IdKind = UiIdKind.Positional, Actions = UiActions.None });
                if (keyboardMenu || pointerMenu) ImGui.OpenPopup("##recent-menu"u8);
                if (enter || (activated && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))) OpenRecent(p);
                if (Ui.BeginPopup("##recent-menu"u8))
                {
                    selectedRecent = p;
                    var dataExists = Directory.Exists(p.Data); var schemasExist = Directory.Exists(p.Schemas);
                    // An explicit root is re-resolved by OpenRecent's remembered provider/plain-root policy.
                    // Stale cached paths must not disable that existing recovery route or load adapter code here.
                    var openReason = p.Folder is { } originalRoot
                        ? Directory.Exists(originalRoot) ? null : "That project folder was not found."
                        : !dataExists ? "The project's data folder was not found." : !schemasExist ? "The project's schema folder was not found." : null;
                    if (Ui.MenuItem("Open"u8, ""u8, false, openReason is null, openReason)) OpenRecent(p);
                    var shellFolder = p.Folder ?? Path.GetDirectoryName(p.Data) ?? p.Data;
                    var canShow = Directory.Exists(shellFolder);
                    if (Ui.MenuItem("Show in Explorer"u8, ""u8, false, canShow, canShow ? null : "That project folder was not found.")) ShowRecentFolder(p);
                    if (Ui.MenuItem("Remove from list"u8)) { Recent.Remove(p); selectedRecent = null; Say(true, "Removed from the list; files aren't deleted."); }
                    Ui.EndPopup();
                }
                var after = ImGui.GetCursorScreenPos(); ImGui.SetCursorScreenPos(at); ImGui.TextWrapped(Name(p)); if (!Directory.Exists(folder)) ImGui.TextWrapped("Folder not found"u8); ImGui.SetCursorScreenPos(after); ImGui.Dummy(new float2(0, 0));
                ImGui.TableNextColumn(); ImGui.TextWrapped(WrapFolder(folder, Math.Max(50, ImGui.GetContentRegionAvail().X)));
                ImGui.TableNextColumn(); ImGui.TextWrapped(metadata.LastOpenedUtc?.ToLocalTime().ToString("g") ?? "Not recorded");
                ImGui.TableNextColumn(); ImGui.TextWrapped(metadata.Kind);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.TableNextColumn();
        ImGui.SeparatorText("Get started"u8);
        var size = new float2(-1, ImGui.GetFrameHeight());
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new float2(0, 0.5f));
        if (ImGui.Button("New project..."u8, size)) { startPage = StartPage.New; focusNewName = true; }
        ImGui.TextWrapped("Create a folder for a new project."u8);
        if (ImGui.Button("Open project..."u8, size)) BeginOpenProject();
        ImGui.TextWrapped("Open a folder you already work on."u8);
        if (ImGui.Button("Try the example"u8, size)) TryExample();
        ImGui.TextWrapped("Make a copy you can change safely."u8);
        ImGui.PopStyleVar();
        ImGui.EndTable();
    }

    // Prefer path separators as line boundaries so a component is not broken just because the prefix filled the line.
    // Every character remains visible, including separators; unusually long individual components still wrap normally.
    private static string WrapFolder(string folder, float width)
    {
        var result = new System.Text.StringBuilder();
        var line = "";
        foreach (System.Text.RegularExpressions.Match part in System.Text.RegularExpressions.Regex.Matches(folder, @"[^\\/\s]+[\\/\s]*|[\\/\s]+"))
        {
            var token = part.Value;
            if (line.Length > 0 && ImGui.CalcTextSize(line + token).X > width)
            {
                result.Append(line).Append('\n'); line = token;
            }
            else line += token;
        }
        return result.Append(line).ToString();
    }

    private void BeginOpenProject()
    {
        startPage = StartPage.Open;
        focusOpenFolder = true;
        if (FileDialogs is not { } dialogs) return;
        try
        {
            var chosen = dialogs.PickFolder("Open project", personalProjects.Length == 0 ? null : personalProjects);
            if (chosen is null) { startPage = StartPage.Home; return; }
            UseChosenProjectFolder(chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Say(false, "The folder dialog could not open: " + e.Message); }
    }

    private void DrawOpenProject()
    {
        ImGui.SeparatorText("Open project"u8);
        ImGui.TextWrapped("Choose a project folder, or choose the data and rules folders below."u8);
        if (focusOpenFolder) { ImGui.SetKeyboardFocusHere(); focusOpenFolder = false; }
        ImGui.SetNextItemWidth(-1); ImGui.InputText("Project folder"u8, projectFolder);
        if (ImGui.Button("Browse for project..."u8))
        {
            if (FileDialogs is not null) BeginOpenProject();
            else { rootPicker = true; picker.Start(Text(projectFolder).Length == 0 ? personalProjects : Text(projectFolder), chooseFile: false); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Use this folder"u8)) OpenFolder(Text(projectFolder));
        DrawManualProject();
        if (rootPicker && picker.Draw(chooseFile: false) is { } chosen)
        {
            rootPicker = false;
            if (chosen.Length > 0) { projectFolder = new ImInputString(2048, chosen); if (!OpenFolder(chosen)) ChooseDataFolder(chosen); }
        }
    }

    private void DrawNewProject()
    {
        ImGui.SeparatorText("New project"u8);
        if (focusNewName) { ImGui.SetKeyboardFocusHere(); focusNewName = false; }
        ImGui.InputText("Name"u8, projectName); ImGui.InputText("Location"u8, projectLocation);
        if (ImGui.Button("Browse..."u8))
        {
            if (FileDialogs is { } dialogs)
            {
                try { if (dialogs.PickFolder("Project location", Text(projectLocation)) is { } chosen) projectLocation = new ImInputString(2048, chosen); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                { Say(false, "The folder dialog could not open: " + e.Message); }
            }
            else { locationPicker = true; picker.Start(Text(projectLocation), chooseFile: false); }
        }
        if (locationPicker && picker.Draw(chooseFile: false) is { } picked)
        {
            locationPicker = false;
            if (picked.Length > 0) projectLocation = new ImInputString(2048, picked);
        }
        var templates = ProjectTemplates.All;
        if (ImGui.BeginCombo("Start from"u8, templates[templateIndex].Name))
        {
            for (var i = 0; i < templates.Count; i++) if (ImGui.Selectable(templates[i].Name, templateIndex == i)) templateIndex = i;
            ImGui.EndCombo();
        }
        var preview = projectCreator.Preview(templates[templateIndex].Id, Text(projectLocation), Text(projectName));
        ImGui.TextWrapped(preview.CanCreate ? "Will create: " + preview.Folder : preview.Reason);
        ImGui.BeginDisabled(!preview.CanCreate);
        if (ImGui.Button("Create and open"u8)) CreateProject(templates[templateIndex].Id, Text(projectLocation), Text(projectName));
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("Cancel"u8)) startPage = StartPage.Home;
    }

    private void ShowRecentFolder(ProjectPaths project)
    {
        var folder = project.Folder ?? Path.GetDirectoryName(project.Data) ?? project.Data;
        if (!Directory.Exists(folder)) { Say(false, "That project folder was not found."); return; }
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Say(false, "The folder could not be shown: " + e.Message); }
    }
}
