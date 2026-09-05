namespace Spotnet.TaskSchedulers;

internal interface ITaskSchedulerExtentions
{
	int DebugTaskCount { get; }

	int DebugQueueCount { get; }

	void CancelAllTasks();
}
