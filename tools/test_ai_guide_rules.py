"""Check the copyable rules and the shipped Claude pointer without running an assistant."""
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parents[1]

def validate(guide,index):
    blocks=re.findall(r"```markdown\n(.*?)\n```",guide,re.S)
    assert len(blocks)==1,"Keep one complete copyable rules file"
    sample=blocks[0]
    shipped=re.search(r'const string Text = """\n(.*?)\n\s*"""',index,re.S).group(1)
    for text in (sample,shipped):
        assert "bed-cli" in text
        assert "not" in text and "data files by hand" in text
        assert "couldn't check:" in text
        assert "findings" in text and "person decides" in text
        assert "typed command" in text and "save" in text
        assert "git reset --hard" in text or "discard history" in text
    pointer=re.search(r'const string ClaudeInstructions = "(.*?)";',index).group(1)
    assert pointer==r"Read AGENTS.md before changing this project.\n"
    assert 'Read AGENTS.md before changing this project.' in guide
    for command in ('Remove-Item -Recurse','rm -rf','git reset --hard','git clean -fd','git push --force'):
        assert command in guide

class AiGuideRulesTests(unittest.TestCase):
    def test_copyable_rules_and_shipped_helper_agree(self):
        validate((ROOT/'docs/guide/using_bed_with_ai.md').read_text(encoding='utf-8-sig'),(ROOT/'src/Bed.Editor/AgentIndex.cs').read_text(encoding='utf-8-sig'))
    def test_a_broken_Claude_pointer_is_rejected(self):
        guide=(ROOT/'docs/guide/using_bed_with_ai.md').read_text(encoding='utf-8-sig')
        index=(ROOT/'src/Bed.Editor/AgentIndex.cs').read_text(encoding='utf-8-sig').replace('Read AGENTS.md before changing this project.','Ignore project instructions.')
        with self.assertRaises(AssertionError):validate(guide,index)

if __name__=='__main__':unittest.main()
