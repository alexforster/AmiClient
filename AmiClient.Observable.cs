// Copyright © 2017 Alex Forster. All rights reserved.

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

    public sealed partial class AmiClient : IDisposable, IObservable<AmiMessage>
    {
        private readonly ConcurrentDictionary<IObserver<AmiMessage>, Subscription> observers =
            new ConcurrentDictionary<IObserver<AmiMessage>, Subscription>();

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

            var subscription = new Subscription(this, observer);

            this.observers.TryAdd(observer, subscription);

            return subscription;
        }

        public void Unsubscribe(IObserver<AmiMessage> observer)
        {
            if(observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            this.observers.TryRemove(observer, out var subscription);
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

        public sealed class Subscription : IDisposable
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
