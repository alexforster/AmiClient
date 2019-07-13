/* Copyright © 2019 Alex Forster. All rights reserved.
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
	using System.Threading;
	using System.Threading.Tasks;
	using System.Linq;
	using System.Reactive.Linq;
	using System.Security.Cryptography;

	using ByteArrayExtensions;

	public sealed partial class AmiClient
	{
		private Stream stream;

		public Stream Stream
		{
			get => this.stream;

			set
			{
				if(this.stream != null)
				{
					throw new ArgumentException("\"Stream\" property has already been set", nameof(value));
				}

				if(!value.CanRead)
				{
					throw new ArgumentException("stream does not support reading", nameof(value));
				}

				if(!value.CanWrite)
				{
					throw new ArgumentException("stream does not support writing", nameof(value));
				}

				this.stream = value;

				Task.Factory.StartNew(this.WorkerMain, TaskCreationOptions.LongRunning);

				// wait up to one second for the worker thread to consume a protocol banner from the server

				var oneSecondFromNow = DateTimeOffset.Now.AddSeconds(1);

				while(!this.processing && DateTimeOffset.Now < oneSecondFromNow)
				{
					Thread.Yield();
				}

				if(!this.processing)
				{
					throw new AmiException("could not exchange the AMI protocol banner");
				}
			}
		}

		private readonly ConcurrentDictionary<IObserver<AmiMessage>, Subscription> observers;

		private readonly ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>> inFlight;

		public AmiClient()
		{
			this.observers = new ConcurrentDictionary<IObserver<AmiMessage>, Subscription>(
				Environment.ProcessorCount,
				1024 * 64);

			this.inFlight = new ConcurrentDictionary<String, TaskCompletionSource<AmiMessage>>(
				Environment.ProcessorCount,
				1024 * 16);
		}

		public AmiClient(Stream stream) : this()
		{
			this.Stream = stream;
		}

		public async Task<AmiMessage> Publish(AmiMessage action)
		{
			if(this.stream == null)
			{
				throw new InvalidOperationException("\"Stream\" property has not been set");
			}

			try
			{
				var tcs = new TaskCompletionSource<AmiMessage>(TaskCreationOptions.AttachedToParent);

				if(!this.inFlight.TryAdd(action["ActionID"], tcs))
				{
					throw new AmiException("a message with the same ActionID is already in flight");
				}

				var buffer = action.ToBytes();

				lock(this.stream)
				{
					this.stream.Write(buffer, 0, buffer.Length);
				}

				this.DataSent?.Invoke(this, new DataEventArgs(buffer));

				var response = await tcs.Task;

				this.inFlight.TryRemove(response["ActionID"], out _);

				return response;
			}
			catch(Exception ex)
			{
				this.Dispatch(ex);

				this.inFlight.TryRemove(action["ActionID"], out _);

				return null;
			}
		}

		private Byte[] readBuffer = new Byte[0];

		private IEnumerable<Byte[]> LineObserver()
		{
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

				while(this.readBuffer.Any())
				{
					var crlfPos = this.readBuffer.Find(AmiMessage.CrLfBytes);
					if(crlfPos == -1)
					{
						goto CONTINUE;
					}
					var line = this.readBuffer.Slice(0, crlfPos + AmiMessage.CrLfBytes.Length);
					this.readBuffer = this.readBuffer.Slice(crlfPos + AmiMessage.CrLfBytes.Length);
					yield return line;
				}
				CONTINUE: ;
			}
		}

		private Boolean processing;

		private async Task WorkerMain()
		{
			try
			{
				var lineObserver = this.LineObserver().ToObservable();

				var handshake = await lineObserver.Take(1);

				this.DataReceived?.Invoke(this, new DataEventArgs(handshake));

				this.processing = true;

				if(String.IsNullOrEmpty(Encoding.UTF8.GetString(handshake)))
				{
					throw new AmiException("protocol handshake failed (is this an Asterisk server?)");
				}

				while(this.processing)
				{
					var payload = new Byte[0];

					await lineObserver
					     .TakeUntil(line => line.SequenceEqual(AmiMessage.CrLfBytes))
					     .Do(line => payload = payload.Append(line));

					this.DataReceived?.Invoke(this, new DataEventArgs(payload));

					var message = AmiMessage.FromBytes(payload);

					if(message["Response"] != null && this.inFlight.TryGetValue(message["ActionID"], out var tcs))
					{
						tcs.SetResult(message);
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
