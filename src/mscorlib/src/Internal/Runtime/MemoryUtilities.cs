using System;
using System.Diagnostics.Contracts;

#if BIT64
using nint = System.Int64;
using nuint = System.UInt64;
#else // BIT64
using nint = System.Int32;
using nuint = System.UInt32;
#endif // BIT64

namespace Internal.Runtime
{
    // Much like Buffer, this class contains unsafe static
    // methods for dealing with memory.

    // Note: The methods in this class are performance-sensitive
    // as they're used in low-level classes like String. Please
    // make sure you don't introduce any performance regressions
    // when changing the methods in this class, and consider the
    // impact your changes may make on the generated asm.
    
    internal static class MemoryUtilities
    {
        // The following terms are used in this class:
        // aligned - if ptr % IntPtr.Size == 0, then it is 'aligned'.
        // string-aligned - if (ptr + 4 bytes) is aligned, then ptr is 'string-aligned'.
        //                  Note that this means ptr itself will be aligned for 32-bit, but not 64-bit.
        //                  This should hold true for all pointers obtained by using the fixed statement on a string.
        
        public unsafe static void AlignedCopy(char* dest, char* src, int count)
        {
            // If you make a change to this function, you may also
            // wish to change the StringAlignedCopy function if
            // applicable.

            AssertAligned(dest, nameof(dest));
            AssertAligned(src, nameof(src));
            Contract.Assert(count >= 0);
            AssertNonOverlapping(dest, src, count);

            // First switch on count and specialize the implementation for small lengths
            // This is implemented very efficiently (no branching) in asm using jump tables
            // It also benefits the main code path by eliminating a branch (see loop below)

            // The reason we're able to get away with such a gigantic method (as in Buffer.MemoryCopy)
            // is because this code is aot-compiled via crossgen, meaning that there will be no
            // jit compilation overhead this first time this method is run (which can be quite substantial)

            // If porting this method out of System.Private.CoreLib into a jitted environment,
            // please consider reducing the code size of this method, or there may be a significant
            // performance degredation the first time this run. (See also: http://stackoverflow.com/q/36610160/4077294)

            // Additional note: case 4 and above contains ifdefs for x64 since we want to copy
            // in the natural word size of the processor; e.g. ints for 32-bit and longs for 64-bit.
            // Unfortunately separating them out into a single helper function that contains the ifdef, e.g.
            
            // #if BIT64
            // static void CopyLong(void* dest, void* src) => *(long*)dest = *(long*)src;
            // #else ...

            // seems to generate less efficient code even when it is inlined, at least for ryujit-x64.

            switch (count)
            {
                case 0:
                    return;
                case 1:
                    *dest = *src;
                    return;
                case 2:
                    *(int*)dest = *(int*)src;
                    return;
                case 3:
                    *(int*)dest = *(int*)src;
                    *(dest + 2) = *(src + 2);
                    return;
                case 4:
#if BIT64
                    *(long*)dest = *(long*)src;
#else // BIT64
                    *(int*)dest = *(int*)src;
                    *(int*)(dest + 2) = *(int*)(src + 2);
#endif // BIT64
                    return;
                case 5:
#if BIT64
                    *(long*)dest = *(long*)src;
#else // BIT64
                    *(int*)dest = *(int*)src;
                    *(int*)(dest + 2) = *(int*)(src + 2);
#endif // BIT64
                    *(dest + 4) = *(src + 4);
                    return;
                case 6:
#if BIT64
                    *(long*)dest = *(long*)src;
#else // BIT64
                    *(int*)dest = *(int*)src;
                    *(int*)(dest + 2) = *(int*)(src + 2);
#endif // BIT64
                    *(int*)(dest + 4) = *(int*)(src + 4);
                    return;
                case 7:
#if BIT64
                    *(long*)dest = *(long*)src;
#else // BIT64
                    *(int*)dest = *(int*)src;
                    *(int*)(dest + 2) = *(int*)(src + 2);
#endif // BIT64
                    *(int*)(dest + 4) = *(int*)(src + 4);
                    *(dest + 6) = *(src + 6);
                    return;
            }

            // Use the native implementation (which is likely to beat us)
            // for large lengths
            if (count >= 256)
                goto PInvoke;

            const int Chunk = 8; // 8 chars per iteration of the loop
            
            // Divide count by the number of chars we're going to write beforehand
            // This allows us to do a test _, _ rather than a cmp per iteration,
            // and also dec rather than sub (although the former may be slightly slower),
            // both of which slightly reduce the loop code size
            int iterations = count / Chunk;

            // Write one word at a time; additionally loop unroll for better perf

            // We know that at least one iteration will always run due to the
            // switch/range checks above, so use do .. while to avoid branching
            // on the first run
            Contract.Assert(iterations > 0);
            
            do
            {
#if BIT64
                *(long*)dest = *(long*)src;
                *(long*)(dest + 4) = *(long*)(src + 4);
#else // BIT64
                *(int*)dest = *(int*)src;
                *(int*)(dest + 2) = *(int*)(src + 2);
                *(int*)(dest + 4) = *(int*)(src + 4);
                *(int*)(dest + 6) = *(int*)(src + 6);
#endif // BIT64
                
                iterations--; dest += Chunk; src += Chunk;
            }
            while (iterations > 0);
            
            // Finish up any leftover chars
            // If the nth lsb (0-indexed) is set, it means we have at least 2 ^ n more chars to copy
            // e.g. if count % 8 is 5, which is 0b101, then we take 2 ^ 2 + 2 ^ 0 to get the remaining chars
            
            if ((count & 4) != 0)
            {
#if BIT64
                *(long*)dest = *(long*)src;
#else // BIT64
                *(int*)dest = *(int*)src;
                *(int*)(dest + 2) = *(int*)(src + 2);
#endif // BIT64
                dest += 4; src += 4;
            }
            
            if ((count & 2) != 0)
            {
                *(int*)dest = *(int*)src;
                dest += 2; src += 2;
            }
            
            if ((count & 1) != 0)
            {
                *dest = *src;
                // not needed since we're no longer using the variables
                // dest++; src++;
            }

            return;

            PInvoke:
            NativeMemmove(dest, src, count);            
        }
        
