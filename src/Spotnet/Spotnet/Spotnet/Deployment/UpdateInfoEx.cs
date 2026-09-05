using System;
using System.Collections.Generic;
using System.Linq;
using Spotnet.Helpers;
using Squirrel;

namespace Spotnet.Deployment;

internal class UpdateInfoEx : UpdateInfo
{
	public Exception Exception;

	public bool IsNewVersionAvailable
	{
		get
		{
			if (base.ReleasesToApply != null && base.ReleasesToApply.Any())
			{
				return base.FutureReleaseEntry.Version > AppHelper.AppVersion;
			}
			return false;
		}
	}

	public UpdateInfoEx(ReleaseEntry currentlyInstalledVersion, IEnumerable<ReleaseEntry> releasesToApply, string packageDirectory, FrameworkVersion appFrameworkVersion)
		: base(currentlyInstalledVersion, releasesToApply, packageDirectory, appFrameworkVersion)
	{
	}

	public UpdateInfoEx(UpdateInfo info)
		: base(info.CurrentlyInstalledVersion, info.ReleasesToApply, info.PackageDirectory, info.AppFrameworkVersion)
	{
	}

	public UpdateInfoEx(Exception ex)
		: base(null, null, null, FrameworkVersion.Net40)
	{
		Exception = ex;
	}

	public UpdateInfoEx(string errorMessage)
		: base(null, null, null, FrameworkVersion.Net40)
	{
		Exception = new Exception(errorMessage);
	}
}
