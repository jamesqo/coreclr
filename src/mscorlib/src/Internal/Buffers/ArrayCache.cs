// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.Contracts;
using System.Threading;

namespace Internal.Buffers
{
    // This class implements a simple, generic array pool. Ideally
    // the System.Buffers.ArrayPool API could be used instead of this
    // (which is more robust), but unfortunately that can't be exposed
    // to System.Private.CoreLib without a circular dependency.

    // It can only hold one array at a time (which is stored in t_array),
    // so it's important to return the array after you're done using it.
    // (NOTE: It may be worth changing the implementation in the future
    // to hold multiple arrays if we run into scenarios where that is benefical)

    // This class is lockless/thread-safe since all the state is held in t_array,
    // which is marked as [ThreadStatic] so every thread gets its own
    // copy of the field. (In addition, it gets another instantiation for
    // every different type parameter.)

    // The methods in here mirror the ones in StringBuilder, with a few extras:
    // Acquire - Rents or allocates a new array with the specified minimum capacity. (not necessarily clear)
    // Release - Returns the array so it can be used by another method (or does nothing if it can't be returned)
    // TryAcquire - Rents an array if available, returns false and does nothing otherwise
    // TryRelease - Like Release, but it notifies you whether the array was successfully returned

    // Unlike StringBuilderCache, we do not clear the array when calling Acquire.
    // It's up to you to null it out once you rent the array.

    // IMPORTANT NOTE: You may wish to clear the array before returning it
    // to the pool (via Array.Clear or a manual for-loop) if:
    // - T is non-primitive and is/contains reference types, since that
    //   will free up those references for the GC to collect if they aren't
    //   being used anywhere else.
    // - The array contains sensitive data and needs to be zeroed out.

    // A clearArray parameter is not provided for convenience like in ArrayPool, since:
    // - In the first scenario above, it may be smarter to only clear it
    //   if the array is actually returned to the pool. Assuming the array
    //   you rented is no longer in use after you return it, its contents
    //   will be GC'd anyway and you avoid clearing out a gigantic array.
    // - The caller may be able to call Array.Clear more efficiently if it
    //   knows that only part of the array was used (which will be a common
    //   case if Acquire returns something with a length > minimumLength).

    internal static class ArrayCache<T>
    {
        // MaxArrayLength: Maximum size of arrays we can hold

        // In the future if the Unsafe class is exposed to System.Private.CoreLib,
        // we may want to make this dependent on sizeof(T), e.g.
        // static int MaxArrayLength => 1024 / Unsafe.SizeOf<T>();
        // That's why this is a property for now.

        private static int MaxArrayLength => 1024;

        [ThreadStatic]
        private static T[] t_array; // The cached array we'll hand out to renters; null if we don't have one yet.

        public static T[] Acquire(int minimumLength)
        {
            Contract.Assert(minimumLength >= 0);

            // NOTE: This method should not perform any field accesses to t_array,
            // since that's expensive. TryAcquire should do all the work.
            
            T[] result;
            if (!TryAcquire(minimumLength, out result))
            {
                // The cached array is either not present or too small, so allocate another one.
                result = new T[minimumLength];
            }

            return result;
        }

        public static void Release(T[] array)
        {
            Contract.Assert(array != null);
            
            TryRelease(array);
        }

        public static bool TryAcquire(int minimumLength, out T[] array)
        {
            Contract.Assert(minimumLength >= 0);

            // If this condition is false, we know we won't
            // have a suitable cached array to hand out and
            // we can save a thread-static field access (see below)
            if (minimumLength < MaxArrayLength)
            {
                // Return the same empty array if minimumLength == 0,
                // since 0-length arrays are immutable
                if (minimumLength == 0)
                {
                    array = Array.Empty<T>();
                    return true;
                }
                
                T[] localArray = t_array; // It's important to cache the field into a local here, since ThreadStatic field accesses can be expensive
                Contract.Assert(localArray == null || localArray.Length <= MaxArrayLength);

                if (localArray != null && localArray.Length >= minimumLength)
                {
                    // We can use the cached array! Since it's in use, set t_array to null.
                    t_array = null;

                    // We don't clear the array since unlike StringBuilder, it's common
                    // that the data will be overwritten soon after. Call Array.Clear yourself
                    // if you need to do this.
                    array = localArray;
                    return true;
                }
            }

            array = null;
            return false;
        }

        public static bool TryRelease(T[] array)
        {
            Contract.Assert(array != null);

            bool shouldRelease = array.Length <= MaxArrayLength;

            // Ignore empty arrays; there's no use caching them
            // since we return Array.Empty<T> for minimumLength == 0.
            // For consistency's sake we still return true, since otherwise
            // 0 would be the only length TryAcquire returns true for and
            // this method would return false (at least with the current
            // implementation).
            if (array.Length != 0 && shouldRelease)
            {
                // We don't check if we already have an array bigger
                // than the one given since (as mentioned above)
                // thread-static field accesses are expensive, and
                // since Release is usually used in tandem with
                // Acquire that would very rarely happen.

                t_array = array;
            }

            return shouldRelease;
        }
    }
}
