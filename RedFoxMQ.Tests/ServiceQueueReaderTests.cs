﻿// 
// Copyright 2013-2014 Hans Wolff
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

using NUnit.Framework;
using RedFoxMQ.Transports;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RedFoxMQ.Tests
{
    [TestFixture]
    public class ServiceQueueReaderTests
    {
        [TestCase(ServiceQueueRotationAlgorithm.FirstIdle)]
        [TestCase(ServiceQueueRotationAlgorithm.LoadBalance)]
        public void ServiceQueue_connect_AddMessageFrame_single_message_received(ServiceQueueRotationAlgorithm rotationAlgorithm)
        {
            var endpoint = new RedFoxEndpoint("/path");

            using (var serviceQueue = new ServiceQueue(rotationAlgorithm))
            using (var serviceQueueReader = new ServiceQueueReader())
            {
                IMessage messageReceived = null;
                var received = new ManualResetEventSlim();
                serviceQueueReader.MessageReceived += m =>
                {
                    messageReceived = m;
                    received.Set();
                };

                serviceQueue.Bind(endpoint);
                serviceQueueReader.Connect(endpoint);

                var testMessage = new TestMessage();
                var testMessageFrame = new MessageFrameCreator(DefaultMessageSerialization.Instance).CreateFromMessage(testMessage);

                serviceQueue.AddMessageFrame(testMessageFrame);

                Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(1)));
                Assert.AreEqual(testMessage, messageReceived);
            }
        }

        [TestCase(ServiceQueueRotationAlgorithm.FirstIdle)]
        [TestCase(ServiceQueueRotationAlgorithm.LoadBalance)]
        public void ServiceQueue_AddMessageFrame_connect_single_message_received(ServiceQueueRotationAlgorithm rotationAlgorithm)
        {
            var endpoint = new RedFoxEndpoint("/path");

            using (var serviceQueue = new ServiceQueue(rotationAlgorithm))
            using (var serviceQueueReader = new ServiceQueueReader())
            {
                IMessage messageReceived = null;
                var received = new ManualResetEventSlim();
                serviceQueueReader.MessageReceived += m =>
                {
                    messageReceived = m;
                    received.Set();
                };

                var testMessage = new TestMessage();
                var testMessageFrame = new MessageFrameCreator(DefaultMessageSerialization.Instance).CreateFromMessage(testMessage);

                serviceQueue.AddMessageFrame(testMessageFrame);

                serviceQueue.Bind(endpoint);
                serviceQueueReader.Connect(endpoint);

                Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(1)));
                Assert.AreEqual(testMessage, messageReceived);
            }
        }

        [TestCase(ServiceQueueRotationAlgorithm.FirstIdle)]
        [TestCase(ServiceQueueRotationAlgorithm.LoadBalance)]
        public void ServiceQueue_connect_AddMessageFrame_single_message_received_disconnect_connect_AddMessageFrame_single_message_received(ServiceQueueRotationAlgorithm rotationAlgorithm)
        {
            var endpoint = new RedFoxEndpoint("/path");

            using (var serviceQueue = new ServiceQueue(rotationAlgorithm))
            using (var serviceQueueReader = new ServiceQueueReader())
            {
                IMessage messageReceived = null;
                var received = new ManualResetEventSlim();
                serviceQueueReader.MessageReceived += m =>
                {
                    messageReceived = m;
                    received.Set();
                };

                var testMessage = new TestMessage();
                var testMessageFrame = new MessageFrameCreator(DefaultMessageSerialization.Instance).CreateFromMessage(testMessage);

                serviceQueue.Bind(endpoint);
                serviceQueueReader.Connect(endpoint);

                serviceQueue.AddMessageFrame(testMessageFrame);

                Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(1)));
                Assert.AreEqual(testMessage, messageReceived);

                serviceQueueReader.Disconnect();

                messageReceived = null;
                received.Reset();

                serviceQueueReader.Connect(endpoint);

                serviceQueue.AddMessageFrame(testMessageFrame);

                Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(1)));
                Assert.AreEqual(testMessage, messageReceived);
            }
        }

        [TestCase(ServiceQueueRotationAlgorithm.FirstIdle)]
        [TestCase(ServiceQueueRotationAlgorithm.LoadBalance)]
        public void ServiceQueue_AddMessageFrame_connect_multiple_readers_multiple_message_received(ServiceQueueRotationAlgorithm rotationAlgorithm)
        {
            var endpoint = new RedFoxEndpoint("/path");

            using (var serviceQueue = new ServiceQueue(rotationAlgorithm))
            using (var serviceQueueReader1 = new ServiceQueueReader())
            using (var serviceQueueReader2 = new ServiceQueueReader())
            {
                const int count = 1000;
                var counter = new CounterSignal(count, 0);

                var messagesReceived1 = new List<IMessage>();
                serviceQueueReader1.MessageReceived += m =>
                {
                    messagesReceived1.Add(m);
                    counter.Increment();
                };

                var messagesReceived2 = new List<IMessage>();
                serviceQueueReader2.MessageReceived += m =>
                {
                    messagesReceived2.Add(m);
                    counter.Increment();
                };

                var testMessage = new TestMessage();
                var testMessageFrame = new MessageFrameCreator(DefaultMessageSerialization.Instance).CreateFromMessage(testMessage);

                for (var i = 0; i < count; i++)
                    serviceQueue.AddMessageFrame(testMessageFrame);

                serviceQueue.Bind(endpoint);

                serviceQueueReader1.Connect(endpoint);
                serviceQueueReader2.Connect(endpoint);

                Assert.IsTrue(counter.Wait(TimeSpan.FromSeconds(10)));
                Assert.AreEqual(count, messagesReceived1.Count + messagesReceived2.Count);
            }
        }

        [Test]
        public void ServiceQueue_AddMessageFrame_connect_multiple_readers_LoadBalance()
        {
            var endpoint1 = new RedFoxEndpoint("/path1");
            var endpoint2 = new RedFoxEndpoint("/path2");

            using (var serviceQueue = new ServiceQueue(ServiceQueueRotationAlgorithm.LoadBalance))
            using (var serviceQueueReader1 = new ServiceQueueReader())
            using (var serviceQueueReader2 = new ServiceQueueReader())
            {
                const int count = 1000;
                var counter = new CounterSignal(count, 0);

                var messagesReceived1 = new List<IMessage>();
                serviceQueueReader1.MessageReceived += m =>
                {
                    messagesReceived1.Add(m);
                    counter.Increment();
                };

                var messagesReceived2 = new List<IMessage>();
                serviceQueueReader2.MessageReceived += m =>
                {
                    messagesReceived2.Add(m);
                    counter.Increment();
                };

                var testMessage = new TestMessage();
                var testMessageFrame = new MessageFrameCreator(DefaultMessageSerialization.Instance).CreateFromMessage(testMessage);

                serviceQueue.Bind(endpoint1);
                serviceQueue.Bind(endpoint2);

                serviceQueueReader1.Connect(endpoint1);
                serviceQueueReader2.Connect(endpoint2);

                for (var i = 0; i < count; i++)
                    serviceQueue.AddMessageFrame(testMessageFrame);

                Assert.IsTrue(counter.Wait());
                Assert.AreEqual(count, messagesReceived1.Count + messagesReceived2.Count);

                Assert.AreNotEqual(0, messagesReceived1.Count);
                Assert.AreNotEqual(0, messagesReceived2.Count);

                var ratio = (decimal)messagesReceived1.Count / messagesReceived2.Count;
                Assert.Greater(ratio, 0.25);
                Assert.Less(ratio, 0.75);
            }
        }

        [SetUp]
        public void Setup()
        {
            TestHelpers.InitializeMessageSerialization();
        }
    }
}