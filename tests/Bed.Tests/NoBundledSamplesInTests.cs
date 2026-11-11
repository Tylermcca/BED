using System.Runtime.CompilerServices;
using Bed.Editor.ImGui;

namespace Bed.Tests;

// The test build has its own samples folder; start screens in tests list none unless a test sets SamplesFolder itself.
internal static class NoBundledSamplesInTests
{
    [ModuleInitializer]
    internal static void Init() => ProjectLauncher.DefaultSamplesFolder = Path.Combine(Path.GetTempPath(), "bed-tests-no-samples");
}
