using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.TaskSchedulers;

[DebuggerTypeProxy(typeof(StackedTaskSchedulerDebugView))]
[DebuggerDisplay("Id={Id}, Queues={DebugQueueCount}, ScheduledTasks = {DebugTaskCount}")]
public sealed class StackedTaskScheduler : TaskScheduler, IDisposable, ITaskSchedulerExtentions
{
	private class QueueGroup : List<StackedTaskSchedulerQueue>
	{
		public int NextQueueIndex;

		public IEnumerable<int> CreateSearchOrder()
		{
			for (int j = NextQueueIndex; j < base.Count; j++)
			{
				yield return j;
			}
			for (int j = 0; j < NextQueueIndex; j++)
			{
				yield return j;
			}
		}
	}

	private class StackedTaskSchedulerDebugView
	{
		private readonly StackedTaskScheduler _scheduler;

		public IEnumerable<Task> ScheduledTasks
		{
			get
			{
				IEnumerable<Task> source;
				if (_scheduler._targetScheduler == null)
				{
					IEnumerable<Task> blockingTaskQueue = _scheduler._blockingTaskQueue;
					source = blockingTaskQueue;
				}
				else
				{
					IEnumerable<Task> blockingTaskQueue = _scheduler._nonthreadsafeTaskQueue;
					source = blockingTaskQueue;
				}
				return source.Where((Task t) => t != null).ToList();
			}
		}

		public IEnumerable<TaskScheduler> Queues
		{
			get
			{
				List<TaskScheduler> list = new List<TaskScheduler>();
				foreach (KeyValuePair<int, QueueGroup> queueGroup in _scheduler._queueGroups)
				{
					list.AddRange(queueGroup.Value);
				}
				return list;
			}
		}

		public StackedTaskSchedulerDebugView(StackedTaskScheduler scheduler)
		{
			if (scheduler == null)
			{
				throw new ArgumentNullException("scheduler");
			}
			_scheduler = scheduler;
		}
	}

	[DebuggerDisplay("QueuePriority = {_priority}, WaitingTasks = {WaitingTasks}")]
	[DebuggerTypeProxy(typeof(StackedTaskSchedulerQueueDebugView))]
	private sealed class StackedTaskSchedulerQueue : TaskScheduler, IDisposable
	{
		private sealed class StackedTaskSchedulerQueueDebugView
		{
			private readonly StackedTaskSchedulerQueue _queue;

			public int Priority => _queue._priority;

			public int Id => _queue.Id;

			public IEnumerable<Task> ScheduledTasks => _queue.GetScheduledTasks();

			public StackedTaskScheduler AssociatedScheduler => _queue._pool;

			public StackedTaskSchedulerQueueDebugView(StackedTaskSchedulerQueue queue)
			{
				if (queue == null)
				{
					throw new ArgumentNullException("queue");
				}
				_queue = queue;
			}
		}

		private readonly StackedTaskScheduler _pool;

		internal readonly int _priority;

		internal readonly Stack<Task> _workItems;

		internal bool _disposed;

		internal int WaitingTasks => _workItems.Count;

		public override int MaximumConcurrencyLevel => _pool.MaximumConcurrencyLevel;

		internal StackedTaskSchedulerQueue(int priority, StackedTaskScheduler pool)
		{
			_priority = priority;
			_pool = pool;
			_workItems = new Stack<Task>();
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			lock (_pool._queueGroups)
			{
				if (_workItems.Count == 0)
				{
					_pool.RemoveQueue_NeedsLock(this);
				}
			}
			_disposed = true;
		}

		protected override IEnumerable<Task> GetScheduledTasks()
		{
			return _workItems.ToList();
		}

		protected override void QueueTask(Task task)
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
			lock (_pool._queueGroups)
			{
				_workItems.Push(task);
			}
			_pool.NotifyNewWorkItem();
		}

		protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyStacked)
		{
			if (_taskProcessingThread.Value)
			{
				return TryExecuteTask(task);
			}
			return false;
		}

		internal void ExecuteTask(Task task)
		{
			TryExecuteTask(task);
		}
	}

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly ThreadLocal<bool> _taskProcessingThread = new ThreadLocal<bool>();

	private BlockingCollection<Task> _blockingTaskQueue;

	private readonly int _concurrencyLevel;

	private readonly CancellationTokenSource _disposeCancellation = new CancellationTokenSource();

	private readonly Stack<Task> _nonthreadsafeTaskQueue;

	private readonly SortedList<int, QueueGroup> _queueGroups = new SortedList<int, QueueGroup>();

	private readonly TaskScheduler _targetScheduler;

	private readonly Thread[] _threads;

	private int _delegatesStackedOrRunning;

	public int DebugQueueCount => _queueGroups.Sum((KeyValuePair<int, QueueGroup> group) => group.Value.Count);

	public int DebugTaskCount
	{
		get
		{
			IEnumerable<Task> source;
			if (_targetScheduler == null)
			{
				IEnumerable<Task> blockingTaskQueue = _blockingTaskQueue;
				source = blockingTaskQueue;
			}
			else
			{
				IEnumerable<Task> blockingTaskQueue = _nonthreadsafeTaskQueue;
				source = blockingTaskQueue;
			}
			return source.Count((Task t) => t != null);
		}
	}

	public override int MaximumConcurrencyLevel => _concurrencyLevel;

	public StackedTaskScheduler()
		: this(TaskScheduler.Default, 0)
	{
	}

	public StackedTaskScheduler(TaskScheduler targetScheduler)
		: this(targetScheduler, 0)
	{
	}

	public StackedTaskScheduler(TaskScheduler targetScheduler, int maxConcurrencyLevel)
	{
		if (targetScheduler == null)
		{
			throw new ArgumentNullException("underlyingScheduler");
		}
		if (maxConcurrencyLevel < 0)
		{
			throw new ArgumentOutOfRangeException("concurrencyLevel");
		}
		_targetScheduler = targetScheduler;
		_nonthreadsafeTaskQueue = new Stack<Task>();
		_concurrencyLevel = ((maxConcurrencyLevel != 0) ? maxConcurrencyLevel : Environment.ProcessorCount);
		if (targetScheduler.MaximumConcurrencyLevel > 0 && targetScheduler.MaximumConcurrencyLevel < _concurrencyLevel)
		{
			_concurrencyLevel = targetScheduler.MaximumConcurrencyLevel;
		}
	}

	public StackedTaskScheduler(int threadCount)
		: this(threadCount, string.Empty)
	{
	}

	public StackedTaskScheduler(int threadCount, string threadName = "", bool useForegroundThreads = false, ThreadPriority threadPriority = ThreadPriority.Normal, ApartmentState threadApartmentState = ApartmentState.MTA, int threadMaxStackSize = 0, Action threadInit = null, Action threadFinally = null)
	{
		StackedTaskScheduler stackedTaskScheduler = this;
		if (threadCount < 0)
		{
			throw new ArgumentOutOfRangeException("concurrencyLevel");
		}
		if (threadCount == 0)
		{
			_concurrencyLevel = Environment.ProcessorCount;
		}
		else
		{
			_concurrencyLevel = threadCount;
		}
		_blockingTaskQueue = new BlockingCollection<Task>(new ConcurrentStack<Task>());
		_threads = new Thread[threadCount];
		for (int i = 0; i < threadCount; i++)
		{
			_threads[i] = new Thread((ThreadStart)delegate
			{
				stackedTaskScheduler.ThreadBasedDispatchLoop(threadInit, threadFinally);
			}, threadMaxStackSize)
			{
				Priority = threadPriority,
				IsBackground = !useForegroundThreads
			};
			if (threadName != null)
			{
				_threads[i].Name = threadName + " (" + i + ")";
			}
			_threads[i].SetApartmentState(threadApartmentState);
		}
		Thread[] threads = _threads;
		for (int j = 0; j < threads.Length; j++)
		{
			threads[j].Start();
		}
	}

	public void CancelAllTasks()
	{
		if (_targetScheduler != null)
		{
			lock (_nonthreadsafeTaskQueue)
			{
				while (_nonthreadsafeTaskQueue.Count != 0)
				{
					_nonthreadsafeTaskQueue.Pop();
				}
				return;
			}
		}
		while (_blockingTaskQueue.Count > 0)
		{
			_blockingTaskQueue.TryTake(out var _);
		}
	}

	public void Dispose()
	{
		_disposeCancellation.Cancel();
	}

	private void ThreadBasedDispatchLoop(Action threadInit, Action threadFinally)
	{
		_taskProcessingThread.Value = true;
		threadInit?.Invoke();
		try
		{
			while (true)
			{
				try
				{
					foreach (Task item in _blockingTaskQueue.GetConsumingEnumerable(_disposeCancellation.Token))
					{
						if (item != null)
						{
							TryExecuteTask(item);
							continue;
						}
						Task targetTask;
						StackedTaskSchedulerQueue queueForTargetTask;
						lock (_queueGroups)
						{
							FindNextTask_NeedsLock(out targetTask, out queueForTargetTask);
						}
						if (targetTask != null)
						{
							queueForTargetTask.ExecuteTask(targetTask);
						}
					}
				}
				catch (ThreadAbortException)
				{
					if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
					{
						Thread.ResetAbort();
					}
				}
				catch (NullReferenceException ex2)
				{
					Log.Exception(ex2);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			threadFinally?.Invoke();
			_taskProcessingThread.Value = false;
		}
	}

	private void FindNextTask_NeedsLock(out Task targetTask, out StackedTaskSchedulerQueue queueForTargetTask)
	{
		targetTask = null;
		queueForTargetTask = null;
		foreach (KeyValuePair<int, QueueGroup> queueGroup in _queueGroups)
		{
			QueueGroup value = queueGroup.Value;
			foreach (int item in value.CreateSearchOrder())
			{
				queueForTargetTask = value[item];
				Stack<Task> workItems = queueForTargetTask._workItems;
				if (workItems.Count > 0)
				{
					targetTask = workItems.Pop();
					if (queueForTargetTask._disposed && workItems.Count == 0)
					{
						RemoveQueue_NeedsLock(queueForTargetTask);
					}
					value.NextQueueIndex = (value.NextQueueIndex + 1) % queueGroup.Value.Count;
					return;
				}
			}
		}
	}

	protected override void QueueTask(Task task)
	{
		if (_disposeCancellation.IsCancellationRequested)
		{
			throw new ObjectDisposedException(GetType().Name);
		}
		if (_targetScheduler == null)
		{
			_blockingTaskQueue.Add(task);
			return;
		}
		bool flag = false;
		lock (_nonthreadsafeTaskQueue)
		{
			_nonthreadsafeTaskQueue.Push(task);
			if (_delegatesStackedOrRunning < _concurrencyLevel)
			{
				_delegatesStackedOrRunning++;
				flag = true;
			}
		}
		if (flag)
		{
			Task.Factory.StartNew(ProcessPrioritizedAndBatchedTasks, CancellationToken.None, TaskCreationOptions.None, _targetScheduler);
		}
	}

	private void ProcessPrioritizedAndBatchedTasks()
	{
		bool flag = true;
		while (!_disposeCancellation.IsCancellationRequested && flag)
		{
			try
			{
				_taskProcessingThread.Value = true;
				while (!_disposeCancellation.IsCancellationRequested)
				{
					Task targetTask;
					lock (_nonthreadsafeTaskQueue)
					{
						if (_nonthreadsafeTaskQueue.Count == 0)
						{
							break;
						}
						targetTask = _nonthreadsafeTaskQueue.Pop();
						goto IL_0055;
					}
					IL_0055:
					StackedTaskSchedulerQueue queueForTargetTask = null;
					if (targetTask == null)
					{
						lock (_queueGroups)
						{
							FindNextTask_NeedsLock(out targetTask, out queueForTargetTask);
						}
					}
					if (targetTask != null)
					{
						if (queueForTargetTask != null)
						{
							queueForTargetTask.ExecuteTask(targetTask);
						}
						else
						{
							TryExecuteTask(targetTask);
						}
					}
				}
			}
			finally
			{
				lock (_nonthreadsafeTaskQueue)
				{
					if (_nonthreadsafeTaskQueue.Count == 0)
					{
						_delegatesStackedOrRunning--;
						flag = false;
						_taskProcessingThread.Value = false;
					}
				}
			}
		}
	}

	private void NotifyNewWorkItem()
	{
		QueueTask(null);
	}

	protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyStacked)
	{
		if (_taskProcessingThread.Value)
		{
			return TryExecuteTask(task);
		}
		return false;
	}

	protected override IEnumerable<Task> GetScheduledTasks()
	{
		if (_targetScheduler == null)
		{
			return _blockingTaskQueue.Where((Task t) => t != null).ToList();
		}
		return _nonthreadsafeTaskQueue.Where((Task t) => t != null).ToList();
	}

	public TaskScheduler ActivateNewQueue()
	{
		return ActivateNewQueue(0);
	}

	public TaskScheduler ActivateNewQueue(int priority)
	{
		StackedTaskSchedulerQueue stackedTaskSchedulerQueue = new StackedTaskSchedulerQueue(priority, this);
		lock (_queueGroups)
		{
			if (!_queueGroups.TryGetValue(priority, out var value))
			{
				value = new QueueGroup();
				_queueGroups.Add(priority, value);
			}
			value.Add(stackedTaskSchedulerQueue);
			return stackedTaskSchedulerQueue;
		}
	}

	private void RemoveQueue_NeedsLock(StackedTaskSchedulerQueue queue)
	{
		QueueGroup queueGroup = _queueGroups[queue._priority];
		int num = queueGroup.IndexOf(queue);
		if (queueGroup.NextQueueIndex >= num)
		{
			queueGroup.NextQueueIndex--;
		}
		queueGroup.RemoveAt(num);
	}
}
