using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;

namespace Spotnet.AutoTests;

public class DbUpdateTest : TestBase
{
	private NntpSettings _headerSettings;

	private RSACryptoServiceProvider[] _rsa;

	public override void Start()
	{
		base.Start();
		_headerSettings = AppHelper.HeaderSettings(bIncludePosition: true);
		_rsa = SpotHelper.GetRsa(_headerSettings.TrustedKeys);
	}

	public override void Run()
	{
		TestBase.Log.Debug("Run ParseHeadersTest");
		ParseHeadersTest();
	}

	public void ParseHeadersTest()
	{
		BlockingCollection<List<Spot>> blockingCollection = new BlockingCollection<List<Spot>>();
		Headers.InitializeForAutoTests(blockingCollection);
		try
		{
			Tracker tracker = new Tracker();
			_ = new int[7] { 10, 4858, 4755, 4816, 4900, 4966, 4940 };
			for (int i = 0; i < 7; i++)
			{
				Headers.ParseHeaders(_headerSettings, _rsa, 0, File.ReadAllText("../../../AutoTests/Data/Headers{0}.txt".Format(i + 1)));
				blockingCollection.ToList();
				tracker.Debug("List {0} parsed".Format(i + 1));
			}
		}
		catch (Exception ex)
		{
			TestBase.Log.Exception(ex);
		}
	}
}
