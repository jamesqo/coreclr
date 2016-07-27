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
    // to hold multiple arrays if we run into those scenarios)

    // This class is lockless/thread-safe since all the state is held in t_array,
    // which is marked as [ThreadStatic] so every thread gets its own
    // copy of the field. (In addition, it gets another instantiation for
    // every different type parameter)

    // The methods in here mirror the ones in StringBuilder:
    // Acquire - Rents a new array with the specified minimum capacity. (not necessarily clear)
    // Release - Returns the array so it can be used by another method

    // Unlike StringBuilderCache, we do not clear the array when calling Acquire.
    // It's up to you to null it out once you rent the array.

    // We do, however, allow you to clear the array when returning via
    // the optional clearArray parameter (echoing the ArrayPool.Return method).
    //
    // You may want to do this if:
    // - T is non-primitive and is/contains reference types, since that
    //   will free up those references for the GC to collect if they aren't
    //   being used anywhere else.
    // - The array contains sensitive data and needs to be zeroed out.

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
            
            // If this condition is false, we know we won't
            // have a suitable cached array to hand out and
            // we can save a thread-static field access (see below)
            if (minimumLength < MaxArrayLength)
            {
                // Return the same empty array if minimumLength == 0,
                // since 0-length arrays are immutable
                if (minimumLength == 0)
                    return Array.Empty<T>();
                
                T[] localArray = t_array; // It's important to cache the field into a local here, since ThreadStatic field accesses can be expensive

                if (localArray != null && localArray.Length >= minimumLength)
                {
                    // We can use the cached array! Since it's in use, set t_array to null.
                    t_array = null;

                    // We don't clear the array since unlike StringBuilder, it's common
                    // that the data will be overwritten soon after. Call Array.Clear yourself
                    // if you need to do this.
                    return localArray;
                }
            }
            
            // The cached array is either not present or too small, so allocate another one.
            return new T[minimumLength];
        }

        public static void Release(T[] array, bool clearArray = false)
        {
            Contract.Assert(array != null);

            // Ignore empty arrays; there's no use caching them
            // since we return Array.Empty<T> for minimumLength == 0.
            if (array.Length != 0)
            {
                if (clearArray)
                {
                    Array.Clear(array, 0, array.Length);
                }

                // We don't check if we already have an array bigger
                // than the one given since (as mentioned above)
                // thread-static field accesses are expensive, and
                // since Release is usually used in tandem with
                // Acquire that would very rarely happen.

                if (array.Length <= MaxArrayLength)
                {
                    t_array = array;
                }
            }
        }
    }
}
