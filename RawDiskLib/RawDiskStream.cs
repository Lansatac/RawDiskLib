﻿using System;
using System.Diagnostics;
using System.IO;

namespace RawDiskLib
{
    public class RawDiskStream : Stream
    {
        private readonly FileStream _diskStream;
        private readonly int _smallestChunkSize;

        internal RawDiskStream(FileStream diskStream, int smallestChunkSize)
        {
            _diskStream = diskStream;
            _smallestChunkSize = smallestChunkSize;
        }

        public override void Flush()
        {
            _diskStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = Position;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPosition = offset;
                    break;
                case SeekOrigin.Current:
                    newPosition += offset;
                    break;
                case SeekOrigin.End:
                    newPosition = Length - offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("origin");
            }

            // Is it valid?
            if (0 > newPosition || newPosition > Length)
                throw new ArgumentOutOfRangeException("Out of bounds");

            // Valid
            Position = newPosition;

            // Position disk stream
            long diskOffset = Position - Position % _smallestChunkSize; // Align to a multiple of '_smallestChunkSize'

            Debug.Assert(diskOffset % _smallestChunkSize == 0);

            _diskStream.Seek(diskOffset, SeekOrigin.Begin);

            return Position;
        }

        public override void SetLength(long value)
        {
            _diskStream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long chunk = Position / _smallestChunkSize;
            int chunks = count / _smallestChunkSize + (Position % _smallestChunkSize == 0 ? 0 : 1);

            // Seek
            long diskOffset = chunk * _smallestChunkSize;
            if (diskOffset != _diskStream.Position)
                _diskStream.Seek(diskOffset, SeekOrigin.Begin);

            // Read sectors
            if (Position % _smallestChunkSize == 0 && count % _smallestChunkSize == 0)
            {
                // Read directly into target buffer
                _diskStream.Read(buffer, offset, count);
            }
            else
            {
                // Do a temporary buffer
                byte[] data = new byte[chunks * _smallestChunkSize];
                _diskStream.Read(data, 0, data.Length);

                Array.Copy(data, Position % _smallestChunkSize, buffer, offset, count);
            }

            Position += count;
            return count;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            long chunk = Position / _smallestChunkSize;
            int chunks = count / _smallestChunkSize + (Position % _smallestChunkSize == 0 ? 0 : 1);

            // Write sectors
            if (Position % _smallestChunkSize == 0 && count % _smallestChunkSize == 0)
            {
                // Seek
                long diskOffset = chunk * _smallestChunkSize;
                if (diskOffset != _diskStream.Position)
                    _diskStream.Seek(diskOffset, SeekOrigin.Begin);

                // Write directly into stream
                _diskStream.Write(buffer, offset, count);
            }
            else
            {
                // Do copy-on write
                byte[] tmpBuff = new byte[_smallestChunkSize];

                int firstChunkLength = _smallestChunkSize - (int)(Position % _smallestChunkSize);
                int middleChunksLength = ((count - firstChunkLength) / _smallestChunkSize) * _smallestChunkSize;        // This will ensure that 'middleChunksLength' is a multiple of '_smallestChunkSize'
                int lastChunkLength = count - middleChunksLength - firstChunkLength;

                Debug.Assert(0 <= firstChunkLength && firstChunkLength < _smallestChunkSize);
                Debug.Assert(middleChunksLength % _smallestChunkSize == 0);
                Debug.Assert(0 <= lastChunkLength && lastChunkLength < _smallestChunkSize);

                // Seek
                _diskStream.Seek(chunk * _smallestChunkSize, SeekOrigin.Begin);

                // == First chunk ==
                if (firstChunkLength > 0)
                {
                    // Do copy-on write
                    _diskStream.Read(tmpBuff, 0, tmpBuff.Length);

                    Array.Copy(buffer, offset, tmpBuff, tmpBuff.Length - firstChunkLength, firstChunkLength);

                    _diskStream.Seek(-tmpBuff.Length, SeekOrigin.Current);
                    _diskStream.Write(tmpBuff, 0, tmpBuff.Length);
                }

                // == Middle chunks ==
                if (middleChunksLength > 0)
                {
                    // Write directly
                    _diskStream.Write(buffer, offset + firstChunkLength, middleChunksLength);
                }

                // == Last chunk ==
                if (lastChunkLength > 0)
                {
                    // Do copy-on write
                    _diskStream.Read(tmpBuff, 0, tmpBuff.Length);

                    Array.Copy(buffer, offset + firstChunkLength + middleChunksLength, tmpBuff, 0, lastChunkLength);

                    _diskStream.Seek(-tmpBuff.Length, SeekOrigin.Current);
                    _diskStream.Write(tmpBuff, 0, tmpBuff.Length);
                }
            }

            Position += count;
        }

        public override bool CanRead
        {
            get { return _diskStream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _diskStream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _diskStream.CanWrite; }
        }

        public override long Length
        {
            get { return _diskStream.Length; }
        }

        public override long Position { get; set; }
    }
}