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

	public sealed partial class AmiClient : IDisposable, IObservable<AmiMessage>
	{
		private void Dispatch(AmiMessage message)
		{
			foreach(var observer in this.observers.Keys)
			{
				observer.OnNext(message);
			}
		}

		private void Dispatch(Exception exception)
		{
			foreach(var observer in this.observers.Keys)
			{
				observer.OnError(exception);

				this.Unsubscribe(observer);
			}
		}

		public IDisposable Subscribe(IObserver<AmiMessage> observer)
		{
			if(observer == null)
			{
				throw new ArgumentNullException(nameof(observer));
			}

			return this.observers.GetOrAdd(observer, _ => new Subscription(this, observer));
		}

		public void Unsubscribe(IObserver<AmiMessage> observer)
		{
			if(observer == null)
			{
				throw new ArgumentNullException(nameof(observer));
			}

			this.observers.TryRemove(observer, out _);
		}

		public void Dispose()
		{
			foreach(var observer in this.observers.Keys)
			{
				observer.OnCompleted();

				this.Unsubscribe(observer);
			}

			this.processing = false;
		}

		private sealed class Subscription : IDisposable
		{
			private readonly AmiClient client;

			private readonly IObserver<AmiMessage> observer;

			internal Subscription(AmiClient client, IObserver<AmiMessage> observer)
			{
				this.client = client;
				this.observer = observer;
			}

			public void Dispose()
			{
				this.client.Unsubscribe(this.observer);
			}
		}
	}
}
