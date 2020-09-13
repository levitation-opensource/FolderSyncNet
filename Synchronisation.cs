#define ASYNC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace FolderSync
{
    public class AsyncLockQueueDictionary
    {
        private readonly object DictionaryAccessMutex = new object();
        //private readonly SemaphoreSlim DictionaryAccessMutex = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, AsyncLockWithCount> LockQueueDictionary = new Dictionary<string, AsyncLockWithCount>();

        public sealed class AsyncLockWithCount
        {
            public readonly AsyncLock LockEntry;
#pragma warning disable S1104   //Warning	S1104	Make this field 'private' and encapsulate it in a 'public' property.
            public int WaiterCount;
#pragma warning restore S1104

            public AsyncLockWithCount()
            {
                this.LockEntry = new AsyncLock();
                this.WaiterCount = 1;
            }
        }

        public sealed class LockDictReleaser : IDisposable  //TODO: implement IAsyncDisposable in .NET 5.0
        {
            private readonly string Name;
            private readonly AsyncLockWithCount LockEntry;
            private readonly IDisposable LockHandle;
            private readonly AsyncLockQueueDictionary AsyncLockQueueDictionary;

            internal LockDictReleaser(string name, AsyncLockWithCount lockEntry, IDisposable lockHandle, AsyncLockQueueDictionary asyncLockQueueDictionary)
            {
                this.Name = name;
                this.LockEntry = lockEntry;
                this.LockHandle = lockHandle;
                this.AsyncLockQueueDictionary = asyncLockQueueDictionary;
            }

            public void Dispose()
            {
                this.AsyncLockQueueDictionary.ReleaseLock(this.Name, this.LockEntry, this.LockHandle);
            }
        }

        private void ReleaseLock(string name, AsyncLockWithCount lockEntry, IDisposable lockHandle)
        {
            lockHandle.Dispose();

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

        public async Task<LockDictReleaser> LockAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            AsyncLockWithCount lockEntry;
#pragma warning disable PH_S023     //Message	PH_S023	Using the blocking synchronization mechanics of a monitor inside an async method is discouraged; use SemaphoreSlim instead.
            lock (DictionaryAccessMutex)
#pragma warning restore PH_S023
            //await DictionaryAccessMutex.WaitAsync(cancellationToken);
            //try
            {
                if (!LockQueueDictionary.TryGetValue(name, out lockEntry))
                {
                    lockEntry = new AsyncLockWithCount();                    
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

            var lockHandle = await lockEntry.LockEntry.LockAsync(cancellationToken);
            return new LockDictReleaser(name, lockEntry, lockHandle, this);
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

        public async Task<MultiLockDictReleaser> LockAsync(string name1, string name2, CancellationToken cancellationToken = default(CancellationToken))
        {
            var names = new List<string>()
                            {
                                name1,
                                name2
                            };

            //NB! in order to avoid deadlocks, always take the locks in deterministic order
            names.Sort(StringComparer.InvariantCultureIgnoreCase);

            var releaser1 = await this.LockAsync(names[0], cancellationToken);
            var releaser2 = name1 != name2 ? await this.LockAsync(names[1], cancellationToken) : null;

            return new MultiLockDictReleaser(releaser1, releaser2);
        }
    }
}
