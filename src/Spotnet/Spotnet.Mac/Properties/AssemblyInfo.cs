using System.Runtime.CompilerServices;

// The post-process steps are driven by parsing tool output and archive headers.
// The tests exercise those parsers directly with recorded output and synthesised
// archives, rather than requiring par2/unrar to be installed on the build machine.
[assembly: InternalsVisibleTo("Spotnet.Mac.Tests")]
