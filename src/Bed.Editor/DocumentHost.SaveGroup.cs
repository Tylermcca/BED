using Bed.Data;
namespace Bed.Editor;

public sealed partial class DocumentHost
{
    private sealed record SavePart(DataFile File,string Text,string? Baseline,Func<string?> Check,Func<string?> Current,Action<string> Written,bool Data=false);
    internal sealed class MixedSave(IReadOnlyList<DataFile> files,Task<string?> verdict,Func<IReadOnlyList<DataFile>> finish,Action release):IDisposable
    {
        internal IReadOnlyList<DataFile> Files=>files;
        internal Task<string?> Verdict=>verdict;
        private IReadOnlyList<DataFile>? result;private bool closed;
        internal IReadOnlyList<DataFile> Complete()
        {
            if(result is not null)return result;
            if(closed||!verdict.IsCompleted)return files;
            try{return result=finish();}finally{Dispose();}
        }
        public void Dispose(){if(closed)return;closed=true;release();}
    }
    private IReadOnlyList<DataFile> SaveMixedGroup()=>BeginMixedSave(false).Complete();
    internal MixedSave BeginMixedSave(bool background,Func<string?>? ownerCurrent=null)
    {
        var projectPolicy=project.SaveCheck;var parts=new List<SavePart>();
        foreach(var file in project.Files)
        {
            if(file.Session is {} data&&data.Dirty)
            {
                var revision=data.Revision;var baseline=data.ExpectedOnDisk;
                string? Current()
                {
                    if(!ReferenceEquals(file.Session,data)||data.Revision!=revision||data.ExpectedOnDisk!=baseline)return "The data draft changed; save again.";
                    return project.FileAccessRefusal(file.Name)??Scene2Source.PathRefusal(file.Path)??file.WhyNotNow(revision);
                }
                string? Check(){var issues=data.Validator.Validate(data.Current);return issues.HasErrors()?string.Join(" ",issues.Where(i=>i.Severity==Severity.Error).Select(i=>i.Message)):file.WhyNotSaved();}
                parts.Add(new(file,data.TextToSave,baseline,Check,Current,file.Written,true));
            }
            if(scenes.TryGetValue(file.Name,out var one)&&one.Session.Dirty)
                Add(file,one,one.Session,()=>scenes.TryGetValue(file.Name,out var now)&&ReferenceEquals(now,one),()=>one.Editor.Playing is not null,()=>SceneSaveCheck,()=>SceneSaveCheck?.Invoke(one));
            if(scene2.TryGetValue(file.Name,out var two)&&two.Session.Dirty)
                Add(file,two,two.Session,()=>scene2.TryGetValue(file.Name,out var now)&&ReferenceEquals(now,two),()=>two.Editor.Playing is not null,()=>Scene2SaveCheck,()=>Scene2SaveCheck?.Invoke(two));
        }
        if(parts.Count==0)return new([],Task.FromResult<string?>(null),()=>[],()=>{});
        IReadOnlyList<DataFile> Refuse(string why)
        {foreach(var part in parts)part.File.Refuse("Save all: "+why);return parts.Select(p=>p.File).ToArray();}
        if(checking||project.Judging)return new(parts.Select(p=>p.File).ToArray(),Task.FromResult<string?>(null),()=>Refuse("A save check is in progress; keep the drafts open."),()=>{});
        checking=true;var previous=project.SavingTogether;project.SavingTogether=parts.Where(p=>p.Data).Select(p=>p.File).ToArray();
        string? CheckData()
        {
            try{foreach(var part in parts.Where(p=>p.Data))if(part.Check() is {} why)return part.File.Name+": "+why;return null;}
            catch(Exception e)when(e is not OutOfMemoryException){return "The data check failed: "+e.Message;}
        }
        var verdict=background?project.CheckSaveGroupInBackground(CheckData):Task.FromResult(CheckData());
        void Release(){project.SavingTogether=previous;checking=false;}
        IReadOnlyList<DataFile> Finish()
        {
            try
            {
                if(!verdict.IsCompletedSuccessfully)return Refuse("The data check failed: "+verdict.Exception?.GetBaseException().Message);
                if(verdict.Result is {} dataWhy)return Refuse(dataWhy);
                if(ownerCurrent?.Invoke() is {} ownerWhy)return Refuse(ownerWhy);
                foreach(var part in parts.Where(p=>!p.Data))if(part.Check() is {} why)return Refuse(part.File.Name+": "+why);
                if(project.Judging)return Refuse("A save check started; keep the drafts open.");
                if(!ReferenceEquals(projectPolicy,project.SaveCheck))return Refuse("The project save policy changed; save again.");
                foreach(var part in parts)if(part.Current() is {} why)return Refuse(part.File.Name+": "+why);
                if(ownerCurrent?.Invoke() is {} changedOwner)return Refuse(changedOwner);
                var result=FileGroup.Write(parts.Select(p=>new GroupFile(p.File.Path,p.Baseline,p.Text)).ToArray());var written=new List<DataFile>();
                for(var i=0;i<parts.Count;i++)if(result.States[i]==GroupFileState.Written)
                {parts[i].Written(parts[i].Text);parts[i].File.ClearProblems();written.Add(parts[i].File);}
                project.NotifySaved(written);
                if(result.Failed is null&&result.States.All(s=>s==GroupFileState.Written))return [];
                var failed=parts.Where((_,i)=>result.States[i]!=GroupFileState.Written).Select(p=>p.File).ToArray();
                var message=result.Why??"The group could not be committed; drafts were retained.";if(written.Count>0)message+=" Already written and not rolled back: "+string.Join(", ",written.Select(f=>f.Name))+"; check those files.";
                foreach(var file in failed)file.Refuse("Save all: "+message);return failed;
            }
            catch(Exception e)when(e is not OutOfMemoryException){return Refuse("The group was refused: "+e.Message);}
        }
        return new(parts.Select(p=>p.File).ToArray(),verdict,Finish,Release);

        void Add<T>(DataFile file,object entry,DocumentSession<T> session,Func<bool> owner,Func<bool> playing,Func<object?> policy,Func<string?> check) where T:class
        {
            var revision=session.Revision;var baseline=session.ExpectedOnDisk;var capturedPolicy=policy();
            string? Current()
            {
                if(!owner()||session.Revision!=revision||session.ExpectedOnDisk!=baseline||!ReferenceEquals(capturedPolicy,policy()))return "The scene or save policy changed; save again.";
                if(playing())return "Stop Play before saving.";
                if(project.FileAccessRefusal(file.Name) is {} admission)return admission;
                if(Scene2Source.PathRefusal(file.Path) is {} unsafePath)return unsafePath;
                if(project.ReadOnlyReason(file.Name) is {} readOnly)return readOnly;
                if(project.SaveCheck is not null&&capturedPolicy is null)return "This game's data check requires its explicit scene save policy.";
                if(session.CheckDisk()!=DiskState.Unchanged)return "The saved level was changed outside BED; choose which version to keep.";
                return null;
            }
            string? Check()
            {
                if(Current() is {} why)return why;var issues=session.Validator.Validate(session.Current);
                if(issues.HasErrors())return string.Join(" ",issues.Where(i=>i.Severity==Severity.Error).Select(i=>i.Message));return check();
            }
            parts.Add(new(file,session.TextToSave,baseline,Check,Current,text=>session.MarkSaved(text)));
        }
    }
}
