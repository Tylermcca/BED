using System.Diagnostics;
using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class ProjectLauncher
{
    private enum StartPage { Home, New, NewGame, Open }
    private StartPage startPage;
    private readonly ProjectCreator projectCreator;
    private readonly string? chosenProjects;   // given by the host (tests, trials); otherwise Preferences > Locations decides
    /// <summary>Where new projects go: the host's choice, else Preferences > Locations, else DocumentsBED Projects.</summary>
    internal string NewProjectsFolder => personalProjects;
    private string personalProjects => chosenProjects ?? (Locations.Projects.Length > 0 ? Locations.Projects : ProjectCreator.DefaultLocation ?? "");
    private ImInputString recentSearch = new(512, ""u8);
    private ImInputString projectName = new(256, "My project"u8);
    private ImInputString projectLocation;
    private ImInputString projectFolder = new(2048, ""u8);
    private int templateIndex;
    private ProjectPaths? selectedRecent;
    private bool focusPreferences, showAddOns;
    private bool showPreferences, showGuide, showAbout, exampleGuideShown;
    private bool rootPicker, locationPicker, focusNewName, focusGuideOpener, focusOpenFolder;

    internal string DisplayName(ProjectPaths project)
    {
        var name=Name(project);
        if(!string.Equals(Path.GetDirectoryName(ProjectFolder(project)),SamplesRoot,StringComparison.OrdinalIgnoreCase))return name;
        return name switch{"checkers"=>"Checkers","first-task"=>"First task","new-game"=>"New game starter","prefab-physics"=>"Physics island","recipes"=>"Recipes","scene-starter"=>"3D scene","sprite-2d"=>"2D garden",_=>name};
    }
    /// <summary>The matching recent rows, pinned first and newest first within each group.</summary>
    public IReadOnlyList<ProjectPaths> SearchRecent(string text) => Recent.Items.Concat(InstalledSamples()).Where(p =>
        text.Length == 0 || DisplayName(p).Contains(text,StringComparison.OrdinalIgnoreCase) || Name(p).Contains(text, StringComparison.OrdinalIgnoreCase)
        || (p.Folder ?? p.Data).Contains(text, StringComparison.OrdinalIgnoreCase)
        || RowKind(p).Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Creates and opens a personal example; the installed sample stays unchanged.</summary>
    public bool TryExample()
    {
        if (!ConfirmBefore(() => TryExample(), projectReplacement: true)) return false;
        if (personalProjects.Length == 0) return Say(false, "Choose a project location with New project first; Documents is unavailable.");
        var forTour=EnableOnboarding&&tourActive&&tourStep==TourStep.OpenExample;
        var result = projectCreator.TryExampleAt(personalProjects);
        if (result.Status != ProjectCreationStatus.Created) return Say(false, result.Reason);
        if (!Open(result.Paths!)) return Say(false, $"Created your example in {result.Folder}, but it couldn't open: {Said}");
        if (Workspace?.Project.Files.FirstOrDefault(file => Workspace.Documents.IsScene(file.Name)) is { } scene && !Workspace.Show(scene.Name))
            return Say(false, $"Created your example in {result.Folder}, but its level couldn't open: {Workspace.Said}");
        if (!exampleGuideShown && Workspace?.Window is { } window)
        {
            GettingStartedPanel.Show(window.Editor);
            exampleGuideShown = true;
        }
        if(forTour)AttachTourOwner();
        return Say(true, $"Created your example in {result.Folder}.");
    }

    /// <summary>Creates a new project from one built-in template, then opens it without changing an installed sample.</summary>
    public bool CreateProject(string templateId, string location, string name)
    {
        if (!ConfirmBefore(() => CreateProject(templateId, location, name), projectReplacement: true)) return false;
        var result = projectCreator.Create(templateId, location, name);
        if (result.Status != ProjectCreationStatus.Created) return Say(false, result.Reason);
        if (!Open(result.Paths!)) return Say(false, $"Created {result.Folder}, but it couldn't open: {Said}");
        if (templateId == "game") Workspace?.ShowNewGameStart();
        return Say(true, $"Created your {(templateId == "game" ? "game" : "project")} in {result.Folder}.");
    }

    private bool ChooseNewGame()
    {
        if (Workspace is null) { startPage = StartPage.NewGame; focusNewName = true; return true; }
        return ChooseProject(StartPage.NewGame);
    }

    private void DrawStart()
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.KeyShift && !io.KeyAlt && !io.KeySuper && ImGui.IsKeyPressed(ImGuiKey.Comma, false))
        { showPreferences = true; focusPreferences = true; }
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.O, false)) BeginOpenProject();
        if (io.KeyCtrl && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.N, false)) { startPage = StartPage.New; focusNewName = true; }
        if (ImGui.IsKeyPressed(ImGuiKey.F1, false)) showGuide = !showGuide;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            if (showGuide || showPreferences || showAbout) { focusGuideOpener = showGuide; showGuide = showPreferences = showAbout = false; }
            else if (startPage != StartPage.Home) EndProjectChooser();
            else recentSearch = new ImInputString(512, ""u8);
        }
        Ui.Text("BED"u8);
        var home = startPage == StartPage.Home;
        if (home)
        {
            var buttonHeight = MathF.Ceiling(ImGui.GetFrameHeight() * 1.4f);
            var width = ImGui.GetContentRegionAvail().X;
            if (Ui.Button("Try the example"u8, new float2(-1, buttonHeight))) TryExample();
            if (Workspace is not null && !projectChooser) return;

            var buttonWidth = (width - ImGui.GetStyle().ItemSpacing.X * 2) / 3;
            var horizontal = buttonWidth >= ImGui.CalcTextSize("Open project..."u8).X + ImGui.GetStyle().FramePadding.X * 2;
            var size = new float2(horizontal ? buttonWidth : -1, buttonHeight);
            if (Ui.Button("New game..."u8, size)) ChooseNewGame();
            if (horizontal) ImGui.SameLine();
            if (Ui.Button("New project..."u8, size)) { startPage = StartPage.New; focusNewName = true; }
            if (horizontal) ImGui.SameLine();
            if (Ui.Button("Open project..."u8, size)) BeginOpenProject();
            if (Workspace is not null && !projectChooser) return;
        }

        // Again, not `home`: a button above may have just changed the page, and that page's first frame must be drawn
        // where it will stay, or its focus request lands in this child and is lost (Tab then stuck on the gone button).
        if (startPage == StartPage.Home)
        {
            if (Ui.BeginChild("Start choices###launcher.choices"u8, new float2(0, 0), ImGuiChildFlags.NavFlattened))
                DrawStartContent();
            Ui.EndChild();
        }
        else DrawStartContent();
    }

    private void DrawStartContent()
    {
        if (startPage == StartPage.Home) DrawHome();
        Ui.TextWrapped("Edit game data and levels. Save changes, run checks, then play your game."u8);
        if (Ui.Button("Report a problem"u8)) problemReport.Open();
        DrawTourOffer();
        if (Loaded.Count > 0)
        {
            if (Ui.SmallButton(showAddOns ? "Hide game add-ons###launcher.addons"u8 : "Game add-ons###launcher.addons"u8))
                showAddOns = !showAddOns;
            var nameProblems = new List<string>();
            var names = Loaded.Select(adapter => NameOf(adapter, nameProblems)).ToArray();
            if (showAddOns) foreach (var name in names) Ui.TextWrapped(name);
            foreach (var problem in nameProblems) Ui.TextWrapped(problem);
        }
        foreach (var problem in AdapterProblems) Ui.TextWrapped(problem);
        if (said.Showing) Ui.StatusWrapped(Said, "launcher.message");
        if (startPage is StartPage.New or StartPage.NewGame) DrawNewProject();
        else if (startPage == StartPage.Open) DrawOpenProject();
        if (Workspace is not null && !projectChooser) startPage = StartPage.Home;
        ImGui.Separator();
        if (focusGuideOpener) { ImGui.SetKeyboardFocusHere(); focusGuideOpener = false; }
        if (Ui.SmallButton("Getting started"u8)) showGuide = !showGuide;
        ImGui.SameLine();
        if (Ui.SmallButton("Options"u8)) showPreferences = !showPreferences;
        ImGui.SameLine();
        if (Ui.SmallButton("About BED"u8)) showAbout = !showAbout;
        if (showGuide)
        {
            ImGui.SeparatorText("Getting started"u8);
            Ui.TextWrapped(GettingStartedPanel.Heading);
            foreach (var step in GettingStartedPanel.Steps) Ui.TextWrapped(step);
            if(EnableOnboarding&&Ui.Button("Take the tour"u8))BeginTour();
            if (Ui.SmallButton("Close guide"u8)) { showGuide = false; focusGuideOpener = true; }
        }
        if (showPreferences)
        {
            var creditWidth=ImGui.GetContentRegionAvail().X;
            var creditHeight=ImGui.CalcTextSize(BrutalCredit.Copyright,false,creditWidth).Y
                +ImGui.CalcTextSize("Built with BRUTAL, a game development framework by Ahwoo Limited. See NOTICE.txt beside this program."u8,false,creditWidth).Y
                +ImGui.GetStyle().ItemSpacing.Y*4;
            // Options can scroll on a small display; keep the program's credits below it visible.
            var optionsHeight=MathF.Max(ImGui.GetFrameHeightWithSpacing()*2,ImGui.GetContentRegionAvail().Y-creditHeight);
            if(Ui.BeginChild("Options###launcher.options"u8,new float2(0,optionsHeight),ImGuiChildFlags.NavFlattened))
            {
            ImGui.SetNextItemWidth(160);
            if (focusPreferences) { ImGui.SetKeyboardFocusHere(); focusPreferences = false; }
            if (Ui.BeginCombo("Theme"u8, EditorTheme.Label(EditorTheme.Preference)))
            {
                foreach (var choice in Enum.GetValues<EditorThemePreference>())
                {
                    var selected = EditorTheme.Preference == choice;
                    if (Ui.Selectable(EditorTheme.Label(choice), selected)) EditorTheme.Preference = choice;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                Ui.EndCombo();
            }
            ImGui.SetNextItemWidth(MathF.Min(160, ImGui.GetContentRegionAvail().X));
            if (Ui.BeginCombo("UI scale"u8, $"{EditorWindow<DataDocument>.TextScale * 100:0}%"))
            {
                foreach (var percent in new[] { 100, 150, 200 })
                {
                    var selected = EditorWindow<DataDocument>.TextScale == percent / 100f;
                    if (Ui.Selectable($"{percent}%", selected)) EditorWindow<DataDocument>.SetTextScale(percent / 100f);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                Ui.EndCombo();
            }
            PreferencesWindow.DrawCopyNames();
            ViewportControls.DrawChoice();
            Locations.Draw();
            Ui.Text($"Text size {EditorWindow<DataDocument>.TextScale:P0}");
            if (Ui.SmallButton("Smaller"u8)) EditorWindow<DataDocument>.SetTextScale(EditorWindow<DataDocument>.TextScale - EditorWindow<DataDocument>.TextStep);
            ImGui.SameLine();
            if (Ui.SmallButton("Larger"u8)) EditorWindow<DataDocument>.SetTextScale(EditorWindow<DataDocument>.TextScale + EditorWindow<DataDocument>.TextStep);
            }
            Ui.EndChild();
        }
        if (showAbout) Ui.TextWrapped(BedAbout.Heading + " is Copyright (c) 2026 Tyler McCauley, released under the MIT licence (LICENSE.txt).");
        Ui.TextWrapped(BrutalCredit.Copyright);
        Ui.TextWrapped("Built with BRUTAL, a game development framework by Ahwoo Limited. See NOTICE.txt beside this program."u8);
    }

    private void DrawHome()
    {
        ImGui.SeparatorText("Open recent"u8);
        ImGui.SetNextItemWidth(-1);
        Ui.InputText("Search recent projects"u8, recentSearch);
        var rows = SearchRecent(Text(recentSearch));
        if (Recent.Items.Count >= Recent.Limit && Recent.Items.Count > 0 && Recent.Items.All(Recent.IsPinned))
            Ui.TextWrapped("All recent slots are pinned. Unpin a project to remember another."u8, semanticId:"recent.projects.pinned.limit");
        var recentHeight = Math.Clamp(ImGui.GetContentRegionAvail().Y * .48f, 120, 360);
        if(rows.Count==0)
        {
            if(Ui.BeginChild("Recent projects###recent.empty"u8,new float2(0,recentHeight),ImGuiChildFlags.Borders,ImGuiWindowFlags.NoNavInputs|ImGuiWindowFlags.NoNavFocus))
                Ui.TextWrapped(Text(recentSearch).Length>0?"No matches. Clear the search to see your recent projects.":"None yet. Open an existing folder or create a project to begin.");
            Ui.EndChild();
        }
        var compact = ImGui.GetContentRegionAvail().X < ImGui.GetFontSize() * 32;
        if (rows.Count > 0 && Ui.BeginTable("##recents"u8, compact ? 2 : 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new float2(0,recentHeight)))
        {
            ImGui.TableSetupColumn("Name"u8, ImGuiTableColumnFlags.WidthStretch, compact ? .4f : .27f);
            ImGui.TableSetupColumn("Folder"u8, ImGuiTableColumnFlags.WidthStretch, compact ? .6f : .53f);
            if (!compact)
            {
                ImGui.TableSetupColumn("Last opened"u8, ImGuiTableColumnFlags.WidthStretch, .12f);
                ImGui.TableSetupColumn("Kind"u8, ImGuiTableColumnFlags.WidthStretch, .08f);
            }
            ImGui.TableHeadersRow();
            for (var i = 0; i < rows.Count; i++)
            {
                var p = rows[i]; var metadata = Recent.Metadata(p); var folder = p.Folder ?? p.Data;
                var openReason = RecentOpenRefusal(p);
                ImGui.PushID(System.Text.Json.JsonSerializer.Serialize(p)); Ui.TableNextRow(); Ui.TableNextColumn();
                var at = ImGui.GetCursorScreenPos();
                var width = Math.Max(50, ImGui.GetContentRegionAvail().X);
                var rowHeight = Math.Max(ImGui.CalcTextSize(DisplayName(p), false, width).Y + ((openReason is null ? 0 : 1) + (Recent.IsPinned(p) ? 1 : 0)) * ImGui.GetTextLineHeightWithSpacing(), ImGui.CalcTextSize(WrapFolder(folder, width), false, width).Y);
                Ui.NameNextItem("##recent"u8, DisplayName(p));
                var activated = Ui.Selectable("##recent"u8, selectedRecent == p,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick, new float2(0, rowHeight));
                var enter = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter, false);
                var keyboardMenu = ImGui.IsItemFocused() && (ImGui.IsKeyPressed(ImGuiKey.Menu, false)
                    || (ImGui.GetIO().KeyShift && ImGui.IsKeyPressed(ImGuiKey.F10, false)));
                var pointerMenu = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) && ImGui.IsMouseReleased(ImGuiMouseButton.Right);
                if (activated || keyboardMenu || pointerMenu) selectedRecent = p;
                // Keep the path/adapter identity and hit rectangle; filtering or reordering cannot retarget an action.
                var rowNode = Ui.Tree.Count - 1;
                Ui.Tree.Update(rowNode, Ui.Tree.At(rowNode) with { Selected = selectedRecent == p });
                if (keyboardMenu || pointerMenu) ImGui.OpenPopup("##recent-menu"u8);
                if (openReason is null && (enter || (activated && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)))) OpenRow(p);
                if (Ui.BeginPopup("##recent-menu"u8))
                {
                    selectedRecent = p;
                    if (Ui.MenuItem("Open"u8, ""u8, false, openReason is null, openReason)) OpenRow(p);
                    var shellFolder = p.Folder ?? Path.GetDirectoryName(p.Data) ?? p.Data;
                    var canShow = Directory.Exists(shellFolder);
                    if (Ui.MenuItem("Show in Explorer"u8, ""u8, false, canShow, canShow ? null : "That project folder was not found.")) ShowRecentFolder(p);
                    var pinned = Recent.IsPinned(p);
                    if (!IsListedSample(p) && (pinned ? Ui.MenuItem("Unpin"u8) : Ui.MenuItem("Pin"u8)))
                    { Recent.SetPinned(p, !pinned); Say(true, pinned ? "Unpinned the recent project." : "Pinned the recent project."); }
                    if (Ui.MenuItem("Remove from list"u8)) { RemoveFromList(p); selectedRecent = null; Say(true, "Removed from the list; files aren't deleted."); }
                    Ui.EndPopup();
                }
                var after = ImGui.GetCursorScreenPos(); ImGui.SetCursorScreenPos(at); Ui.TextWrapped(DisplayName(p)); if (Recent.IsPinned(p)) Ui.TextWrapped("Pinned"u8); if (openReason is not null) Ui.TextWrapped("Folder not found"u8); ImGui.SetCursorScreenPos(after); ImGui.Dummy(new float2(0, 0));
                Ui.TableNextColumn(); Ui.TextWrapped(WrapFolder(folder, Math.Max(50, ImGui.GetContentRegionAvail().X)));
                if (!compact)
                {
                    Ui.TableNextColumn(); Ui.TextWrapped(metadata.LastOpenedUtc?.ToLocalTime().ToString("g") ?? (IsListedSample(p) ? "Not opened yet" : "Not recorded"));
                    Ui.TableNextColumn(); Ui.TextWrapped(RowKind(p));
                }
                ImGui.PopID();
            }
            Ui.EndTable();
        }

    }

    /// <summary>Every folder in BED's own <c>samples</c> folder (<see cref="SamplesFolder"/>) that isn't already a recent project
    /// and hasn't been removed from the list, by name. Nothing is hard-coded: an empty or missing samples folder lists none.</summary>
    public IReadOnlyList<ProjectPaths> InstalledSamples()
    {
        string[] folders;
        try { folders = Directory.Exists(SamplesFolder) ? Directory.GetDirectories(SamplesFolder) : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        var removed = RemovedSamples();
        var recent = Recent.Items.Select(ProjectFolder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return folders.Select(Path.GetFullPath).Where(f => !removed.Contains(f) && !recent.Contains(f))
            .Order(StringComparer.OrdinalIgnoreCase).Select(f => new ProjectPaths("", "") { Folder = f }).ToArray();
    }

    private string SamplesRoot => Path.GetFullPath(SamplesFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>True for a row that is one of BED's samples not yet opened (so not in the recent list).</summary>
    public bool IsListedSample(ProjectPaths p) => p.Folder is { } && !Recent.Items.Contains(p)
        && string.Equals(Path.GetDirectoryName(ProjectFolder(p)), SamplesRoot, StringComparison.OrdinalIgnoreCase);

    private static string ProjectFolder(ProjectPaths p)
    {
        try { return Path.GetFullPath(p.Folder ?? p.Root ?? Path.GetDirectoryName(Path.GetFullPath(p.Data)) ?? p.Data).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
    }

    private HashSet<string>? removedSamples;
    private string RemovedSamplesFile => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Recent.File))!, "removed-samples.json");

    private HashSet<string> RemovedSamples()
    {
        if (removedSamples is not null) return removedSamples;
        removedSamples = new(StringComparer.OrdinalIgnoreCase);
        try { if (File.Exists(RemovedSamplesFile)) removedSamples.UnionWith(System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(RemovedSamplesFile)) ?? []); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        return removedSamples;
    }

    /// <summary>Takes a row off the start screen's list; a sample stays off even though its folder is still there. No files are deleted.</summary>
    public void RemoveFromList(ProjectPaths p)
    {
        var folder = ProjectFolder(p);
        Recent.Remove(p);
        if (folder.Length == 0 || !string.Equals(Path.GetDirectoryName(folder), SamplesRoot, StringComparison.OrdinalIgnoreCase)) return;
        if (!RemovedSamples().Add(folder)) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(RemovedSamplesFile)!); File.WriteAllText(RemovedSamplesFile, System.Text.Json.JsonSerializer.Serialize(removedSamples!.Order().ToArray())); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say(false, "The list change could not be saved: " + e.Message); }
    }

    internal bool OpenRow(ProjectPaths p) => IsListedSample(p) ? OpenFolder(p.Folder!) : OpenRecent(p);

    private string RowKind(ProjectPaths p)
    {
        if (string.Equals(Path.GetDirectoryName(ProjectFolder(p)), SamplesRoot, StringComparison.OrdinalIgnoreCase)) return "Sample";
        var kind = Recent.Metadata(p).Kind;
        return kind.EndsWith(" connection", StringComparison.Ordinal) ? "Game" : kind;
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
            if (chosen is null) { EndProjectChooser(); return; }
            UseChosenProjectFolder(chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Say(false, "The folder dialog could not open: " + e.Message); }
    }

    private void DrawOpenProject()
    {
        ImGui.SeparatorText("Open project"u8);
        Ui.TextWrapped("Choose a project folder, or choose the data and rules folders below."u8);
        if (focusOpenFolder) { ImGui.SetKeyboardFocusHere(); focusOpenFolder = false; }
        ImGui.SetNextItemWidth(-1); Ui.InputText("Project folder"u8, projectFolder);
        if (Ui.Button("Browse for project..."u8))
        {
            if (FileDialogs is not null) BeginOpenProject();
            else { rootPicker = true; picker.Start(Text(projectFolder).Length == 0 ? personalProjects : Text(projectFolder), chooseFile: false); }
        }
        ImGui.SameLine();
        if (Ui.Button("Use this folder"u8)) OpenFolder(Text(projectFolder));
        DrawManualProject();
        if (rootPicker && picker.Draw(chooseFile: false) is { } chosen)
        {
            rootPicker = false;
            if (chosen.Length > 0) { projectFolder = new ImInputString(2048, chosen); if (!OpenFolder(chosen)) ChooseDataFolder(chosen); }
        }
    }

    private void DrawNewProject()
    {
        var newGame = startPage == StartPage.NewGame;
        Ui.Text(newGame ? "New game"u8 : "New project"u8);
        ImGui.Separator();
        if (focusNewName) { ImGui.SetKeyboardFocusHere(); focusNewName = false; }
        Ui.InputText("Name"u8, projectName); Ui.InputText("Location"u8, projectLocation);
        if (Ui.Button("Browse..."u8))
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
        if (!newGame && Ui.BeginCombo("Start from"u8, templates[templateIndex].Name))
        {
            for (var i = 0; i < templates.Count; i++) if (Ui.Selectable(templates[i].Name, templateIndex == i)) templateIndex = i;
            Ui.EndCombo();
        }
        var template = newGame ? "game" : templates[templateIndex].Id;
        var preview = projectCreator.Preview(template, Text(projectLocation), Text(projectName));
        Ui.TextWrapped(preview.CanCreate ? "Will create: " + preview.Folder : preview.Reason);
        Ui.BeginDisabled(!preview.CanCreate, preview.CanCreate ? null : preview.Reason);
        if (Ui.Button("Create and open"u8)) CreateProject(template, Text(projectLocation), Text(projectName));
        Ui.EndDisabled(); ImGui.SameLine();
        if (Ui.Button("Cancel"u8)) EndProjectChooser();
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