        public unsafe static void StringAlignedCopy(char* dest, char* src, int count)
        {
            // If you make a change in this function, you may also
            // wish to change the AlignedCopy function if applicable.

            // The reason this is separated out from AlignedCopy on x64,
            // rather than just incrementing the pointers by 4 bytes
            // and forwarding to that function, is because we want to
            // avoid the overhead of two function calls (thus the duplicated code)

            AssertStringAligned(dest, nameof(dest));
            AssertStringAligned(src, nameof(src));
            Contract.Assert(count >= 0);
            AssertNonOverlapping(dest, src, count);

#if !BIT64
            // On x86 all 'string-aligned' pointers are aligned, so
            // all we do is forward and this will get inlined
            // This also helps with code size

            // Note: if the 64-bit part of this function ever
            // gets compiled for i386, then the relevant parts
            // (e.g. places that use long) should be changed

            AlignedCopy(dest, src, count);
#else // !BIT64

            // Switch for small lengths
            // This will be implemented in asm as a jump table,
            // and also helps the main codepath (see loop below)

            // If you're porting this to a jitted environment,
            // please see notes in AlignedCopy pertaining to code size

            switch (count)
            {
                case 0:
                    return;
                case 1:
                    *dest = *src;
                    return;
                case 2:
                    *(int*)dest = *(int*)src;
                    return;
                case 3:
                    *(int*)dest = *(int*)src;
                    *(dest + 2) = *(src + 2);
                    return;
                case 4:
                    // Take note: we don't use long* here like in AlignedCopy since
                    // that would cross a word boundary (string-aligned pointers are
                    // 2 chars off from alignment on x64). Instead, we wait until
                    // case 6 to do so.
                    *(int*)dest = *(int*)src;
                    *(int*)(dest + 2) = *(int*)(src + 2);
                    return;
                case 5:
                    *(int*)dest = *(int*)src;
                    *(int*)(dest + 2) = *(int*)(src + 2);
                    *(dest + 4) = *(src + 4);
                    return;
                case 6:
                    *(int*)dest = *(int*)src;
                    *(long*)(dest + 2) = *(long*)(src + 2);
                    return;
                case 7:
                    *(int*)dest = *(int*)src;
                    *(long*)(dest + 2) = *(long*)(src + 2);
                    *(dest + 6) = *(src + 6);
                    return;
                // Since we have to handle the first 2 chars specially
                // to align the pointers, we have to add an extra 2 cases
                // to reap the benefit of eliminating a branch
                case 8:
                    *(int*)dest = *(int*)src;
                    *(long*)(dest + 2) = *(long*)(src + 2);
                    *(int*)(dest + 6) = *(int*)(src + 6);
                    return;
                case 9:
                    *(int*)dest = *(int*)src;
                    *(long*)(dest + 2) = *(long*)(src + 2);
                    *(int*)(dest + 6) = *(int*)(src + 6);
                    *(dest + 8) = *(src + 8);
                    return;
            }

            // Align the pointer on a word boundary
            // Due to the above switch and range checks we know
            // that count > 8, so we don't have to check anything here
            *(int*)dest = *(int*)src;
            count -= 2; dest += 2; src += 2;

            // Forward to the native method (which is likely to have a faster implementation) for large lengths
            // We do this after aligning the pointers, since then it's more likely that the native implementation
            // will notice they're aligned and copy a word at a time, similarly to how we do it
            if (count >= 256)
                goto PInvoke; // goto avoids a jump for small buffers

            const int Chunk = 8; // number of chars to copy per iteration of the loop

            int iterations = count / Chunk; // see notes in AlignedCopy for the same variable

            // Since we've handled all cases count < 10 and
            // decremented by 2, that means count / 8 > 0
            // so we can skip a branch the first run of the loop
            Contract.Assert(iterations > 0);

            do
            {
                *(long*)dest = *(long*)src;
                *(long*)(dest + 4) = *(long*)(src + 4);

                iterations--; dest += Chunk; src += Chunk;
            }
            while (iterations > 0);

            // Handle leftover chars

            if ((count & 4) != 0)
            {
                *(long*)dest = *(long*)src;
                dest += 4; src += 4;
            }
            if ((count & 2) != 0)
            {
                *(int*)dest = *(int*)src;
                dest += 2; src += 2;
            }
            if ((count & 1) != 0)
            {
                *dest = *src;
                // dest++; src++;
            }

            return;

            PInvoke:
            NativeMemmove(dest, src, count);

#endif // !BIT64
        }

        private unsafe static void NativeMemmove(void* dest, void* src, int count)
        {
            Buffer._Memmove((byte*)dest, (byte*)src, (nuint)count);
        }

        // Debug helper methods

        // Note: These don't have [Conditional("_DEBUG")] intentionally, in
        // case this code needs to be ported somewhere else that doesn't
        // define that symbol.
        // These should get inlined and forgotten by the jit anyway, since
        // the Contract.Asserts are just compiled out in Release mode and
        // the whole method becomes a nop.

        private unsafe static void AssertAligned(void* pointer, string name)
        {
            Contract.Assert((int)pointer % IntPtr.Size == 0, $"{name} should be aligned!");
        }

        private unsafe static void AssertStringAligned(void* pointer, string name)
        {
            Contract.Assert(((int)pointer + 4) % IntPtr.Size == 0, $"{name} should be string-aligned!");
        }

        private unsafe static void AssertNonOverlapping(void* dest, void* src, int count)
        {
            Contract.Assert(Math.Abs((nint)dest - (nint)src) >= count, "Buffers should not overlap!");
            Contract.Assert(dest != src, "The destination and source should be different buffers!");
        }
    }
}
