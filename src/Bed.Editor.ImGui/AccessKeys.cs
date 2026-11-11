using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>One menu-local access letter and its measured label position. No UI-tree mutation or input dispatch is implied.</summary>
/// <param name="Id">Stable action or menu id.</param>
/// <param name="Label">Visible label.</param>
/// <param name="Letter">Uppercase access letter.</param>
/// <param name="Index">UTF-16 position of that ASCII letter in the label.</param>
public sealed record AccessKey(string Id, string Label, char Letter, int Index);

/// <summary>Menu-local mnemonic assignment and underline geometry; existing action descriptors still own invocation and refusals.</summary>
public static class AccessKeys
{
    /// <summary>Assigns the first unused ASCII letter in each visible label, deterministically within this menu only. Labels without a free letter remain unassigned.</summary>
    public static IReadOnlyList<AccessKey> Assign(IEnumerable<(string Id, string Label)> items)
    {
        var used = new HashSet<char>(); var ids = new HashSet<string>(StringComparer.Ordinal); var result = new List<AccessKey>();
        foreach (var (id, raw) in items)
        {
            if (!ids.Add(id)) throw new ArgumentException("Access-key ids must be unique within one menu.", nameof(items));
            var hidden = raw.IndexOf("##", StringComparison.Ordinal);
            var label = hidden < 0 ? raw : raw[..hidden];
            for (var i = 0; i < label.Length; i++)
            {
                var letter = char.ToUpperInvariant(label[i]);
                if (letter is < 'A' or > 'Z' || !used.Add(letter)) continue;
                result.Add(new(id, label, letter, i));
                break;
            }
        }
        return result;
    }

    /// <summary>Resolves only an Alt-letter gesture outside text entry. Ctrl+Alt/AltGr and system chords never become menu input.</summary>
    public static AccessKey? Resolve(IEnumerable<AccessKey> menu, char letter, bool alt, bool textInput, bool ctrl = false, bool super = false)
    {
        if (!alt || textInput || ctrl || super) return null;
        var matches = menu.Where(item => item.Letter == char.ToUpperInvariant(letter)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Invokes exactly one existing visible, enabled action for a resolved gesture. Disabled or hidden actions retain their ordinary refusal boundary.</summary>
    public static bool Invoke(IEnumerable<AccessKey> menu, IReadOnlyList<UiAction> actions, char letter, bool alt, bool textInput, bool ctrl = false, bool super = false)
    {
        var key = Resolve(menu, letter, alt, textInput, ctrl, super);
        if (key is null) return false;
        var action = actions.FirstOrDefault(a => a.Id == key.Id);
        if (action?.Present(UiActionContext.Palette) is not { Visible: true, Enabled: true }) return false;
        return action.Invoke();
    }

    /// <summary>Draws a dialog button with an explicit access letter; normal button and guarded Alt input share the same return value.</summary>
    public static bool Button(string label, char letter)
    {
        var visible = label.Split("##", 2)[0];
        var index = visible.IndexOf(letter.ToString(), StringComparison.OrdinalIgnoreCase);
        if (letter is < 'A' or > 'Z' || index < 0) throw new ArgumentException("The uppercase access letter must occur in the button label.", nameof(letter));
        var clicked = Ui.Button(label);
        Ui.AccessKeyLast(letter.ToString());
        var min = ImGui.GetItemRectMin(); var size = ImGui.GetItemRectSize();
        var text = ImGui.CalcTextSize(visible); var align = ImGui.GetStyle().ButtonTextAlign;
        var origin = min + new float2(MathF.Max(0, size.X - text.X) * align.X, MathF.Max(0, size.Y - text.Y) * align.Y);
        DrawUnderline(new(label, visible, letter, index), origin);
        var io = ImGui.GetIO();
        return clicked || Ui.Tree.At(Ui.Tree.Count - 1).Enabled && Resolve(new[] { new AccessKey(label, visible, letter, index) }, letter,
            io.KeyAlt, io.WantTextInput, io.KeyCtrl, io.KeySuper) is not null &&
            ImGui.IsWindowFocused() && ImGui.IsKeyPressed((ImGuiKey)((int)ImGuiKey.A + letter - 'A'), false);
    }

    /// <summary>Measures the underline using the actual font and prefix width, relative to the caller's actual text origin.</summary>
    public static UiBounds Underline(AccessKey key, float2 textOrigin)
    {
        if (key.Index < 0 || key.Index >= key.Label.Length || char.ToUpperInvariant(key.Label[key.Index]) != key.Letter)
            throw new ArgumentException("The access letter must name the supplied visible label character.", nameof(key));
        var prefix = ImGui.CalcTextSize(key.Label[..key.Index]).X;
        var letter = ImGui.CalcTextSize(key.Label.Substring(key.Index, 1)).X;
        return new(textOrigin.X + prefix, textOrigin.Y + ImGui.GetFontSize() - 1, letter, 1);
    }

    /// <summary>Draws the measured underline only while Alt is held and no text field or Ctrl/OS chord owns the input.</summary>
    public static bool DrawUnderline(AccessKey key, float2 textOrigin)
    {
        var io = ImGui.GetIO();
        if (!io.KeyAlt || io.WantTextInput || io.KeyCtrl || io.KeySuper) return false;
        var line = Underline(key, textOrigin);
        ImGui.GetWindowDrawList().AddLine(new float2(line.X, line.Y), new float2(line.X + line.Width, line.Y), ImGui.GetColorU32(ImGuiCol.Text), line.Height);
        return true;
    }
}
