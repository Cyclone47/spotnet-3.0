using System;
using System.Linq;
using System.ServiceProcess;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Helpers;

internal class ServiceManager
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static bool IsInstalled(string serviceName)
	{
		return ServiceController.GetServices().Any((ServiceController service) => service.ServiceName == serviceName);
	}

	public static bool IsRunning(string serviceName)
	{
		using ServiceController serviceController = new ServiceController(serviceName);
		if (!IsInstalled(serviceName))
		{
			return false;
		}
		return serviceController.Status == ServiceControllerStatus.Running;
	}

	public static void StartService()
	{
		if (!IsInstalled(Configuration.UpdaterServiceName))
		{
			return;
		}
		using ServiceController serviceController = new ServiceController(Configuration.UpdaterServiceName);
		try
		{
			if (serviceController.Status != ServiceControllerStatus.Running)
			{
				serviceController.Start();
				serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10.0));
			}
		}
		catch
		{
			throw;
		}
	}

	public static void StopService()
	{
		if (!IsInstalled(Configuration.UpdaterServiceName))
		{
			return;
		}
		using ServiceController serviceController = new ServiceController(Configuration.UpdaterServiceName);
		try
		{
			if (serviceController.Status != ServiceControllerStatus.Stopped)
			{
				serviceController.Stop();
				serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10.0));
			}
		}
		catch
		{
			throw;
		}
	}
}
