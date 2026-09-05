using System.Reflection;
using System.Runtime.CompilerServices;

// Moved code keeps the visibility it had inside Spotnet.dll. Widening these types to
// public just to cross an assembly boundary would enlarge the API surface for no reason.
[assembly: InternalsVisibleTo("Spotnet")]
[assembly: InternalsVisibleTo("Spotnet.Tests")]
[assembly: InternalsVisibleTo("Spotnet.AutoTests")]

[assembly: AssemblyTitle("Spotnet.Core")]
[assembly: AssemblyDescription("Platform-neutral Spotnet core shared by the Windows and macOS clients.")]
