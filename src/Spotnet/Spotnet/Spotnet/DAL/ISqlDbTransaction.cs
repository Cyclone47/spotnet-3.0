using System;
using System.Data.Common;

namespace Spotnet.DAL;

public interface ISqlDbTransaction : IDisposable
{
	DbTransaction Transaction { get; }

	void Commit();

	void Rollback();
}
