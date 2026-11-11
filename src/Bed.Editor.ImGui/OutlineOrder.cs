using Bed.Editor;
namespace Bed.Editor.ImGui;

// One hierarchy order for Outliner rows and keyboard picking. Cycles emit each ID once.
internal static class OutlineOrder
{
    internal static IReadOnlyList<(string Id,int Depth,bool Place)> Rows<TDoc>(EditorModel<TDoc> editor) where TDoc:class
    {
        var doc=editor.Session.Current;var ids=editor.Outline.ObjectIds(doc).ToList();var present=ids.ToHashSet(StringComparer.Ordinal);
        var children=ids.ToLookup(id=>editor.Outline.Parent(doc,id) is {} parent&&present.Contains(parent)?parent:"",StringComparer.Ordinal);
        var drawn=new HashSet<string>(StringComparer.Ordinal);var rows=new List<(string,int,bool)>();
        void Row(string root)
        {
            var pending=new Stack<(string Id,int Depth)>();pending.Push((root,0));
            while(pending.TryPop(out var item))
            {
                if(!drawn.Add(item.Id))continue;
                var nested=children[item.Id].ToArray();rows.Add((item.Id,item.Depth,nested.Length>0));
                for(var i=nested.Length-1;i>=0;i--)pending.Push((nested[i],item.Depth+1));
            }
        }
        if(editor.FocusedOn is {} focus)Row(focus);else foreach(var root in children[""])Row(root);
        foreach(var id in ids.Where(id=>!drawn.Contains(id)&&editor.InFocus(id)))Row(id);
        return rows;
    }
}
