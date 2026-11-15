"""Small source/staging checks for the playable new-game kit; no game or SDK is started."""
from pathlib import Path
import json
import re
import tempfile
import unittest
from xml.etree import ElementTree as ET
from tools import package_release

ROOT=Path(__file__).resolve().parents[1]

def template():
    text=(ROOT/'src/Bed.Editor.ImGui/ProjectTemplates.cs').read_text(encoding='utf-8-sig')
    manifest=json.loads(text.split('private const string Manifest = """',1)[1].split('""";',1)[0])
    return next(t for t in manifest['templates'] if t['id']=='game')

class NewGameStarterTests(unittest.TestCase):
    def test_package_copies_every_declared_input_and_the_current_guide(self):
        with tempfile.TemporaryDirectory(prefix='bed-new-game-stage-') as tmp:
            app=Path(tmp)/'app';stage=Path(tmp)/'stage'
            files=[name for name in template()['files'] if name not in ('AGENTS.md','CLAUDE.md')]
            for name in files:
                p=app/'samples/new-game'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(('original staging fixture '+name).encode())
            package_release.copy_new_game(stage,app)
            for name in files:self.assertEqual((app/'samples/new-game'/name).read_bytes(),(stage/'samples/new-game'/name).read_bytes())
            self.assertEqual((ROOT/'docs/guide/first_game_with_ai.md').read_bytes(),(stage/'guides/first_game_with_ai.md').read_bytes())
    def test_the_copied_game_uses_current_Gum_without_editor_or_AI_dependencies(self):
        project=ET.parse(ROOT/'samples/StarterGame/StarterGame.csproj').getroot()
        self.assertFalse(project.findall('.//ProjectReference'))
        packages=project.findall('.//PackageReference')
        self.assertEqual(['Gum.SkiaSharp.Standalone'],[p.attrib['Include'] for p in packages])
        central=ET.parse(ROOT/'Directory.Packages.props').getroot()
        pin=next(p.attrib['Version'] for p in central.findall('.//PackageVersion') if p.attrib['Include']=='Gum.SkiaSharp.Standalone')
        self.assertEqual(pin,packages[0].attrib['Version'])
        self.assertIn('source/StarterGame.csproj',template()['files'])
        self.assertNotIn('game/source/StarterGame.csproj',template()['files'])
    def test_the_guide_matches_the_saved_build_and_links_Gums_own_rules(self):
        guide=(ROOT/'docs/guide/first_game_with_ai.md').read_text(encoding='utf-8-sig')
        settings=json.loads((ROOT/'samples/NewGameProject/.bed/project.json').read_text())
        self.assertEqual(['publish','source/StarterGame.csproj','-c','Release','--self-contained','true','-o','game'],settings['play']['build']['arguments'])
        self.assertIn('dotnet '+ ' '.join(settings['play']['build']['arguments']),guide)
        self.assertIn('https://github.com/vchelaru/Gum/blob/main/CLAUDE.md',guide)
        self.assertIn('Preview lists the files and writes nothing.',guide)
        self.assertIn('--headless --script start,collect,collect,collect',guide)
        self.assertIn('BED connection',guide)

if __name__=='__main__':unittest.main()
