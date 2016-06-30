using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Task scheduling with interval or specific run times.
/// </summary>
public class TaskWrapper {
	/// <summary>
	/// List of all registered actions.
	/// </summary>
	public static List<TaskWrapperEntry> Entries = new List<TaskWrapperEntry>();

	/// <summary>
	/// List of log functions.
	/// </summary>
	public static List<Action<TaskWrapperEntry, string, TaskWrapperLogType, Exception>> LogActions = new List<Action<TaskWrapperEntry, string, TaskWrapperLogType, Exception>>();

	/// <summary>
	/// Different types of log messages.
	/// </summary>
	public enum TaskWrapperLogType {
		Info,
		Warning,
		Exception
	}

	/// <summary>
	/// Add a new log entry.
	/// </summary>
	public static void Log(TaskWrapperEntry entry, string message, TaskWrapperLogType logType, Exception exception = null) {
		foreach (var logAction in LogActions)
			logAction.Invoke(
				entry,
				message,
				logType,
				exception);
	}

	/// <summary>
	/// Pause a task by name.
	/// </summary>
	public static void Pause(string name) {
		var entry = Entries.SingleOrDefault(n => n.Name == name);

		if (entry == null)
			return;

		entry.Pause();
	}

	/// <summary>
	/// Queue up an action to run at given intervals.
	/// </summary>
	public static void Register(TaskWrapperEntry entry) {
		if (string.IsNullOrWhiteSpace(entry.Name))
			throw new Exception("Name is required.");

		if (entry.Action == null)
			throw new Exception("Action is required.");

		if (Entries.SingleOrDefault(n => n.Name == entry.Name) != null)
			throw new Exception("Task with same name already exists.");

		Entries.Add(entry);

		Log(
			entry,
			"Registered new TaskWrapperEntry.",
			TaskWrapperLogType.Info);

		if (entry.RunAtRegister)
			entry.Run();
		else
			entry.QueueNextRun(null);
	}

	/// <summary>
	/// Remove a task by name.
	/// </summary>
	public static void Remove(string name) {
		var entry = Entries.SingleOrDefault(n => n.Name == name);

		if (entry == null)
			return;

		entry.Stop();

		Log(
			entry,
			"Removing.",
			TaskWrapperLogType.Info);

		Entries.Remove(entry);

		entry.Interval = null;
		entry.DateTimes = null;
		entry.Removed = true;
	}

	/// <summary>
	/// Resume a task by name.
	/// </summary>
	public static void Resume(string name) {
		var entry = Entries.SingleOrDefault(n => n.Name == name);

		if (entry == null)
			return;

		entry.Resume();
	}

	/// <summary>
	/// Stop a task by name.
	/// </summary>
	public static void Stop(string name) {
		var entry = Entries.SingleOrDefault(n => n.Name == name);

		if (entry == null)
			return;

		entry.Stop();
	}
}

public class TaskWrapperEntry {
	#region Properties

	/// <summary>
	/// Name of the registered task.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Whether or not the task should run.
	/// </summary>
	public bool Enabled { get; private set; }

	/// <summary>
	/// Whether or not the task has been removed.
	/// </summary>
	public bool Removed { get; set; }

	/// <summary>
	/// Whether or not to run the function when it is registered.
	/// </summary>
	public bool RunAtRegister { get; set; }

	/// <summary>
	/// Whether or not to run the function in a separate thread.
	/// </summary>
	public bool RunThreaded { get; set; }

	/// <summary>
	/// Whether or not the task is currently running.
	/// </summary>
	public bool IsRunning { get; private set; }
	
	/// <summary>
	/// When the function was registered.
	/// </summary>
	public DateTime Created { get; private set; }

	/// <summary>
	/// The last time the run-function was called.
	/// </summary>
	public DateTime LastAttemptedRun { get; private set; }

	/// <summary>
	/// The last time the function was attempted to start.
	/// </summary>
	public DateTime LastRunStarted { get; private set; }

	/// <summary>
	/// The last time the function was completed.
	/// </summary>
	public DateTime LastRunEnded { get; private set; }

	/// <summary>
	/// The function to call when the time is right.
	/// </summary>
	public Action<TaskWrapperEntry> Action { get; set; }

	/// <summary>
	/// Interval between each run.
	/// </summary>
	public TimeSpan? Interval { get; set; }

	/// <summary>
	/// A list of times (the date-part is ignored) when the function is to be runned.
	/// </summary>
	public List<DateTime> DateTimes { get; set; }

	/// <summary>
	/// Function which is called just before the action-function is to be called, allowing you to postpone it.
	/// </summary>
	public Func<TaskWrapperEntry, TimeSpan> VerifyAtRuntime { get; set; }

	/// <summary>
	/// Background thread to run the function in, if spesified.
	/// </summary>
	public Thread Thread { get; private set; }

	/// <summary>
	/// List of exceptions that has ocured during runs.
	/// </summary>
	public List<Exception> Exceptions { get; private set; }

	#endregion
	#region Constructors

	/// <summary>
	/// Create a new instance of a TaskWrapperEntry.
	/// </summary>
	public TaskWrapperEntry() {
		this.Created = DateTime.Now;
		this.Exceptions = new List<Exception>();
		this.Enabled = true;
	}

	#endregion
	#region Instance Methods

	/// <summary>
	/// Pause the task.
	/// </summary>
	public void Pause() {
		TaskWrapper.Log(
			this,
			"Paused.",
			TaskWrapper.TaskWrapperLogType.Info);

		this.Enabled = false;
	}

