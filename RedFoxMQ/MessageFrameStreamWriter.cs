﻿// 
// Copyright 2013 Hans Wolff
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// 
using RedFoxMQ.Transports;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RedFoxMQ
{
    class MessageFrameStreamWriter
    {
        private static readonly ConcurrentQueue<WeakReference<MemoryStream>> RecycledMemoryStreams = new ConcurrentQueue<WeakReference<MemoryStream>>();

        public void WriteMessageFrame(ISocket socket, MessageFrame messageFrame)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (messageFrame == null) throw new ArgumentNullException("messageFrame");
            if (messageFrame.RawMessage == null) throw new ArgumentException("messageFrame.RawMessage cannot be null");

            CreateBufferWriteSingle(socket, messageFrame);
        }

        public async Task WriteMessageFrameAsync(ISocket socket, MessageFrame messageFrame, CancellationToken cancellationToken)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (messageFrame == null) throw new ArgumentNullException("messageFrame");
            if (messageFrame.RawMessage == null) throw new ArgumentException("messageFrame.RawMessage cannot be null");

            await CreateBufferWriteSingleAsync(socket, messageFrame, cancellationToken);
        }

        public void WriteMessageFrames(ISocket socket, ICollection<MessageFrame> messageFrames)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (messageFrames == null) return;

            CreateBufferWriteMany(socket, messageFrames);
        }

        public async Task WriteMessageFramesAsync(ISocket socket, ICollection<MessageFrame> messageFrames, CancellationToken cancellationToken)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (messageFrames == null) return;

            await CreateBufferWriteManyAsync(socket, messageFrames, cancellationToken);
        }

        private static void CreateBufferWriteSingle(ISocket socket, MessageFrame messageFrame)
        {
            var sendBufferSize = MessageFrame.HeaderSize + messageFrame.RawMessage.Length;

            WeakReference<MemoryStream> reference;
            var mem = GetOrCreateMemoryStream(sendBufferSize, out reference);

            try
            {
                WriteTypeId(mem, messageFrame.MessageTypeId);
                WriteLength(mem, messageFrame.RawMessage.Length);
                WriteBody(mem, messageFrame.RawMessage);

                var toSend = mem.GetBuffer();
                socket.Write(toSend, 0, sendBufferSize);
            }
            finally
            {
                mem.SetLength(0);
                RecycledMemoryStreams.Enqueue(reference);
            }
        }

        private static async Task CreateBufferWriteSingleAsync(ISocket socket, MessageFrame messageFrame,
            CancellationToken cancellationToken)
        {
            var sendBufferSize = MessageFrame.HeaderSize + messageFrame.RawMessage.Length;

            WeakReference<MemoryStream> reference;
            var mem = GetOrCreateMemoryStream(sendBufferSize, out reference);

            try
            {
                WriteTypeId(mem, messageFrame.MessageTypeId);
                WriteLength(mem, messageFrame.RawMessage.Length);
                WriteBody(mem, messageFrame.RawMessage);

                var toSend = mem.GetBuffer();
                await socket.WriteAsync(toSend, 0, sendBufferSize, cancellationToken);
            }
            finally
            {
                mem.SetLength(0);
                RecycledMemoryStreams.Enqueue(reference);
            }
        }

        private static MemoryStream GetOrCreateMemoryStream(int sendBufferSize, out WeakReference<MemoryStream> reference)
        {
            MemoryStream mem;
            if (!RecycledMemoryStreams.TryDequeue(out reference))
            {
                mem = new MemoryStream(sendBufferSize);
                reference = new WeakReference<MemoryStream>(mem);
            }
            else
            {
                if (!reference.TryGetTarget(out mem))
                {
                    mem = new MemoryStream(sendBufferSize);
                    reference.SetTarget(mem);
                }
            }
            return mem;
        }

        private static void WriteTypeId(Stream stream, ushort messageTypeId)
        {
            var bytes = BitConverter.GetBytes(messageTypeId);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteLength(Stream stream, int length)
        {
            var bytes = BitConverter.GetBytes(length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteBody(Stream stream, byte[] rawMessage)
        {
            stream.Write(rawMessage, 0, rawMessage.Length);
        }

        private static void CreateBufferWriteMany(ISocket socket, ICollection<MessageFrame> messageFrames)
        {
            if (messageFrames == null) return;
            var sendBufferSize = messageFrames.Count * MessageFrame.HeaderSize + messageFrames.Sum(m => m.RawMessage.Length);

            WeakReference<MemoryStream> reference;
            var mem = GetOrCreateMemoryStream(sendBufferSize, out reference);

            try
            {
                foreach (var messageFrame in messageFrames)
                {
                    WriteTypeId(mem, messageFrame.MessageTypeId);
                    WriteLength(mem, messageFrame.RawMessage.Length);
                    WriteBody(mem, messageFrame.RawMessage);
                }

                var toSend = mem.GetBuffer();
                socket.Write(toSend, 0, sendBufferSize);
            }
            finally
            {
                mem.SetLength(0);
                RecycledMemoryStreams.Enqueue(reference);
            }
        }

        private static async Task CreateBufferWriteManyAsync(ISocket socket, ICollection<MessageFrame> messageFrames,
            CancellationToken cancellationToken)
        {
            if (messageFrames == null) return;
            var sendBufferSize = messageFrames.Count * MessageFrame.HeaderSize + messageFrames.Sum(m => m.RawMessage.Length);

            WeakReference<MemoryStream> reference;
            var mem = GetOrCreateMemoryStream(sendBufferSize, out reference);
            
            try
            {
                foreach (var messageFrame in messageFrames)
                {
                    WriteTypeId(mem, messageFrame.MessageTypeId);
                    WriteLength(mem, messageFrame.RawMessage.Length);
                    WriteBody(mem, messageFrame.RawMessage);
                }

                var toSend = mem.GetBuffer();
                await socket.WriteAsync(toSend, 0, sendBufferSize, cancellationToken);
            }
            finally
            {
                mem.SetLength(0);
                RecycledMemoryStreams.Enqueue(reference);
            }
        }
    }
}
