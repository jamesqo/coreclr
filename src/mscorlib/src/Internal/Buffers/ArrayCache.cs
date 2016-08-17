// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Internal.Buffers
{
    // This class implements a lockless/thread-safe array cache.
    // All of the state is held in t_array, which is ThreadStatic
    // so every thread gets its own copy of the field.

    // Methods:
    // Acquire - Rents an array from the cache, or allocates a new one.
    // Release - Returns the array to the cache if it's not too big.
    // Fast* - These methods expose some implementation details about the class,
    //         and make some assumptions about the caller, in exchange for faster
    //         performance. (see below)

    // Like StringBuilderCache, a typical use of this class could be like this:
    // var array = ArrayCache<T>.Acquire(length);
    // ...do work with array...
    // ArrayCache<T>.Release(array); // return when done

    // However, this is somewhat inefficient. ThreadStatic field accesses can
    // be very slow compared to normal ones- and we're making 3 of them.
    // Acquire does 2, one to read t_array and one to set it to null.
    // Release also does 1, to set the field back to the original value.

    // Since the last 2 essentially cancel each other out, we can
    // skip them if we know none of our callees call {Fast}Acquire
    // before we call {Fast}Release, hence the Fast* overloads. You
    // can use them like this:

    // bool wasCached;
    // var array = ArrayCache<T>.FastAcquire(length, out wasCached);
    // ...do work with array...
    // ArrayCache<T>.FastRelease(array, wasCached);

    // The wasCached parameter is necessary because it allows us
    // to not write anything during FastRelease if t_array already
    // contains that value (which will be the common case).
    // This leaves only 1 ThreadStatic access: the initial read
    // of t_array.

    // If t_array was originally null or the minimumLength specified
    // during FastAcquire was bigger than the currently cached array's
    // length, we do one additional access to write to t_array.

    // More notes:

    // Unlike StringBuilderCache, we do not clear the array when calling Acquire.
    // It's up to you to null it out once you rent the array.

    // You may also wish to clear the array before returning it, if:
    // - T is a reference type or contains reference types
    //   This will allow the GC to collect them as soon as
    //   you're done with them.
    // - The array contains sensitive data
    // Note that for the first scenario above, it may be smarter
    // to only clear if the array is actually returned to the pool.
    // (Otherwise if the array is unreachable, the references will
    // still get collected anyway.) You can do this via
    // if (ArrayCache<T>.Release(array)) Array.Clear(array, 0, length);
    // Since the field is ThreadStatic, there's no risk another thread
    // will see t_array before it's finished clearing.

    internal static class ArrayCache<T>
    {
        // MaxArrayLength: Maximum size of arrays we can hold

        // In the future if there is a way to calculate sizeof(T) here,
        // we may want to use that, e.g.
        // static int MaxArrayLength => 1024 / sizeof(T)
        // That's why this is a property instead of a const.

        private static int MaxArrayLength => 1024;

        [ThreadStatic]
        private static T[] t_array; // The cached array we'll hand out to renters; null if we don't have one yet.

        // 'Safe' version of FastAcquire- sets t_array to null
        // if we rented the array.
        // You will want to do this if one of your callees call
        // ArrayCache.{Fast}Acquire before you call Release-
        // you may want to use this if you're i.e. making virtual
        // method calls between Acquire/Release.
        public static T[] Acquire(int minimumLength)
        {
            bool wasCached;
            T[] result = FastAcquire(minimumLength, out wasCached);

            Contract.Assert(!wasCached || t_array == result);

            if (wasCached)
            {
                t_array = null;
            }

            return result;
        }

        // Since the caller is unlikely to be inlined anyway (correct usage
        // of this class constitutes calls to {Fast}Acquire/Release),
        // we aggressively inline these into the caller.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] FastAcquire(int minimumLength, out bool wasCached)
        {
            Contract.Assert(minimumLength >= 0);

            // If minimumLength > MaxArrayLength, we already know it
            // won't fit the requirements, so don't even both with the
            // ThreadStatic access
            if (minimumLength <= MaxArrayLength)
            {
                T[] localArray = t_array; // Cache the ThreadStatic into a local
                Contract.Assert(localArray == null || localArray.Length <= MaxArrayLength);

                if (localArray != null && localArray.Length >= minimumLength)
                {
                    wasCached = true;
                    return localArray;
                }
            }

            // The cached array was either nonexistent or didn't meet our requirements;
            // return a new array

            wasCached = false;
            return new T[minimumLength];
        }

        // If wasCached is true, then t_array should already
        // be set to the value and we don't need to write it.
        // Otherwise, we need to update the value of t_array.
        // This method returns true if wasCached is true.
        public static bool FastRelease(T[] array, bool wasCached)
        {
            Contract.Assert(!wasCached || t_array == array);

            return wasCached || Release(array);
        }

        // Releases the array if it meets the requirements;
        // returns whether the array was actually released.
        // This needs to be tagged AggressiveInlining as well,
        // since the generated code for thread-static writes
        // includes a
        // CORINFO_HELP_ASSIGN_REF
        // in addition to
        // CORINFO_HELP_GETSHARED_GCTHREADSTATIC_BASE_DYNAMICCLASS.
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