	/// <summary>
	/// Queue up the next run.
	/// </summary>
	public async Task QueueNextRun(TimeSpan? postpone) {
		TimeSpan? timeSpan = null;

		// Queue up based on postpone time-span.
		if (postpone.HasValue)
			timeSpan = postpone.Value;

		// Queue up the next interval.
		if (timeSpan == null &&
			this.Interval.HasValue)
			timeSpan = this.Interval.Value;

		// Queue up the next time-to-run.
		if (timeSpan == null &&
			this.DateTimes != null &&
			this.DateTimes.Any()) {
			var now = DateTime.Now;

			foreach (var dateTime in this.DateTimes.OrderBy(t => t)) {
				var dt = new DateTime(
					now.Year,
					now.Month,
					now.Day,
					dateTime.Hour,
					dateTime.Minute,
					dateTime.Second);

				if (dt <= now)
					continue;

				timeSpan = dt - now;
				break;
			}
		}

		// Task up the next run.
		if (timeSpan == null)
			return;

		TaskWrapper.Log(
			this,
			string.Format(
				"Next run will be in {0}.",
				timeSpan),
			TaskWrapper.TaskWrapperLogType.Info);

		// Wait the alotted time.
		await Task.Delay(timeSpan.Value);

		// Attempt to run the action now.
		Run();
	}

	/// <summary>
	/// Remove the task from the list of entries.
	/// </summary>
	public void Remove() {
		this.Stop();

		TaskWrapper.Log(
			this,
			"Removing.",
			TaskWrapper.TaskWrapperLogType.Info);

		var entry = TaskWrapper.Entries.SingleOrDefault(n => n.Name == this.Name);

		if (entry == null)
			return;

		TaskWrapper.Entries.Remove(entry);

		this.Thread = null;
		this.Interval = null;
		this.DateTimes = null;
	}

	/// <summary>
	/// Resume the task.
	/// </summary>
	public void Resume() {
		TaskWrapper.Log(
			this,
			"Resuming.",
			TaskWrapper.TaskWrapperLogType.Info);

		this.Enabled = true;
	}

	/// <summary>
	/// Attempt to run the action now.
	/// </summary>
	public void Run() {
		if (this.Removed)
			return;

		if (!this.Enabled) {
			TaskWrapper.Log(
				this,
				"Skipping run since task is disabled.",
				TaskWrapper.TaskWrapperLogType.Warning);

			this.QueueNextRun(null);
			return;
		}

		TaskWrapper.Log(
			this,
			"Preparing to run function.",
			TaskWrapper.TaskWrapperLogType.Info);

		this.LastAttemptedRun = DateTime.Now;

		// Run VerifyAtRuntime
		if (this.VerifyAtRuntime != null) {
			TaskWrapper.Log(
				this,
				"Found verify function, running.",
				TaskWrapper.TaskWrapperLogType.Info);

			var postpone = this.VerifyAtRuntime.Invoke(this);

			if (postpone.Ticks > 0) {
				TaskWrapper.Log(
					this,
					string.Format(
						"Postponing the function {0} seconds.",
						postpone.TotalSeconds),
					TaskWrapper.TaskWrapperLogType.Warning);

				this.QueueNextRun(postpone);
				return;
			}
		}

		if (this.RunThreaded) {
			if (this.Thread != null &&
			    this.Thread.IsAlive) {
				TaskWrapper.Log(
					this,
					"Background thread is still running, so we will not start a new one just yet.",
					TaskWrapper.TaskWrapperLogType.Warning);

				return;
			}

			this.Thread = new Thread(() => {
				try {
					this.LastRunStarted = DateTime.Now;
					this.IsRunning = true;

					TaskWrapper.Log(
						this,
						"Running main function.",
						TaskWrapper.TaskWrapperLogType.Info);

					this.Action.Invoke(this);

					this.IsRunning = false;
					this.LastRunEnded = DateTime.Now;

					var span = this.LastRunEnded - this.LastRunStarted;

					TaskWrapper.Log(
						this,
						string.Format(
							"Run completed successfully. Took {0} seconds.",
							span.TotalSeconds),
						TaskWrapper.TaskWrapperLogType.Info);
				}
				catch (Exception ex) {
					TaskWrapper.Log(
						this,
						ex.Message,
						TaskWrapper.TaskWrapperLogType.Exception,
						ex);

					this.Exceptions.Add(ex);
					this.IsRunning = false;
					this.LastRunEnded = DateTime.Now;
				}

				// Queue up the next run.
				this.QueueNextRun(null);
			});

			TaskWrapper.Log(
				this,
				"Starting background thread.",
				TaskWrapper.TaskWrapperLogType.Info);

			this.Thread.Start();
			return;
		}

		try {
			this.LastRunStarted = DateTime.Now;
			this.IsRunning = true;

			TaskWrapper.Log(
				this,
				"Running main function.",
				TaskWrapper.TaskWrapperLogType.Info);

			this.Action.Invoke(this);

			this.IsRunning = false;
			this.LastRunEnded = DateTime.Now;

			var span = this.LastRunEnded - this.LastRunStarted;

			TaskWrapper.Log(
				this,
				string.Format(
					"Run completed successfully. Took {0} seconds.",
					span.TotalSeconds),
				TaskWrapper.TaskWrapperLogType.Info);
		}
		catch (Exception ex) {
			TaskWrapper.Log(
				this,
				ex.Message,
				TaskWrapper.TaskWrapperLogType.Exception,
				ex);

			this.Exceptions.Add(ex);
			this.IsRunning = false;
			this.LastRunEnded = DateTime.Now;
		}

		// Queue up the next run.
		this.QueueNextRun(null);
	}

	/// <summary>
	/// Stop the running thread.
	/// </summary>
	public void Stop() {
		this.Enabled = false;

		TaskWrapper.Log(
			this,
			"Stopping.",
			TaskWrapper.TaskWrapperLogType.Info);

		if (this.Thread != null)
			this.Thread.Abort();
	}

	#endregion
}