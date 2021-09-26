namespace AvalonLog


open System
open System.Threading
open System.Windows.Threading

/// Threading Utils to setup and access the SynchronizationContext
/// and evaluate any function on UI thread (SyncAvalonLog.doSync(f))
type internal SyncAvalonLog private () = 

    static let mutable errorFileWrittenOnce = false // to not create more than one error file on Desktop per app run

    static let mutable ctx : SynchronizationContext = null  // will be set on first access

    /// To ensure SynchronizationContext is set up.
    /// Optionally writes a log file to the desktop if it fails, since these errors can be really hard to debug
    static let installSynchronizationContext (logErrosOnDesktop) = 
        if SynchronizationContext.Current = null then
            // https://stackoverflow.com/questions/10448987/dispatcher-currentdispatcher-vs-application-current-dispatcher
            DispatcherSynchronizationContext(Windows.Application.Current.Dispatcher) |> SynchronizationContext.SetSynchronizationContext
        ctx <- SynchronizationContext.Current

        if isNull ctx && logErrosOnDesktop && not errorFileWrittenOnce then
            // reporting this to the UI instead would not work since there is no sync context for the UI
            let time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff") // to ensure unique file names
            let filename = sprintf "AvalonLog-SynchronizationContext setup failed-%s.txt" time
            let desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            let file = IO.Path.Combine(desktop,filename)
            try IO.File.WriteAllText(file, "Failed to get DispatcherSynchronizationContext") with _ -> () // file might be open or locked
            errorFileWrittenOnce <- true
            failwith ("See" + file)


    /// The UI SynchronizationContext to switch to inside async workflows
    /// Accessing this member from any thread will set up the sync context first if it is not there yet.
    static member context = 
        if isNull ctx then installSynchronizationContext(true)
        ctx

    /// Runs function on UI thread
    static member doSync(func) = 
        //Windows.Application.Current.Dispatcher.Invoke(func) // would not propagate exceptions ?
        async {
            do! Async.SwitchToContext SyncAvalonLog.context
            func()
            } |> Async.StartImmediate



