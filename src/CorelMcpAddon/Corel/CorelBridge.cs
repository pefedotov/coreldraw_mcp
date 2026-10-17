using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using corel = Corel.Interop.VGCore;

namespace CorelMcp.Corel
{
    /// <summary>
    /// Owns the handle to the in-process CorelDRAW <see cref="corel.Application"/> and
    /// guarantees that every object-model call is executed on CorelDRAW's UI thread.
    ///
    /// VGCore is an STA COM server living on the UI thread. Calling it from an HTTP
    /// worker thread deadlocks or fails with 0x8001010E, so all access is marshalled via
    /// a hidden WinForms control created on the UI thread at add-on startup.
    /// </summary>
    public static class CorelBridge
    {
        private static readonly object Gate = new object();

        private static corel.Application _app;
        private static int _uiThreadId = -1;
        private static Control _marshaller;

        /// <summary>Timeout for a marshalled call, so a hung UI thread cannot wedge the server forever.</summary>
        public static int InvokeTimeoutMs = 120000;

        public static bool IsInitialized { get { return _app != null; } }

        /// <summary>
        /// Called from the add-on entry point (which CorelDRAW invokes on its UI thread).
        /// </summary>
        public static void Initialize(corel.Application application)
        {
            if (application == null) throw new ArgumentNullException("application");

            lock (Gate)
            {
                _app = application;
                _uiThreadId = Thread.CurrentThread.ManagedThreadId;

                // A hidden control gives us a message-pump target on the UI thread.
                // Control.Invoke then safely marshals work onto that thread.
                _marshaller = new Control();
                _marshaller.CreateControl();
                // Force handle creation so BeginInvoke works before the control is shown.
                var handle = _marshaller.Handle;
                GC.KeepAlive(handle);
            }
            Log.Info("CorelBridge initialised on UI thread " + _uiThreadId + ".");
        }

        /// <summary>
        /// Last-resort attach to an already running CorelDRAW instance (used when the
        /// add-on entry point was not invoked, e.g. when hosting the server standalone).
        /// </summary>
        public static bool TryAttachToRunningInstance()
        {
            if (IsInitialized) return true;
            try
            {
                var progId = "CorelDRAW.Application";
                var type = Type.GetTypeFromProgID(progId);
                if (type == null) return false;
                var app = (corel.Application)Marshal2.GetActiveObject(progId);
                if (app == null) return false;
                Initialize(app);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not attach to a running CorelDRAW instance: " + ex.Message);
                return false;
            }
        }

        public static corel.Application Application
        {
            get
            {
                var app = _app;
                if (app == null)
                    throw new InvalidOperationException(
                        "CorelDRAW application object is not available. The add-on must be loaded by CorelDRAW.");
                return app;
            }
        }

        /// <summary>Run a function on CorelDRAW's UI thread and return its result.</summary>
        public static T Run<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException("func");

            if (_app == null)
                throw new InvalidOperationException("CorelBridge is not initialised.");

            if (Thread.CurrentThread.ManagedThreadId == _uiThreadId)
                return func();

            var marshaller = _marshaller;
            if (marshaller == null)
                throw new InvalidOperationException("CorelBridge marshaller is not available.");

            T result = default(T);
            Exception failure = null;
            var async = marshaller.BeginInvoke((Action)(() =>
            {
                try { result = func(); }
                catch (Exception ex) { failure = ex; }
            }));

            if (!async.AsyncWaitHandle.WaitOne(InvokeTimeoutMs))
                throw new TimeoutException(
                    "CorelDRAW did not process the request within " + InvokeTimeoutMs +
                    " ms. It may be showing a modal dialog.");

            marshaller.EndInvoke(async);

            if (failure != null)
                throw new TargetInvocationException(failure.Message, failure);

            return result;
        }

        public static void Run(Action action)
        {
            Run<object>(() => { action(); return null; });
        }

        /// <summary>Small wrapper so the COM call is easy to spot / replace.</summary>
        private static class Marshal2
        {
            public static object GetActiveObject(string progId)
            {
                return System.Runtime.InteropServices.Marshal.GetActiveObject(progId);
            }
        }
    }
}
