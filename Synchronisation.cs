using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace FolderSync
{
    public class AsyncLockQueueDictionary
    {
        private readonly object DictionaryAccessMutex = new object();
        private readonly Dictionary<string, AsyncLockWithCount> LockQueueDictionary = new Dictionary<string, AsyncLockWithCount>();

        public sealed class AsyncLockWithCount
        {
            public readonly AsyncLock LockEntry;
            public int WaiterCount;

            public AsyncLockWithCount()
            {
                this.LockEntry = new AsyncLock();
                this.WaiterCount = 1;
            }
        }

        public sealed class LockDictReleaser : IDisposable
        {
            public readonly string Name;
            public readonly AsyncLockWithCount LockEntry;
            public readonly IDisposable LockHandle;
            public readonly AsyncLockQueueDictionary AsyncLockQueueDictionary;

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
            {
                if (lockEntry.WaiterCount == 0)   //NB!
                {
                    LockQueueDictionary.Remove(name);
                }
            }
        }

        public async Task<LockDictReleaser> LockAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            AsyncLockWithCount lockEntry;
            lock (DictionaryAccessMutex)
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

            var lockHandle = await lockEntry.LockEntry.LockAsync(cancellationToken);
            return new LockDictReleaser(name, lockEntry, lockHandle, this);
        }
    }
}
