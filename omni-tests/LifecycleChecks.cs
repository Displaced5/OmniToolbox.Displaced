using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class LifecycleChecks
{
    public static async Task RunAsync(Assembly moduleAssembly, Assembly common, string dalamudDirectory, Action<bool, string> check)
    {
        var dalamud = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dalamudDirectory, "Dalamud.dll"));
        var moduleType = moduleAssembly.GetType("ClothingPairing.Online.ClothingPairingOnline", true)!;
        var baseType = moduleType.BaseType!;
        var servicesType = moduleAssembly.GetType("ClothingPairing.Online.ModuleServices", true)!;
        var hostServices = common.GetType("OmniToolbox.Host.DalamudServices", true)!;
        var hostProperties = new[] { "PluginInterface", "Framework", "PluginLog" }
            .Select(name => hostServices.GetProperty(name)!).ToArray();
        var previousServices = hostProperties.Select(property => property.GetValue(null)).ToArray();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ClothingPairing-LifecycleChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        using var loop = new FrameworkLoop(dalamud);
        var frameworkEvents = new EventStub();
        var clientEvents = new EventStub();
        var pendingCreation = new ConcurrentQueue<object>();
        var requested = new SemaphoreSlim(0);
        var synchronousCreates = 0;
        var offThreadDataAccess = false;
        var sharedServiceDisposals = 0;
        object? framework = null;
        object Stub(Type type, Func<MethodInfo, object?[], object?>? handler = null) => InterfaceStub.Create(type, (method, args) =>
        {
            if (method.Name == "Dispose") Interlocked.Increment(ref sharedServiceDisposals);
            return handler is null ? Default(method.ReturnType) : handler(method, args);
        });
        object Service(string name, Func<MethodInfo, object?[], object?>? handler = null) =>
            Stub(dalamud.GetType("Dalamud.Plugin.Services." + name, true)!, handler);
        framework = Service("IFramework", (method, args) =>
        {
            if (frameworkEvents.Handle(method, args)) return null;
            if (method.Name == "RunOnFrameworkThread") return loop.Post((Action)args[0]!);
            if (method.Name == "get_IsInFrameworkUpdateThread") return loop.IsOnThread;
            return Default(method.ReturnType);
        });
        var client = Service("IClientState", (method, args) =>
            clientEvents.Handle(method, args) ? null : Default(method.ReturnType));
        var data = new EmptyDataManager(() => offThreadDataAccess |= !loop.IsOnThread);
        object CreateServices()
        {
            var services = Activator.CreateInstance(servicesType, true)!;
            foreach (var property in servicesType.GetProperties())
                property.SetValue(services, property.Name switch
                {
                    "Client" => client,
                    "Data" => data,
                    _ => Stub(property.PropertyType),
                });
            return services;
        }
        var pluginInterfaceType = dalamud.GetType("Dalamud.Plugin.IDalamudPluginInterface", true)!;
        var pluginInterface = Stub(pluginInterfaceType, (method, _) =>
        {
            if (method.Name == "GetPluginConfigDirectory") return temporaryRoot;
            if (method.Name == "Create")
            {
                Interlocked.Increment(ref synchronousCreates);
                throw new InvalidOperationException("Synchronous IoC creation must never run on the framework scheduler.");
            }
            if (method.Name == "CreateAsync")
            {
                var completion = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(method.GetGenericArguments()[0]),
                    TaskCreationOptions.RunContinuationsAsynchronously)!;
                pendingCreation.Enqueue(completion);
                requested.Release();
                return completion.GetType().GetProperty("Task")!.GetValue(completion);
            }
            if (method.Name == "get_InstalledPlugins")
                return Array.CreateInstance(method.ReturnType.GenericTypeArguments[0], 0);
            return Default(method.ReturnType);
        });
        var log = Service("IPluginLog");
        hostProperties[0].SetValue(null, pluginInterface);
        hostProperties[1].SetValue(null, framework);
        hostProperties[2].SetValue(null, log);
        var setEnabled = baseType.GetMethod("SetEnabledAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var instances = new List<object>();
        object NewModule()
        {
            var instance = Activator.CreateInstance(moduleType)!;
            baseType.GetProperty("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, (Func<Action, Task>)loop.Post);
            instances.Add(instance);
            return instance;
        }
        Task Enable(object instance, bool enabled) => loop.PostAsync(() =>
            (Task)setEnabled.Invoke(instance, [enabled, (Func<Action, Task>)loop.Post])!);
        bool Active(object instance) => (bool)baseType.GetProperty("IsEnabled")!.GetValue(instance)!;
        object? Field(object instance, string name) => moduleType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
        async Task CompleteServices()
        {
            if (!await requested.WaitAsync(TimeSpan.FromSeconds(5))) throw new TimeoutException("No asynchronous service request.");
            if (!pendingCreation.TryDequeue(out var completion)) throw new InvalidOperationException("Missing service completion.");
            completion.GetType().GetMethod("SetResult")!.Invoke(completion, [CreateServices()]);
        }
        async Task ExpectFailure(Task operation)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception exception) when (exception is not TimeoutException) { return; }
            throw new InvalidOperationException("Expected initialization failure.");
        }
        try
        {
            var instance = NewModule();
            var startup = Enable(instance, true);
            if (!await requested.WaitAsync(TimeSpan.FromSeconds(5))) throw new TimeoutException("Startup did not begin.");
            await loop.Post(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
            check(!startup.IsCompleted && !Active(instance) && frameworkEvents.Count == 0,
                "actual module startup yields the installed framework scheduler until services are ready");
            requested.Release();
            await CompleteServices();
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            check(Active(instance) && frameworkEvents.Count == 1 && clientEvents.Count == 2 && !offThreadDataAccess,
                "actual Common activation waits for initialization and attaches events on the framework thread");
            var firstAdapter = Field(instance, "adapter");
            var lateTerritoryCallback = clientEvents.Get("TerritoryChanged");
            check(!(bool)Field(instance, "dirty")!, "startup does not arm a memory write or historical restore");
            await Enable(instance, false).WaitAsync(TimeSpan.FromSeconds(5));
            check(!Active(instance) && frameworkEvents.Count == 0 && clientEvents.Count == 0 && Field(instance, "adapter") is null,
                "actual Common stop unsubscribes all events before clearing the activation state");
            await loop.Post(() => lateTerritoryCallback?.DynamicInvoke((uint)1));
            check((int)Field(instance, "actions")!.GetType().GetProperty("Count")!.GetValue(Field(instance, "actions"))! == 0,
                "late event callback cannot enqueue work after module stop");
            var restarted = Enable(instance, true);
            await CompleteServices();
            await restarted.WaitAsync(TimeSpan.FromSeconds(5));
            check(!ReferenceEquals(firstAdapter, Field(instance, "adapter")) && frameworkEvents.Count == 1 && clientEvents.Count == 2,
                "same module instance restarts with one fresh adapter and no duplicate subscriptions");
            await ((IAsyncDisposable)instance).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            check(frameworkEvents.Count == 0 && clientEvents.Count == 0 && !Active(instance),
                "final host disposal releases the running activation without leaving callbacks");

            var failed = NewModule();
            clientEvents.FailNextAdd = "Logout";
            var partialStartup = Enable(failed, true);
            await CompleteServices();
            await ExpectFailure(partialStartup);
            check(frameworkEvents.Count == 0 && clientEvents.Count == 0 && Field(failed, "adapter") is null,
                "partial subscription failure rolls back every event and adapter through the host lifetime");

            var memoryDirectory = Path.Combine(temporaryRoot, "ClothingPairingOnline");
            Directory.CreateDirectory(memoryDirectory);
            var memoryPath = Path.Combine(memoryDirectory, "memories.json");
            File.WriteAllText(memoryPath, "invalid synthetic archive");
            var invalidArchive = NewModule();
            var invalidStartup = Enable(invalidArchive, true);
            await CompleteServices();
            await ExpectFailure(invalidStartup);
            check(frameworkEvents.Count == 0 && clientEvents.Count == 0 && File.ReadAllText(memoryPath) == "invalid synthetic archive",
                "invalid archive fails startup without subscriptions or overwriting the original file");
            File.Delete(memoryPath);

            var cancelled = NewModule();
            var cancelledStartup = Enable(cancelled, true);
            if (!await requested.WaitAsync(TimeSpan.FromSeconds(5))) throw new TimeoutException("Cancelled startup did not begin.");
            var activation = baseType.GetField("activation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cancelled)!;
            var stopping = (Task)activation.GetType().GetMethod("StopAsync")!.Invoke(activation, [(Func<Action, Task>)loop.Post])!;
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            requested.Release();
            await CompleteServices();
            await ExpectFailure(cancelledStartup);
            check(frameworkEvents.Count == 0 && clientEvents.Count == 0 && Field(cancelled, "adapter") is null,
                "cancelling initialization prevents late service completion from attaching events");
            var retry = Enable(cancelled, true);
            await CompleteServices();
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
            await Enable(cancelled, false).WaitAsync(TimeSpan.FromSeconds(5));
            check(frameworkEvents.Count == 0 && clientEvents.Count == 0,
                "cancelled initialization can be enabled and stopped again cleanly");
            check(synchronousCreates == 0 && sharedServiceDisposals == 0,
                "module never synchronously creates services or disposes host-owned services");
        }
        finally
        {
            foreach (var instance in instances)
                await ((IAsyncDisposable)instance).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < hostProperties.Length; index++) hostProperties[index].SetValue(null, previousServices[index]);
            Directory.Delete(temporaryRoot, true);
        }
    }

    private static object? Default(Type type) => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);

    private sealed class EventStub
    {
        private readonly Dictionary<string, Delegate?> events = [];
        public string? FailNextAdd { get; set; }
        public int Count => events.Values.Sum(value => value?.GetInvocationList().Length ?? 0);
        public Delegate? Get(string name) => events.GetValueOrDefault(name);
        public bool Handle(MethodInfo method, object?[] args)
        {
            var adding = method.Name.StartsWith("add_", StringComparison.Ordinal);
            if (!adding && !method.Name.StartsWith("remove_", StringComparison.Ordinal)) return false;
            var name = method.Name[(adding ? 4 : 7)..];
            if (adding && FailNextAdd == name)
            {
                FailNextAdd = null;
                throw new InvalidOperationException("Synthetic subscription failure.");
            }
            events[name] = adding ? Delegate.Combine(Get(name), (Delegate)args[0]!) : Delegate.Remove(Get(name), (Delegate)args[0]!);
            return true;
        }
    }

    private sealed class FrameworkLoop : IDisposable
    {
        private readonly TaskScheduler scheduler;
        private readonly MethodInfo pump;
        private readonly Thread thread;
        private volatile bool stopping;
        public bool IsOnThread => Thread.CurrentThread == thread;
        public FrameworkLoop(Assembly dalamud)
        {
            var type = dalamud.GetType("Dalamud.Utility.ThreadBoundTaskScheduler", true)!;
            scheduler = (TaskScheduler)Activator.CreateInstance(type, new object?[] { null })!;
            pump = type.GetMethod("Run")!;
            thread = new Thread(() =>
            {
                type.GetProperty("BoundThread")!.SetValue(scheduler, Thread.CurrentThread);
                while (!stopping) { pump.Invoke(scheduler, null); Thread.Sleep(1); }
            }) { IsBackground = true };
            thread.Start();
        }
        public Task Post(Action action) => Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);
        public Task PostAsync(Func<Task> action) => Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
        public void Dispose() { stopping = true; thread.Join(1000); }
    }
}

public class InterfaceStub : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> handler = null!;
    public static object Create(Type type, Func<MethodInfo, object?[], object?> handler)
    {
        var proxy = Create(type, typeof(InterfaceStub));
        ((InterfaceStub)proxy).handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args ?? []);
}
