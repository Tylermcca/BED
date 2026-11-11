namespace Bed.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private AssetCatalog? outlineNamesCatalog;
    private AssetCatalog.Input[] outlineNameInputs=[];
    private IReadOnlyDictionary<string,string> outlineNames=new Dictionary<string,string>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string,string> SceneAssetSearchNames()
    {
        var catalog=SceneAssets;var inputs=catalog?.CurrentInputs()??[];
        if(ReferenceEquals(catalog,outlineNamesCatalog)&&inputs.SequenceEqual(outlineNameInputs))return outlineNames;
        outlineNamesCatalog=catalog;outlineNameInputs=inputs.ToArray();
        outlineNames=inputs.GroupBy(i=>i.Guid,StringComparer.Ordinal).Where(g=>g.Count()==1)
            .ToDictionary(g=>g.Key,g=>g.Single().ProducingRecord.Source,StringComparer.Ordinal);
        return outlineNames;
    }
}
