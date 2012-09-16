﻿#region License
/*
   Copyright 2011 Sunny Ahuwanya (www.ahuwanya.net)

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
#endregion

namespace ServerToolkit.BufferManagement
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /// <summary>
    /// Represents an efficiently allocated buffer for asynchronous read/write operations.
    /// </summary>
    public sealed class ManagedBuffer : IBuffer
    {
        private bool disposed = false;

        readonly IMemoryBlock memoryBlock;
        readonly byte[] slabArray;

        /// <summary>
        /// Initializes a new instance of the ManagedBuffer class, specifying the memory block that the ManagedBuffer reads and writes to.
        /// </summary>
        /// <param name="allocatedMemoryBlock">Underlying allocated memory block</param>
        internal ManagedBuffer(IMemoryBlock allocatedMemoryBlock)
        {
            if (allocatedMemoryBlock == null) throw new ArgumentNullException("allocatedMemoryBlock");
            memoryBlock = allocatedMemoryBlock;
            slabArray = null;
        }


        /// <summary>
        /// Initializes a new instance of the ManagedBuffer class, specifying the slab to be associated with the ManagedBuffer.
        /// This constructor creates an empty (zero-length) buffer.
        /// </summary>
        /// <param name="slab">The Memory Slab to be associated with the ManagedBuffer</param>
        internal ManagedBuffer(IMemorySlab slab)
        {
            if (slab == null) throw new ArgumentNullException("slab");
            memoryBlock = null;
            this.slabArray = slab.Array;
        }


        /// <summary>
        /// Gets a value indicating whether the buffer is disposed.
        /// </summary>
        public bool IsDisposed
        {
            get { return disposed; }
        }

        /// <summary>
        /// Gets the total size of the buffer, in bytes.
        /// </summary>        
        public long Size
        {
            get { return memoryBlock == null ? 0 : memoryBlock.Length; }
        }

        /// <summary>
        /// Gets the number of segments in the buffer.
        /// </summary>
        public int SegmentCount
        {
            //TODO: MULTI_ARRAY_SEGMENTS: Fix this
            get { return 1; /*Always 1 for now */ }
        }

        /// <summary>
        /// Gets the underlying memory block(s)
        /// </summary>
        /// <remarks>This property is provided for testing purposes</remarks>
        internal IMemoryBlock MemoryBlocks
        {
            get { return memoryBlock; }
        } 


        //NOTE: This overload cannot return segments larger than int.MaxValue;
        //TODO: MULTI_ARRAY_SEGMENTS: NOTE: This method should be able to accept length > int.MaxValue after implementing multi-array-segments

        /// <summary>
        /// Gets buffer segments that can be passed on to an asynchronous socket operation.
        /// </summary>
        /// <returns>A list of ArraySegments(of Byte) containing buffer segments.</returns>
        public IList<ArraySegment<byte>> GetSegments()
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());

            //TODO: MULTI_ARRAY_SEGMENTS: NOTE: This int.MaxValue should be removed after implementing multi-array-segments
            if (this.Size <= int.MaxValue)
            {
                return GetSegments(0, (int)this.Size);
            }
            else
            {
                return GetSegments(0, int.MaxValue);
            }
            
        }

        /// <summary>
        /// Gets buffer segments that can be passed on to an asynchronous socket operation.
        /// </summary>
        /// <param name="length">Total length of segments.</param>
        /// <returns>A list of ArraySegments(of Byte) containing buffer segments.</returns>
        public IList<ArraySegment<byte>> GetSegments(long length)
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());
            return GetSegments(0, length);
        }

        /// <summary>
        /// Gets buffer segments that can be passed on to an asynchronous socket operation.
        /// </summary>
        /// <param name="offset">Offset in the buffer where segments start.</param>
        /// <param name="length">Total length of segments.</param>
        /// <returns>A list of ArraySegments(of Byte) containing buffer segments.</returns>
        public IList<ArraySegment<byte>> GetSegments(long offset, long length)
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());
            if (length > this.Size || length < 0)
            {
                throw new ArgumentOutOfRangeException("length");
            }

            if ((offset >= this.Size && this.Size != 0) || offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            IList<ArraySegment<byte>> result = new List<ArraySegment<byte>>();
            if (this.Size == 0)
            {
                result.Add(new ArraySegment<byte>(slabArray, 0, 0));
                return result;
            }
            else
            {
                //TODO: MULTI_ARRAY_SEGMENTS: NOTE: This exception should not take place after implementing multi-array-segments
                // and a limit to SlabSize (MaximumSlabSize) is in place, which would probably be (int.MaxValue * 2);
                if (offset + memoryBlock.StartLocation > int.MaxValue)
                {
                    throw new InvalidOperationException("ArraySegment location exceeds int.MaxValue");
                }

                result.Add(new ArraySegment<byte>(memoryBlock.Slab.Array, (int)(offset + memoryBlock.StartLocation), (int)length));
                return result;
            }
        }

        /// <summary>
        /// Copies data from the buffer to a byte array.
        /// </summary>
        /// <param name="destinationArray">The one-dimensional byte array which receives the data.</param>
        /// <remarks>The size of the buffer must be less than or equal to the destinationArray length.</remarks>
        public void CopyTo(byte[] destinationArray)
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());

            CopyTo(destinationArray, 0, this.Size);
        }

        /// <summary>
        /// Copies data from the buffer to a byte array
        /// </summary>
        /// <param name="destinationArray">The one-dimensional byte array which receives the data.</param>
        /// <param name="destinationIndex">The index in the destinationArray at which storing begins.</param>
        /// <param name="length">The number of bytes to copy.</param>
        public void CopyTo(byte[] destinationArray, long destinationIndex, long length)
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());
            if (destinationArray == null) throw new ArgumentNullException("destinationArray");
            if (length > this.Size) throw new ArgumentException("length is larger than buffer size");
            if (this.Size == 0) return;

            Array.Copy(memoryBlock.Slab.Array, memoryBlock.StartLocation, destinationArray, destinationIndex, length);
        }

        /// <summary>
        /// Copies data from a byte array into the buffer.
        /// </summary>
        /// <param name="sourceArray">The one-dimensional byte array that contains the data.</param>
        /// <remarks>The length of the sourceArray must be less than or equal to the buffer size.</remarks>
        [Obsolete("Use the FillWith method instead -- this method will be removed in a later version", true)]
        public void CopyFrom(byte[] sourceArray)
        {
            FillWith(sourceArray);
        }

        /// <summary>
        /// Copies data from a byte array into the buffer.
        /// </summary>
        /// <param name="sourceArray">The one-dimensional byte array that contains the data.</param>
        /// <param name="sourceIndex">The index in the sourceArray at which copying begins.</param>
        /// <param name="length">The number of bytes to copy.</param>
        [Obsolete("Use the FillWith method instead -- this method will be removed in a later version", true)]
        public void CopyFrom(byte[] sourceArray, long sourceIndex, long length)
        {
            FillWith(sourceArray, sourceIndex, length);
        }

        /// <summary>
        /// Copies data from a byte array into the buffer.
        /// </summary>
        /// <param name="sourceArray">The one-dimensional byte array that contains the data.</param>
        /// <remarks>The length of the sourceArray must be less than or equal to the buffer size.</remarks>
        public void FillWith(byte[] sourceArray)
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());

            FillWith(sourceArray, 0, sourceArray.Length);
        }

        /// <summary>
        /// Copies data from a byte array into the buffer.
        /// </summary>
        /// <param name="sourceArray">The one-dimensional byte array that contains the data.</param>
        /// <param name="sourceIndex">The index in the sourceArray at which copying begins.</param>
        /// <param name="length">The number of bytes to copy.</param>
        public void FillWith(byte[] sourceArray, long sourceIndex, long length)
        {
            if (disposed) throw new ObjectDisposedException(this.ToString());
            if (sourceArray == null) throw new ArgumentNullException("sourceArray");
            if (length > (sourceIndex + this.Size)) throw new ArgumentException("length will not fit in the buffer");
            if (this.Size == 0) return;

            Array.Copy(sourceArray, sourceIndex, memoryBlock.Slab.Array, memoryBlock.StartLocation, length);

            //NOTE: try not to keep this method as simple as possible, it's can be called from IBuffer.GetBuffer
            //and we do not want new unexpected exceptions been thrown.
        }

        /// <summary>
        /// Releases resources used by the buffer.
        /// </summary>
        /// <remarks>This method frees the memory blocks used by the buffer.</remarks>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Releases resources used by the buffer.
        /// </summary>
        /// <param name="disposing">True, to indicate you want to release all resources. False to release only native resources.</param>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!disposed)
                {
                    disposed = true;

                    try
                    {
                        if (memoryBlock != null)
                        {
                            memoryBlock.Slab.Free(memoryBlock);
                        }
                    }
                    catch
                    {
                        //Suppress exception in release mode
                        #if DEBUG
                            throw;
                        #endif
                    }

                }
            }
        }

    }

    /// <summary>
    /// Provides a pool of buffers that can be used to efficiently allocate memory for asynchronous socket operations
    /// </summary>
    public sealed class BufferPool : IBufferPool
    {
        public const int MinimumSlabSize = 92160; //90 KB to force slab into LOH
        private readonly IMemorySlab firstSlab;

        private long slabSize;
        private int initialSlabs, subsequentSlabs;
        private object syncSlabList = new object(); //synchronizes access to the array of slabs
        private object syncNewSlab = new object(); //synchronizes access to new slab creation
        private List<IMemorySlab> slabs = new List<IMemorySlab>();
        private int singleSlabPool; //-1 or 0, used for faster access if only one slab is available

        /// <summary>
        /// Initializes a new instance of the BufferPool class
        /// </summary>
        /// <param name="slabSize">Length, in bytes, of a slab in the BufferPool</param>
        /// <param name="initialSlabs">Number of slabs to create initially</param>
        /// <param name="subsequentSlabs">Number of additional slabs to create at a time</param>
        public BufferPool(long slabSize, int initialSlabs, int subsequentSlabs)
        {

            if (slabSize < 1) throw new ArgumentException("slabSize must be equal to or greater than 1");
            if (initialSlabs < 1) throw new ArgumentException("initialSlabs must be equal to or greater than 1");
            if (subsequentSlabs < 1) throw new ArgumentException("subsequentSlabs must be equal to or greater than 1");

            this.slabSize = slabSize > MinimumSlabSize ? slabSize : MinimumSlabSize;
            this.initialSlabs = initialSlabs;
            this.subsequentSlabs = subsequentSlabs;

            // lock is unnecessary in this instance constructor
            //lock (syncSlabList)
            //{
                if (slabs.Count == 0)
                {
                    SetSingleSlabPool(initialSlabs == 1); //Assume for optimization reasons that it's a single slab pool if the number of initial slabs is 1

                    for (int i = 0; i < initialSlabs; i++)
                    {
                        slabs.Add(new MemorySlab(slabSize, this));
                    }

                    firstSlab = slabs[0];
                }
            //}
        }

        /// <summary>
        /// Gets the initial number of slabs created
        /// </summary>
        public int InitialSlabs
        {
            get { return initialSlabs; }
        }

        /// <summary>
        /// Gets the additional number of slabs to be created at a time
        /// </summary>
        public int SubsequentSlabs
        {
            get { return subsequentSlabs; }
        }

        /// <summary>
        /// Gets the slab size, in bytes
        /// </summary>
        public long SlabSize
        {
            get { return slabSize; }
        }

        /// <summary>
        /// Gets the number of slabs in the buffer pool
        /// </summary>
        /// <remarks>This property is provided for testing purposes</remarks>
        internal long SlabCount
        {
            get { return slabs.Count; }
        }


        //Pair of Get/Set methods for the optimization singleSlabPool field. This property is accessed instead of the field
        //to prevent the compiler from performing optimizations that may render the field unreliable
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool GetSingleSlabPool()
        {
            return singleSlabPool == -1 ? true : false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetSingleSlabPool(bool value)
        {
            Interlocked.Exchange(ref singleSlabPool, value == true ? -1 : 0);
        }


        /// <summary>
        /// Creates a buffer of the specified size
        /// </summary>
        /// <param name="size">Buffer size, in bytes</param>
        /// <returns>IBuffer object of requested size</returns>        
        public IBuffer GetBuffer(long size)
        {
            return GetBuffer(size, null);
        }

        /// <summary>
        /// Creates a buffer of the specified size, filled with the contents of a specified byte array
        /// </summary>
        /// <param name="size">Buffer size, in bytes</param>
        /// <param name="filledWith">Byte array to copy to buffer</param>
        /// <returns>IBuffer object of requested size</returns>
        public IBuffer GetBuffer(long size, byte[] filledWith)
        {
            if (size < 0) throw new ArgumentException("size must be greater than 0");

            //Make sure filledWith can fit into the requested buffer, so that we do not allocate a buffer and then
            //an exception is thrown (when IBuffer.FillWith() is called) before the buffer is returned.
            if (filledWith != null)
            {
                if (filledWith.LongLength == 0) filledWith = null;
                if (filledWith.LongLength > size) throw new ArgumentException("Length of filledWith array cannot be larger than desired buffer size");

                //TODO: Write test that will test that IBuffer.FillWith() doesn't throw an exception (and that buffers aren't allocated) in this method
            }

            if (size == 0)
            {
                //Return an empty buffer
                return new ManagedBuffer(firstSlab);
            }

            IMemoryBlock allocatedBlock;
            IMemorySlab[] slabArr;

            if (GetSingleSlabPool())
            {
                //Optimization: Chances are that there'll be just one slab in a pool, so access it directly 
                //and avoid the lock statement involved while creating an array of slabs.

                //Note that even if singleSlabPool is inaccurate, this method will still work properly.
                //The optimization is effective because singleSlabPool will be accurate majority of the time.

                slabArr = new IMemorySlab[] { firstSlab };
                if (TryAllocateBlockInSlabs(size, slabArr, out allocatedBlock))
                {
                    var buffer = new ManagedBuffer(allocatedBlock);
                    if (filledWith != null) buffer.FillWith(filledWith);
                    return buffer;
                }

                SetSingleSlabPool(false); // Slab count will soon be incremented
            }
            else
            {

                lock (syncSlabList)
                {
                    slabArr = slabs.ToArray();
                }

                if (TryAllocateBlockInSlabs(size, slabArr, out allocatedBlock))
                {
                    var buffer = new ManagedBuffer(allocatedBlock);
                    if (filledWith != null) buffer.FillWith(filledWith);
                    return buffer;
                }
            }


            //Try to create new slab
            lock (syncNewSlab)
            {
                //Look again for free block
                lock (syncSlabList)
                {
                    slabArr = slabs.ToArray();
                }

                if (TryAllocateBlockInSlabs(size, slabArr, out allocatedBlock))
                {
                    //found it -- leave
                    return new ManagedBuffer(allocatedBlock);
                }

                //Unable to find available free space, so create new slab
                MemorySlab newSlab = new MemorySlab(slabSize, this);

                newSlab.TryAllocate(size, out allocatedBlock);

                lock (syncSlabList)
                {
                    //Add new Slab to collection
                    slabs.Add(newSlab);

                    //Add extra slabs as requested in object properties
                    for (int i = 0; i < subsequentSlabs - 1; i++)
                    {
                        slabs.Add(new MemorySlab(slabSize, this));
                    }
                }

            }

            var newBuffer = new ManagedBuffer(allocatedBlock);
            if (filledWith != null) newBuffer.FillWith(filledWith);
            return newBuffer;
        }

        /// <summary>
        /// Searches for empty slabs and frees one if there are more than InitialSlabs number of slabs.
        /// </summary>
        internal void TryFreeSlabs()
        {
            lock (syncSlabList)
            {
                int emptySlabsCount = 0;
                int lastemptySlab = -1;
                for (int i = 0; i < slabs.Count; i++)
                {
                    if (slabs[i].LargestFreeBlockSize == slabSize)
                    {
                        emptySlabsCount++;
                        lastemptySlab = i;
                    }
                }

                
                if (emptySlabsCount > InitialSlabs) //There should be at least 1+initial slabs empty slabs before one is removed
                {
                    //TODO: MULTI-SLAB: Consider freeing all free slabs that exceed the initial slabs count
                    //'cos a buffer can span several slabs and can actually free multiple slabs instanttly.

                    //remove the last empty one
                    slabs.RemoveAt(lastemptySlab);

                    if (slabs.Count == 1) SetSingleSlabPool(true);
                }

            }
        }

        /// <summary>
        /// Helper method that searches for free block in an array of slabs and returns the allocated block
        /// </summary>
        /// <param name="length">Requested length of memory block</param>
        /// <param name="slabs">Array of slabs to search</param>
        /// <param name="allocatedBlock">Allocated memory block</param>
        /// <returns>True if memory block was successfully allocated. False, if otherwise</returns>
        private static bool TryAllocateBlockInSlabs(long length, IMemorySlab[] slabs, out IMemoryBlock allocatedBlock)
        {
            allocatedBlock = null;
            for (int i = 0; i < slabs.Length; i++)
            {
                if (slabs[i].LargestFreeBlockSize >= length)
                {
                    if (slabs[i].TryAllocate(length, out allocatedBlock))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
