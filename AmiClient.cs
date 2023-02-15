/* Copyright © Alex Forster. All rights reserved.
 * https://github.com/alexforster/AmiClient/
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Ami
{
    using System;
    using System.IO;
    using System.Text;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Security.Cryptography;

    using ByteArrayExtensions;

    public sealed partial class AmiClient : IDisposable
    {
        private readonly ConcurrentDictionary<IObserver<AmiMessage>, Subscription> observers;

        private readonly ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>> inFlight;

        private readonly ConcurrentQueue<string?> eventQueue;

        private readonly CancellationTokenSource cancellationTokenSource;

        public AmiClient()
        {
            this.observers = new ConcurrentDictionary<IObserver<AmiMessage>, Subscription>(
                Environment.ProcessorCount,
                65536);

            this.inFlight = new ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>>(
                Environment.ProcessorCount,
                16384,
                StringComparer.OrdinalIgnoreCase);

            this.eventQueue = new ConcurrentQueue<string>();

            this.cancellationTokenSource = new CancellationTokenSource();
        }

        private NetworkStream stream;

        public Task Start(NetworkStream stream)
        {
            if(this.stream != null)
            {
                throw new AmiException("client has already been started");
            }

            if(!stream.CanRead)
            {
                throw new ArgumentException("stream does not support reading", nameof(stream));
            }

            if(!stream.CanWrite)
            {
                throw new ArgumentException("stream does not support writing", nameof(stream));
            }

            this.stream = stream;

            Task.Factory.StartNew(EventReader, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(EventWorker, TaskCreationOptions.LongRunning);
            
            return Task.CompletedTask;
        }

        public void Stop()
        {
            this.Stop(null);
        }

        private void Stop(Exception ex)
        {
            if(this.stream == null)
            {
                return;
            }

            foreach(var observer in this.observers.Keys)
            {
                if(ex != null)
                {
                    observer.OnError(ex);
                }
                else
                {
                    observer.OnCompleted();
                }

                this.observers.TryRemove(observer, out _);
            }

            foreach(var kvp in this.inFlight)
            {
                if(ex != null)
                {
                    kvp.Value.TrySetException(ex);
                }
                else
                {
                    kvp.Value.TrySetCanceled();
                }

                this.inFlight.TryRemove(kvp.Key, out _);
            }

            this.stream = null;
            cancellationTokenSource.Cancel();

            this.Stopped?.Invoke(this, new LifecycleEventArgs(ex));
        }

        public void Dispose()
        {
            this.Stop();
        }

        public async Task<AmiMessage> Publish(AmiMessage action)
        {
            if(this.stream == null)
            {
                throw new InvalidOperationException("client is not started");
            }

            var tcs = new TaskCompletionSource<AmiMessage>(TaskCreationOptions.AttachedToParent);

            if(!this.inFlight.TryAdd(action["ActionID"], tcs))
            {
                throw new AmiException("a message with the same ActionID is already in flight");
            }

            try
            {
                var buffer = action.ToBytes();

                lock(this.stream)
                {
                    this.stream.Write(buffer, 0, buffer.Length);
                }

                this.DataSent?.Invoke(this, new DataEventArgs(buffer));

                return await tcs.Task;
            }
            catch(Exception ex)
            {
                this.Stop(ex);
                throw;
            }
            finally
            {
                this.inFlight.TryRemove(action["ActionID"], out _);
            }
        }

        private async Task EventReader()
        {
            var bufferStream = new BufferedStream(stream);
            var reader = new StreamReader(bufferStream);
        
            while (!cancellationTokenSource.Token.IsCancellationRequested) {
                var line = await reader.ReadLineAsync();
                eventQueue.Enqueue(line);
            }
        }

        private Task EventWorker() {
            while (!cancellationTokenSource.Token.IsCancellationRequested) {
                var messageBuilder = new StringBuilder();
        
                while (true) {
                    if (!eventQueue.TryDequeue(out var line)) {
                        continue;
                    }
        
                    if (string.IsNullOrEmpty(line)) {
                        messageBuilder.Append("\r\n");
                        break;
                    }

                    if (line.Contains("Asterisk Call Manager")) {
                        continue;
                    }
                    
                    messageBuilder.AppendLine(line);
                }
        
                var message = messageBuilder.ToString();
        
                if (string.IsNullOrEmpty(message)) {
                    continue;
                }
 
                var parsedMessage = AmiMessage.FromString(message);
                
                if(parsedMessage.Fields.FirstOrDefault().Key == "Response" && this.inFlight.TryGetValue(parsedMessage["ActionID"], out var tcs))
                {
                    tcs.SetResult(parsedMessage);
                }
                
                this.DataReceived?.Invoke(this, new DataEventArgs(parsedMessage));
            }
        
            return Task.CompletedTask;
        }
    }
}