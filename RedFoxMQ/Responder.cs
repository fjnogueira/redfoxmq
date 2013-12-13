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
using System.Linq;

namespace RedFoxMQ
{
    public class Responder : IResponder
    {
        private static readonly SocketAccepterFactory SocketAccepterFactory = new SocketAccepterFactory();
        private static readonly MessageFrameCreator MessageFrameCreator = new MessageFrameCreator();

        private readonly ConcurrentDictionary<RedFoxEndpoint, ISocketAccepter> _servers;
        private readonly ConcurrentDictionary<MessageReceiveLoop, MessageQueue> _clientSockets;
        private readonly MessageQueueProcessor _messageQueueProcessor = new MessageQueueProcessor();
        private readonly IResponderWorkUnitFactory _responderWorkUnitFactory;
        private readonly ResponderWorkerScheduler _scheduler;

        public Responder(IResponderWorkUnitFactory responderWorkUnitFactory, int minThreads = 1, int maxThreads = 1)
        {
            if (responderWorkUnitFactory == null) throw new ArgumentNullException("responderWorkUnitFactory");
            _responderWorkUnitFactory = responderWorkUnitFactory;

            _servers = new ConcurrentDictionary<RedFoxEndpoint, ISocketAccepter>();
            _clientSockets = new ConcurrentDictionary<MessageReceiveLoop, MessageQueue>();
            _scheduler = new ResponderWorkerScheduler(minThreads, maxThreads);
            _scheduler.WorkUnitCompleted += SchedulerWorkUnitCompleted;
        }

        public event Action<ISocket> ClientConnected = s => { };
        public event Action<ISocket> ClientDisconnected = s => { };

        public void Bind(RedFoxEndpoint endpoint)
        {
            var server = SocketAccepterFactory.CreateAndBind(endpoint, SocketMode.ReadWrite, OnClientConnected);
            _servers[endpoint] = server;
        }

        private void OnClientConnected(ISocket socket)
        {
            if (socket == null) throw new ArgumentNullException("socket");

            var messageFrameSender = new MessageFrameSender(socket);
            var messageQueue = new MessageQueue();
            var messageReceiveLoop = new MessageReceiveLoop(socket);
            messageReceiveLoop.MessageReceived += m => MessageReceivedProcessMessage(m, messageQueue);
            messageReceiveLoop.MessageDeserializationError += (s, e) => s.Disconnect(); // TODO: log error
            messageReceiveLoop.SocketError += (s, e) => s.Disconnect(); // TODO: log error
            messageReceiveLoop.Start();

            socket.Disconnected += () => SocketDisconnected(socket, messageReceiveLoop);

            if (_clientSockets.TryAdd(messageReceiveLoop, messageQueue))
            {
                ClientConnected(socket);
                _messageQueueProcessor.Register(messageQueue, messageFrameSender);
            }

            if (socket.IsDisconnected)
            {
                // this is to fix the race condition if socket was disconnected meanwhile
                SocketDisconnected(socket, messageReceiveLoop);
            }
        }

        private void SocketDisconnected(ISocket socket, MessageReceiveLoop receiveLoop)
        {
            MessageQueue messageQueue;
            if (_clientSockets.TryRemove(receiveLoop, out messageQueue))
            {
                _messageQueueProcessor.Unregister(messageQueue);
                ClientDisconnected(socket);
            }
        }

        private void MessageReceivedProcessMessage(IMessage requestMessage, MessageQueue messageQueue)
        {
            if (requestMessage == null) throw new ArgumentNullException("requestMessage");
            if (messageQueue == null) throw new ArgumentNullException("messageQueue");

            var workUnit = _responderWorkUnitFactory.CreateWorkUnit(requestMessage);
            _scheduler.AddWorkUnit(workUnit, messageQueue);
        }

        private void SchedulerWorkUnitCompleted(IResponderWorkUnit workUnit, object state, IMessage responseMessage)
        {
            var messageQueue = (MessageQueue) state;
            var responseFrame = MessageFrameCreator.CreateFromMessage(responseMessage);
            messageQueue.Add(responseFrame);
            // this is redundant
            // messageQueue.SendFromQueue();
        }

        public bool Unbind(RedFoxEndpoint endpoint)
        {
            ISocketAccepter removedServer;
            var serverRemoved = _servers.TryRemove(endpoint, out removedServer);
            if (serverRemoved) removedServer.Unbind();

            return serverRemoved;
        }

        private void UnbindAllEndpoints()
        {
            try
            {
                var endpoints = _servers.Keys.ToList();

                foreach (var endpoint in endpoints)
                {
                    Unbind(endpoint);
                }
            }
            catch (InvalidOperationException) { }
        }

        #region Dispose
        private bool _disposed;
        private readonly object _disposeLock = new object();

        protected virtual void Dispose(bool disposing)
        {
            lock (_disposeLock)
            {
                if (!_disposed)
                {
                    UnbindAllEndpoints();

                    _disposed = true;
                    if (disposing) GC.SuppressFinalize(this);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        ~Responder()
        {
            Dispose(false);
        }
        #endregion
    }
}
