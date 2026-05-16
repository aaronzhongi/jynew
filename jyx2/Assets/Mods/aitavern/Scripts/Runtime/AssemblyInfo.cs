// Exposes `internal` runtime members to the editor-mode test assembly so
// the layered-memory tests can drive internals without widening the public
// API surface. Keep this minimal — only the test asmdef is exempted.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AITavern.Tests.Editor")]
