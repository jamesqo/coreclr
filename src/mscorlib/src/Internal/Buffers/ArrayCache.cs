// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
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
            bool wasCached;
            T[] result = FastAcquire(minimumLength, out wasCached);

            if (wasCached)
            {
                t_array = null;
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] FastAcquire(int minimumLength, out bool wasCached)
        {
            Contract.Assert(minimumLength >= 0);

            if (minimumLength < MaxArrayLength)
            {
                T[] localArray = t_array;
                Contract.Assert(localArray == null || localArray.Length <= MaxArrayLength);

                if (localArray != null && localArray.Length >= minimumLength)
                {
                    wasCached = true;
                    return localArray;
                }
            }

            wasCached = false;
            return new T[minimumLength];
        }

        public static bool FastRelease(T[] array, bool wasCached)
        {
            Contract.Assert(!wasCached || t_array == array);

            return wasCached || Release(array);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Release(T[] array)
        {
            Contract.Assert(array != null);

            bool shouldRelease = array.Length <= MaxArrayLength;

            if (shouldRelease)
            {
                t_array = array;
            }

            return shouldRelease;
        }
    }
}
