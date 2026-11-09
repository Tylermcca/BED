using Brutal.Authoring;

namespace Ladder;

public sealed class NotebookValidator : IValidator<Notebook>
{
    public IReadOnlyList<Issue> Validate(Notebook d)
    {
        var issues = new List<Issue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, label, path) in d.Counters.Select((c, i) => (c.Id, c.Label, $"$.counters[{i}]"))
            .Concat(d.Notes.Select((n, i) => (n.Id, n.Label, $"$.notes[{i}]"))))
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                issues.Add(Issue.Error("Use a nonblank id that no other entry has.", string.IsNullOrWhiteSpace(id) ? "notebook" : id, path + ".id"));
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80)
                issues.Add(Issue.Error("Give this entry a label of 1 to 80 characters.", id, path + ".label"));
        }
        for (int i = 0; i < d.Counters.Length; i++)
            if (d.Counters[i].Value is < 0 or > 100)
                issues.Add(Issue.Error("Keep the counter between 0 and 100.", d.Counters[i].Id, $"$.counters[{i}].value"));
        return issues;
    }
}
