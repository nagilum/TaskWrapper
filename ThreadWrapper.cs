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
	/// Queue up an action to run at given intervals.
	/// </summary>
	/// <param name="name">Name of the task to register.</param>
	/// <param name="action">Function to run when the time is right.</param>
	/// <param name="timeSpan">Interval between each run.</param>
	/// <param name="dateTimes">A list of times (the date-part is ignored) when the function is to be runned.</param>
	/// <param name="runInstantly">Run the function instantly.</param>
	/// <param name="runThreaded">Run the function in a separate thread.</param>
	/// <param name="verifyAtRuntime">Function which is called just before the action-function is to be called, allowing you to postpone it.</param>
	public static void Register(string name, Action action, TimeSpan? timeSpan = null, List<DateTime> dateTimes = null, bool runInstantly = true, bool runThreaded = false, Func<TaskWrapperEntry, TimeSpan> verifyAtRuntime = null) {
		if (action == null)
			throw new Exception("Action is required.");

		if (Entries.SingleOrDefault(n => n.Name == name) != null)
			throw new Exception("Task with same name already exists.");

		var entry = new TaskWrapperEntry {
			Name = name,
			RunThreaded = runThreaded,
			Created = DateTime.Now,
			Action = action,
			TimeSpan = timeSpan,
			DateTimes = dateTimes,
			VerifyAtRuntime = verifyAtRuntime
		};

		Entries.Add(entry);

		if (runInstantly)
			entry.Run();
		else
			entry.QueueNextRun(null);
	}
}

public class TaskWrapperEntry {
	/// <summary>
	/// Name of the registered task.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Whether or not to run the function in a separate thread.
	/// </summary>
	public bool RunThreaded { get; set; }
	
	/// <summary>
	/// When the function was registered.
	/// </summary>
	public DateTime Created { get; set; }

	/// <summary>
	/// The last time the run-function was called.
	/// </summary>
	public DateTime LastAttemptedRun { get; set; }

	/// <summary>
	/// The last time the function was attempted to start.
	/// </summary>
	public DateTime LastRunStarted { get; set; }

	/// <summary>
	/// The last time the function was completed.
	/// </summary>
	public DateTime LastRunEnded { get; set; }

	/// <summary>
	/// The function to call when the time is right.
	/// </summary>
	public Action Action { get; set; }

	/// <summary>
	/// Interval between each run.
	/// </summary>
	public TimeSpan? TimeSpan { get; set; }

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
	public Thread Thread { get; set; }

	/// <summary>
	/// Attempt to run the action now.
	/// </summary>
	public void Run() {
		this.LastAttemptedRun = DateTime.Now;

		// Run VerifyAtRuntime
		if (this.VerifyAtRuntime != null) {
			var postpone = this.VerifyAtRuntime.Invoke(this);

			if (postpone.Ticks > 0) {
				this.QueueNextRun(postpone);
				return;
			}
		}

		if (this.RunThreaded) {
			if (this.Thread != null &&
			    this.Thread.IsAlive) {
				return;
			}

			this.Thread = new Thread(() => {
				try {
					this.LastRunStarted = DateTime.Now;
					this.Action.Invoke();
					this.LastRunEnded = DateTime.Now;
				}
				catch {
					// ignore
				}

				// Queue up the next run.
				this.QueueNextRun(null);
			});

			this.Thread.Start();
			return;
		}

		try {
			this.LastRunStarted = DateTime.Now;
			this.Action.Invoke();
			this.LastRunEnded = DateTime.Now;
		}
		catch {
			// ignore
		}

		// Queue up the next run.
		this.QueueNextRun(null);
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
			this.TimeSpan.HasValue)
			timeSpan = this.TimeSpan.Value;

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

		// Wait the alotted time.
		await Task.Delay(timeSpan.Value);

		// Attempt to run the action now.
		Run();
	}
}