/* Copyright © 2019 Alex Forster. All rights reserved.
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
	using System.IO;
	using System.Text;
	using System.Collections.Generic;
	using System.Collections.Concurrent;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Linq;
	using System.Reactive.Linq;
	using System.Security.Cryptography;

	using ByteArrayExtensions;

	public sealed partial class AmiClient
	{
		private readonly Stream stream;

		public AmiClient(Stream stream)
		{
			this.stream = stream ?? throw new ArgumentNullException(nameof(stream));

			Debug.Assert(stream.CanRead);
			Debug.Assert(stream.CanWrite);

			var lineObserver = this.ReadLines().ToObservable();
			var line = lineObserver.Take(1).Wait();

			if(String.IsNullOrEmpty(line))
			{
				throw new Exception($"this does not appear to be an Asterisk server ({line})");
			}

			this.DataReceived?.Invoke(this, new DataEventArgs(line + "\x0d\x0a"));

			if(!line.StartsWith("Asterisk Call Manager", StringComparison.OrdinalIgnoreCase))
			{
				throw new Exception($"this does not appear to be an Asterisk server ({line})");
			}

			Task.Run(this.WorkerMain);
		}

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

		private readonly ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>> inFlight =
			new ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>>(StringComparer.OrdinalIgnoreCase);

		public async Task<AmiMessage> Publish(AmiMessage action)
		{
			try
			{
				var tcs = new TaskCompletionSource<AmiMessage>(TaskCreationOptions.AttachedToParent);

				Debug.Assert(this.inFlight.TryAdd(action["ActionID"], tcs));

				var buffer = action.ToBytes();

				await this.stream.WriteAsync(buffer, 0, buffer.Length);

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

		private Byte[] readBuffer = new Byte[0];

		private IEnumerable<String> ReadLines()
		{
			var needle = new Byte[] { 0x0d, 0x0a };

			while(true)
			{
				if(!this.readBuffer.Any())
				{
					var bytes = new Byte[4096];
					var nrBytes = this.stream.Read(bytes, 0, bytes.Length);
					if(nrBytes == 0)
					{
						break;
					}
					this.readBuffer = this.readBuffer.Append(bytes.Slice(0, nrBytes));
				}
				while(true)
				{
					var crlfPos = this.readBuffer.Find(needle, 0, this.readBuffer.Length);
					if(crlfPos == -1)
					{
						break;
					}
					var line = this.readBuffer.Slice(0, crlfPos);
					this.readBuffer = this.readBuffer.Slice(crlfPos + needle.Length);
					yield return Encoding.UTF8.GetString(line);
				}
			}
		}

		private Boolean processing = true;

		private async Task WorkerMain()
		{
			try
			{
				var lineObserver = this.ReadLines().ToObservable();

				while(this.processing)
				{
					var message = new AmiMessage();

					await lineObserver
					     .TakeWhile(line => line != String.Empty)
					     .Do(line =>
					      {
						      var kv = line.Split(new[] { ':' }, 2);
						      Debug.Assert(kv.Length == 2);
						      message.Add(kv[0], kv[1]);
					      });

					this.DataReceived?.Invoke(this, new DataEventArgs(message.ToString()));

					if(message["Response"] != null && this.inFlight.TryGetValue(message["ActionID"], out var tcs))
					{
						Debug.Assert(tcs.TrySetResult(message));
					}
					else
					{
						this.Dispatch(message);
					}
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
