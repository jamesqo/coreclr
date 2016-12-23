// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Text
{
    /// <summary>
    /// Represents a non-head node in a <see cref="StringBuilder"/>.
    /// </summary>
    internal sealed class StringBuilderNode
    {
        /// <summary>
        /// The contents of this node.
        /// </summary>
        private readonly StringBuilderChunk _chunk;

        /// <summary>
        /// Constructs a new node.
        /// </summary>
        /// <param name="chunk">The contents of this node.</param>
        public StringBuilderNode(StringBuilderChunk chunk)
        {
            _chunk = chunk;
        }

        /// <summary>
        /// Gets a chunk that contains the contents of this node.
        /// </summary>
        public StringBuilderChunk AsChunk() => _chunk;
    }
}
