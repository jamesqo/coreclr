// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Text
{
    public sealed partial class StringBuilder
    {
        /// <summary>
        /// A non-head node in a <see cref="StringBuilder"/>'s singly-linked list of char buffers.
        /// </summary>
        private class Chunk
        {
            /// <summary>
            /// Constructs a new chunk.
            /// </summary>
            /// <param name="previous">The chunk which the chars in this chunk logically follow.</param>
            /// <param name="chars">The char buffer for this chunk.</param>
            /// <param name="length">The number of chars usable in the buffer, starting from index 0.</param>
            public Chunk(Chunk previous, char[] chars, int length)
            {
                Debug.Assert(chars?.Length > 0);
                Debug.Assert(length >= 0);

                Previous = previous;
                Chars = chars;
                Length = length;
            }

            /// <summary>
            /// The chunk which the chars in this chunk logically follow.
            /// </summary>
            public Chunk Previous { get; }

            /// <summary>
            /// The char buffer for this chunk.
            /// </summary>
            public char[] Chars { get; }

            /// <summary>
            /// The number of chars usable in the buffer, starting from index 0.
            /// </summary>
            public int Length { get; }
        }
    }
}
