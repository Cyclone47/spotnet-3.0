using System;

namespace Spotnet;

[Flags]
internal enum ErrorModes : uint
{
	SystemDefault = 0u,
	SemFailcriticalerrors = 1u,
	SemNoalignmentfaultexcept = 4u,
	SemNogpfaulterrorbox = 2u,
	SemNoopenfileerrorbox = 0x8000u
}
