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

        public AmiClient()
        {
            this.observers = new ConcurrentDictionary<IObserver<AmiMessage>, Subscription>(
                Environment.ProcessorCount,
                65536);

            this.inFlight = new ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>>(
                Environment.ProcessorCount,
                16384,
                StringComparer.OrdinalIgnoreCase);
        }

        private Stream stream;

        [Obsolete("use Start() method")]
        public AmiClient(Stream stream) : this()
        {
            this.Stream = stream;
        }

        [Obsolete("use Start() method")]
        public Stream Stream
        {
            get => this.stream;
            set
            {
                var task = this.Start(value);
                task.Wait();
            }
        }

        public async Task<Task> Start(Stream stream)
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

            try
            {
                await this.lineObserver.ToObservable().Take(1);
            }
            catch (Exception ex)
            {
                throw new AmiException("protocol handshake failed (is this an Asterisk server?)", ex);
            }

            return Task.Factory.StartNew(this.WorkerMain, TaskCreationOptions.LongRunning);
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

        private Byte[] readBuffer = new Byte[0];

        private IEnumerable<Byte[]> lineObserver
        {
            get
            {
                while(true)
                {
                    while(true)
                    {
                        var crlfPos = this.readBuffer.Find(AmiMessage.TerminatorBytes, 0, this.readBuffer.Length);
                        if(crlfPos < 0)
                        {
                            break;
                        }

                        var line = this.readBuffer.Slice(0, crlfPos + AmiMessage.TerminatorBytes.Length);
                        this.readBuffer = this.readBuffer.Slice(crlfPos + AmiMessage.TerminatorBytes.Length);

                        yield return line;
                    }

                    try
                    {
                        var bytes = new Byte[4096];

                        var nrBytes = this.stream.Read(bytes, 0, bytes.Length);
                        if(nrBytes == 0)
                        {
                            yield break; // EOF
                        }

                        this.readBuffer = this.readBuffer.Append(bytes.Slice(0, nrBytes));

                        this.DataReceived?.Invoke(this, new DataEventArgs(bytes.Slice(0, nrBytes)));
                    }
                    catch(SocketException ex)
                    {
                        if(ex.SocketErrorCode != SocketError.Interrupted && ex.SocketErrorCode != SocketError.TimedOut)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        private async Task WorkerMain()
        {
            var lineObserver = this.lineObserver.ToObservable();

            try
            {
                while(this.stream != null)
                {
                    var payload = new Byte[0];

                    await lineObserver
                          .TakeUntil(line => line.SequenceEqual(AmiMessage.TerminatorBytes))
                          .Do(line => payload = payload.Append(line))
                          .DefaultIfEmpty();

                    if(payload.Length == 0)
                    {
                        break;
                    }

                    var message = AmiMessage.FromBytes(payload);

                    if(message.Fields.FirstOrDefault().Key == "Response" &&
                       this.inFlight.TryGetValue(message["ActionID"], out var tcs))
                    {
                        tcs.SetResult(message);
                    }
                    else
                    {
                        this.Dispatch(message);
                    }
                }
            }
            catch(Exception ex)
            {
                this.Stop(ex);
                return;
            }

            this.Stop();
        }
    }
}
