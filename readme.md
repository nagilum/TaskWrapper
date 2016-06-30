# TaskWrapper

A C# task scheduler with interval or specific run times.

```csharp
TaskWrapper.Register(
	"Test",
	RunMe,
	new TimeSpan(0, 0, 10));
```

This will run the function `RunMe` at register and every 10 seconds.

```csharp
TaskWrapper.Register(
	"Test",
	RunMe,
	null,
	new List<DateTime> {
		new DateTime(0, 0, 0, 14, 0, 0),
		new DateTime(0, 0, 0, 18, 0, 0)
	});
```

This will run the function `RunMe` at register, and at 14:00, and at 18:00.

```csharp
TaskWrapper.Register(
	"Test",
	RunMe,
	new TimeSpan(0, 0, 10),
	runInstantly: false,
	runThreaded: true);
```

This will run the function `RunMe` only every 10 seconds and not at register. And it will also run it in a separate thread.

```csharp
TaskWrapper.Register(
	"Test",
	RunMe,
	new TimeSpan(0, 0, 10),
	runInstantly: false,
	runThreaded: true,
	verifyAtRuntime: (entry) => {
		if (DateTime.Now.DayOfWeek == DayOfWeek.Friday)
			return new TimeSpan(0);

		return new TimeSpan(1, 0, 0);
	});
```

This will run the function `RunMe` every 10 seconds, not at register, in a separate thread, and each time before it runs, the verify function will be called. This allows you to override the run and postpone it. This verify function postpones the run if the day-of-week is not friday.