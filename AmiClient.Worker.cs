/* Copyright © 2017 Alex Forster. All rights reserved.
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
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.IO;
    using System.Security.Cryptography;

    public sealed partial class AmiClient
    {
        public sealed class DataEventArgs : EventArgs
        {
            public readonly String Data;

            internal DataEventArgs(String data)
            {
                this.Data = data;
            }
        }

        public event EventHandler<DataEventArgs> DataSent;

        public event EventHandler<DataEventArgs> DataReceived;

        private readonly Object writerLock = new Object();

        private readonly ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>> inFlight =
            new ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>>(StringComparer.OrdinalIgnoreCase);

        public async Task<AmiMessage> Publish(AmiMessage action)
        {
            try
            {
                var tcs = new TaskCompletionSource<AmiMessage>(TaskCreationOptions.AttachedToParent);

                Debug.Assert(this.inFlight.TryAdd(action["ActionID"], tcs));

                var buffer = action.ToBytes();

                lock(this.writerLock) this.stream.Write(buffer, 0, buffer.Length);

                this.DataSent?.Invoke(this, new DataEventArgs(action.ToString()));

                var response = await tcs.Task;

                Debug.Assert(this.inFlight.TryRemove(response["ActionID"], out _));

                return response;
            }
            catch(Exception ex)
            {
                this.Dispatch(ex);

                Debug.Assert(this.inFlight.TryRemove(action["ActionID"], out _));

                return null;
            }
        }

        private Boolean processing;

        private void WorkerMain()
        {
            try
            {
                this.processing = true;

                var reader = new StreamReader(this.stream, new UTF8Encoding(false));

                if(this.processing && this.stream != null && this.stream.CanRead)
                {
                    var line = reader.ReadLine();

                    this.DataReceived?.Invoke(this, new DataEventArgs(line + "\r\n"));

                    if(!line.StartsWith("Asterisk Call Manager", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("this does not appear to be an Asterisk server");
                    }
                }

                var lines = new List<String>();

                while(this.processing && this.stream != null && this.stream.CanRead)
                {
                    lines.Add(reader.ReadLine());

                    if(lines.Last() != String.Empty)
                    {
                        continue;
                    }

                    this.DataReceived?.Invoke(this, new DataEventArgs(String.Join("\r\n", lines) + "\r\n"));

                    var message = new AmiMessage();

                    foreach(var line in lines.Where(line => line != String.Empty))
                    {
                        var kv = line.Split(new[] { ':' }, 2);

                        Debug.Assert(kv.Length == 2);

                        message.Add(kv[0], kv[1]);
                    }

                    if(message["Response"] != null && this.inFlight.TryGetValue(message["ActionID"], out var tcs))
                    {
                        Debug.Assert(tcs.TrySetResult(message));
                    }
                    else
                    {
                        this.Dispatch(message);
                    }

                    lines.Clear();
                }
            }
            catch(ThreadAbortException)
            {
                Thread.ResetAbort();
            }
            catch(Exception ex)
            {
                this.Dispatch(ex);
            }
        }
    }
}
