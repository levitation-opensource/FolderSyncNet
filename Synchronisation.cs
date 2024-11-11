//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using AsyncKeyedLock;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FolderSync
{
    public class AsyncLockQueueDictionary<KeyT>
        where KeyT : IComparable<KeyT>, IEquatable<KeyT>
    {
        private readonly AsyncKeyedLocker<KeyT> LockQueueDictionary = new AsyncKeyedLocker<KeyT>();

        private static readonly bool IsStringDictionary = typeof(KeyT) == typeof(string);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<IDisposable> LockAsync(KeyT name)
        {
            return await LockQueueDictionary.LockAsync(name).ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<IDisposable> LockAsync(KeyT name, CancellationToken cancellationToken)
        {
            return await LockQueueDictionary.LockAsync(name, cancellationToken).ConfigureAwait(false);
        }

        public sealed class MultiLockDictReleaser : IDisposable  //TODO: implement IAsyncDisposable in .NET 5.0
        {
            private readonly IDisposable[] Releasers;

            public MultiLockDictReleaser(params IDisposable[] releasers)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
