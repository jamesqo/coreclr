// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Text
{
    /// <summary>
    /// An abstraction over the contents of one node in a <see cref="StringBuilder"/>.
    /// </summary>
    internal struct StringBuilderChunk
    {
        /// <summary>
        /// Constructs a new chunk.
        /// </summary>
        /// <param name="previousNode">The node which the chars in this chunk logically follow.</param>
        /// <param name="chars">The underlying char buffer for this chunk.</param>
        /// <param name="count">The number of chars usable in the buffer, starting from index 0.</param>
        private StringBuilderChunk(StringBuilderNode previousNode, char[] chars, int count)
        {
            Debug.Assert(chars?.Length > 0);
            Debug.Assert(count >= 0);

            PreviousNode = previousNode;
            Chars = chars;
            Count = count;
        }

        /// <summary>
        /// The node which the chars in this chunk logically follow.
        /// </summary>
        public StringBuilderNode PreviousNode { get; }

        /// <summary>
        /// The underlying char buffer for this chunk.
        /// </summary>
        public char[] Chars { get; }

        /// <summary>
        /// The number of chars usable in the buffer, starting from index 0.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Constructs a new chunk from the given node.
        /// </summary>
        /// <param name="node">The node.</param>
        public static implicit operator StringBuilderChunk(StringBuilderNode node) => node.AsChunk();

        /// <summary>
        /// Constructs a new chunk from the given builder.
        /// </summary>
        /// <param name="builder">The builder.</param>
        public static implicit operator StringBuilderChunk(StringBuilder builder) => builder.AsChunk();
    }
}
