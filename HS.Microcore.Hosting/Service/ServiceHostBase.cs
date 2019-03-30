#region Copyright 
// Copyright 2017 HS Inc.  All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License.  
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.
#endregion

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HS.Microcore.SharedLogic;


[assembly: InternalsVisibleTo("LINQPadQuery")]

namespace HS.Microcore.Hosting.Service
{
    public abstract class ServiceHostBase : IDisposable
    {
        private bool disposed;

        public ServiceArguments Arguments { get; private set; }

        private ManualResetEvent StopEvent { get; }
        private TaskCompletionSource<object> ServiceStartedEvent { get; set; }
        private TaskCompletionSource<StopResult> ServiceGracefullyStopped { get; set; }
        private Process MonitoredShutdownProcess { get; set; }
        private readonly string _serviceName;
        protected CrashHandler CrashHandler { get; set; }

        /// <summary>
        /// The name of the service. This will be globally accessible from <see cref="CurrentApplicationInfo.Name"/>.
        /// </summary>
        protected virtual string ServiceName => _serviceName;

        /// <summary>
        /// Version of underlying infrastructure framework. This will be globally accessible from <see cref="CurrentApplicationInfo.InfraVersion"/>.
        /// </summary>
        protected virtual Version InfraVersion => null;


        protected ServiceHostBase()
        {
            if (IntPtr.Size != 8)
                throw new Exception("You must run in 64-bit mode. Please make sure you unchecked the 'Prefer 32-bit' checkbox from the build section of the project properties.");


            StopEvent = new ManualResetEvent(true);
            ServiceStartedEvent = new TaskCompletionSource<object>();
            ServiceGracefullyStopped = new TaskCompletionSource<StopResult>();
            ServiceGracefullyStopped.SetResult(StopResult.None);

            _serviceName = GetType().Name;

         
            if (_serviceName.EndsWith("Host") && _serviceName.Length > 4)
                _serviceName = _serviceName.Substring(0, _serviceName.Length - 4);
        }

        /// <summary>
        /// Start the service, autodetecting between Windows service and command line. Always blocks until service is stopped.
        /// </summary>
        public void Run(ServiceArguments argumentsOverride = null)
        {
            ServiceGracefullyStopped = new TaskCompletionSource<StopResult>();
            Arguments = argumentsOverride ?? new ServiceArguments(Environment.GetCommandLineArgs().Skip(1).ToArray());
            CurrentApplicationInfo.Init(ServiceName, Arguments.InstanceName, InfraVersion);

            if (Arguments.ShutdownWhenPidExits != null)
            {
                try
                {
                    MonitoredShutdownProcess = Process.GetProcessById(Arguments.ShutdownWhenPidExits.Value);
                }
                catch (ArgumentException e)
                {
                    Console.WriteLine($"Service cannot start because monitored PID {Arguments.ShutdownWhenPidExits} is not running. Exception: {e}");
                    Environment.ExitCode = 1;
                    ServiceGracefullyStopped.SetResult(StopResult.None);
                    return;
                }

                Console.WriteLine($"Will perform graceful shutdown when PID {Arguments.ShutdownWhenPidExits} exits.");
                MonitoredShutdownProcess.Exited += (s, a) =>
                {
                    Console.WriteLine($"PID {Arguments.ShutdownWhenPidExits} has exited, shutting down...");
                    Stop();
                };

                MonitoredShutdownProcess.EnableRaisingEvents = true;
            }

            OnStart();
            if (Arguments.ServiceStartupMode == ServiceStartupMode.CommandLineInteractive)
            {
                Thread.Sleep(10); // Allow any startup log messages to flush to Console.

                Console.Title = ServiceName;

                if (Arguments.ConsoleOutputMode == ConsoleOutputMode.Color)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("Service initialized in interactive mode (command line). Press ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                    Console.Write("[Alt+S]");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.WriteLine(" to stop the service gracefully.");
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
                else
                {
                    Console.WriteLine("Service initialized in interactive mode (command line). Press [Alt+S] to stop the service gracefully.");
                }

                Task.Factory.StartNew(() =>
                {
                    while (true)
                    {
                        var key = Console.ReadKey(true);

                        if (key.Key == ConsoleKey.S && key.Modifiers == ConsoleModifiers.Alt)
                        {
                            Stop();
                            break;
                        }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            else
            {
                Console.WriteLine("Service initialized in non-interactive mode (command line). Waiting for stop request...");
            }

            StopEvent.Reset();
            ServiceStartedEvent.SetResult(null);
            StopEvent.WaitOne();

            Console.WriteLine("   ***   Shutting down...   ***   ");

            var maxShutdownTime = TimeSpan.FromSeconds((Arguments.OnStopWaitTimeSec ?? 0) + (Arguments.ServiceDrainTimeSec ?? 0));
            bool isServiceGracefullyStopped = Task.Run(() => OnStop()).Wait(maxShutdownTime);

            if (isServiceGracefullyStopped == false)
                Console.WriteLine($"   ***  Service failed to stop gracefully in the allotted time ({maxShutdownTime}), continuing with forced shutdown.   ***   ");

            ServiceStartedEvent = new TaskCompletionSource<object>();

            ServiceGracefullyStopped.SetResult(isServiceGracefullyStopped ? StopResult.Graceful : StopResult.Force);
            MonitoredShutdownProcess?.Dispose();

            if (Arguments.ServiceStartupMode == ServiceStartupMode.CommandLineInteractive)
            {
                if (Arguments.ConsoleOutputMode == ConsoleOutputMode.Color)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine("   ***   Shutdown complete. Press any key to exit.   ***   ");
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
                else
                {
                    Console.WriteLine("   ***   Shutdown complete. Press any key to exit.   ***   ");
                }

                Console.ReadKey(true);
            }
        }

        /// <summary>
        /// Waits for the service to finish starting. Mainly used from tests.
        /// </summary>
        public Task WaitForServiceStartedAsync()
        {
            return ServiceStartedEvent.Task;
        }

        public Task<StopResult> WaitForServiceGracefullyStoppedAsync()
        {
            return ServiceGracefullyStopped.Task;
        }


        /// <summary>
        /// Signals the service to stop.
        /// </summary>
        public void Stop()
        {
            if (StopEvent.WaitOne(0))
                throw new InvalidOperationException("Service is already stopped, or is running in an unsupported mode.");

            StopEvent.Set();
        }

        protected virtual void OnCrash()
        {
            Stop();
            WaitForServiceGracefullyStoppedAsync().Wait(5000);
            Dispose();
        }
        

        protected abstract void OnStart();
        protected abstract void OnStop();


        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            SafeDispose(StopEvent);
            SafeDispose(MonitoredShutdownProcess);

            disposed = true;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void SafeDispose(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
            }
        }
    }
    public enum StopResult { None, Graceful, Force}

}
