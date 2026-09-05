using System;

namespace Spotnet.Properties;

[Flags]
internal enum SpotsSourceEnum
{
	None = 0,
	FileCache = 1,
	ImageFromFullImagesGroup = 2,
	ImageByUrl = 4,
	ImageFromThumbsGroup = 8
}
