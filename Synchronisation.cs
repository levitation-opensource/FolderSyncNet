//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace FolderSync
{
    public class AsyncLockQueueDictionary<KeyT>
        where KeyT : IComparable<KeyT>, IEquatable<KeyT>
    {
        private static readonly bool IsStringDictionary = typeof(KeyT) == typeof(string);

        private readonly object DictionaryAccessMutex = new object();
        //private readonly SemaphoreSlim DictionaryAccessMutex = new SemaphoreSlim(1, 1);
        private readonly Dictionary<KeyT, AsyncLockWithWaiterCount> LockQueueDictionary = new Dictionary<KeyT, AsyncLockWithWaiterCount>();

        public sealed class AsyncLockWithWaiterCount
        {
            public readonly AsyncLock LockEntry;
#pragma warning disable S1104   //Warning	S1104	Make this field 'private' and encapsulate it in a 'public' property.
            public int WaiterCount;
#pragma warning restore S1104

            public AsyncLockWithWaiterCount()
            {
                this.LockEntry = new AsyncLock();
                this.WaiterCount = 1;
            }
        }

        public sealed class LockDictReleaser : IDisposable  //TODO: implement IAsyncDisposable in .NET 5.0
        {
            private readonly KeyT Name;
            private readonly AsyncLockWithWaiterCount LockEntry;
#if !NOASYNC
            private readonly IDisposable LockHandle;
#endif
            private readonly AsyncLockQueueDictionary<KeyT> AsyncLockQueueDictionary;

#if NOASYNC
            internal LockDictReleaser(KeyT name, AsyncLockWithWaiterCount lockEntry, AsyncLockQueueDictionary<KeyT> asyncLockQueueDictionary)
#else 
            internal LockDictReleaser(KeyT name, AsyncLockWithWaiterCount lockEntry, IDisposable lockHandle, AsyncLockQueueDictionary<KeyT> asyncLockQueueDictionary)
#endif
            {
                this.Name = name;
                this.LockEntry = lockEntry;
#if !NOASYNC
                this.LockHandle = lockHandle;
#endif
                this.AsyncLockQueueDictionary = asyncLockQueueDictionary;
            }

            public void Dispose()
            {
#if NOASYNC
                this.AsyncLockQueueDictionary.ReleaseLock(this.Name, this.LockEntry);
#else
                this.AsyncLockQueueDictionary.ReleaseLock(this.Name, this.LockEntry, this.LockHandle);
#endif
            }
        }

#if NOASYNC
        private void ReleaseLock(KeyT name, AsyncLockWithWaiterCount lockEntry)
#else
        private void ReleaseLock(KeyT name, AsyncLockWithWaiterCount lockEntry, IDisposable lockHandle)
#endif
        {
#if NOASYNC
            Monitor.Exit(lockEntry.LockEntry);
#else
            lockHandle.Dispose();
#endif

            lock (DictionaryAccessMutex)
            //DictionaryAccessMutex.Wait();
            //try
            {
                lockEntry.WaiterCount--;
                Debug.Assert(lockEntry.WaiterCount >= 0);

                if (lockEntry.WaiterCount == 0)   //NB!
                {
                    LockQueueDictionary.Remove(name);
                }
            }
            //finally
            //{
            //    DictionaryAccessMutex.Release();
            //}
        }

        public async Task<LockDictReleaser> LockAsync(KeyT name, CancellationToken cancellationToken = default(CancellationToken))
        {
            AsyncLockWithWaiterCount lockEntry;
#pragma warning disable PH_S023     //Message	PH_S023	Using the blocking synchronization mechanics of a monitor inside an async method is discouraged; use SemaphoreSlim instead.
            lock (DictionaryAccessMutex)
#pragma warning restore PH_S023
            //await DictionaryAccessMutex.WaitAsync(cancellationToken);
            //try
            {
                if (!LockQueueDictionary.TryGetValue(name, out lockEntry))
                {
                    lockEntry = new AsyncLockWithWaiterCount();
                    LockQueueDictionary.Add(name, lockEntry);
                }
                else
                {
                    lockEntry.WaiterCount++;  //NB! must be done inside the lock and BEFORE waiting for the lock
                }
            }
            //finally
            //{
            //    DictionaryAccessMutex.Release();
            //}

#if NOASYNC
#pragma warning disable PH_P006  //warning PH_P006 Favor the use of the lock-statement instead of the use of Monitor.Enter when no timeouts are needed.
            Monitor.Enter(lockEntry.LockEntry);
#pragma warning restore PH_P006
            return new LockDictReleaser(name, lockEntry, this);
#else
            var lockHandle = await lockEntry.LockEntry.LockAsync(cancellationToken);
            return new LockDictReleaser(name, lockEntry, lockHandle, this);
#endif
        }

        public sealed class MultiLockDictReleaser : IDisposable  //TODO: implement IAsyncDisposable in .NET 5.0
        {
            private readonly LockDictReleaser[] Releasers;

            public MultiLockDictReleaser(params LockDictReleaser[] releasers)
            {
                this.Releasers = releasers;
            }

            public void Dispose()
            {
                foreach (var releaser in Releasers)
                {
                    if (releaser != null)   //NB!
                        releaser.Dispose();
                }
            }
        }

        public async Task<MultiLockDictReleaser> LockAsync(KeyT name1, KeyT name2, CancellationToken cancellationToken = default(CancellationToken))
        {
            var names = new List<KeyT>()
                            {
                                name1,
                                name2
                            };

            //NB! in order to avoid deadlocks, always take the locks in deterministic order
            if (IsStringDictionary)
                names.Sort(StringComparer.InvariantCultureIgnoreCase as IComparer<KeyT>);
            else
                names.Sort();

            var releaser1 = await this.LockAsync(names[0], cancellationToken);
            var releaser2 = !name1.Equals(name2) ? await this.LockAsync(names[1], cancellationToken) : null;

            return new MultiLockDictReleaser(releaser1, releaser2);
        }
    }
}
