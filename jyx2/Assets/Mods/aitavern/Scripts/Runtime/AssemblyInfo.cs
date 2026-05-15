// Exposes `internal` runtime members to the editor-mode test assembly so
// Phase 2 (T8) tests can call BuildPriorMemoryBlock without changing the
// public API surface. Keep this minimal — only the test asmdef is exempted.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AITavern.Tests.Editor")]
