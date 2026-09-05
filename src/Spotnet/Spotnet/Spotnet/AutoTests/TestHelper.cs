using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.AutoTests;

public static class TestHelper
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object LockRoot = new object();

	private static bool _isAlreadyRunning;

	public static async Task RunAllAsync()
	{
		lock (LockRoot)
		{
			if (_isAlreadyRunning)
			{
				Log.Debug("Tests are running already");
				return;
			}
			_isAlreadyRunning = true;
		}
		try
		{
			Type myType = typeof(TestHelper);
			List<Type> list = (from t in Assembly.GetExecutingAssembly().GetTypes().ToList()
				where t.Namespace == myType.Namespace && t.Name.EndsWith("Test")
				select t).ToList();
			Type baseType = typeof(TestBase);
			foreach (Type item in list)
			{
				object testInstance = Activator.CreateInstance(item.UnderlyingSystemType, null);
				await Task.Run(() => baseType.GetMethod("Start").Invoke(testInstance, null));
				await Task.Run(() => baseType.GetMethod("Run").Invoke(testInstance, null));
				await Task.Run(() => baseType.GetMethod("Stop").Invoke(testInstance, null));
			}
		}
		finally
		{
			_isAlreadyRunning = false;
		}
	}
}
