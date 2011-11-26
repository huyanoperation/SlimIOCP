﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace SlimIOCP
{
    public class Peer : BasePeer
    {
        internal Queue<IncomingBuffer> IncomingBufferQueue;
        internal readonly QueuePool<IncomingBuffer> IncomingBufferQueuePool;

        internal readonly object IncomingBufferQueueSync = new object();

        internal readonly Queue<IncomingMessage> ReceivedMessages;

        internal readonly MessageBufferPool<IncomingBuffer> IncomingBufferPool;
        internal readonly MessageBufferPool<IncomingMessage> IncomingMessagePool;
        internal readonly MessageBufferPool<OutgoingMessage> OutgoingMessagePool;

        internal Peer() : base()
        {
            Receiver = new Receiver(this);
            ReceivedMessages = new Queue<IncomingMessage>();

            IncomingBufferPool = new MessageBufferPool<IncomingBuffer>(new IncomingBufferProducer(this));
            IncomingMessagePool = new MessageBufferPool<IncomingMessage>(new IncomingMessageProducer());
            OutgoingMessagePool = new MessageBufferPool<OutgoingMessage>(new OutgoingMessageProducer(this));

            IncomingBufferQueue = new Queue<IncomingBuffer>();
            IncomingBufferQueuePool = new QueuePool<IncomingBuffer>(32);
        }

        public override bool TryRecycleMessage(IncomingMessage message)
        {
            lock (IncomingMessagePool)
            {
                return IncomingMessagePool.TryPush(message);
            }
        }

        public override bool TryGetMessage(out IncomingMessage message)
        {
            lock (ReceivedMessages)
            {
                if (ReceivedMessages.Count > 0)
                {
                    message = ReceivedMessages.Dequeue();
                    return true;
                }
            }

            ReceivedMessageEvent.Reset();

            message = null;
            return false;
        }

        internal void OnComplete(object sender, SocketAsyncEventArgs asyncArgs)
        {
            switch (asyncArgs.LastOperation)
            {
                case SocketAsyncOperation.Send:
                    OnSendComplete((OutgoingMessage)asyncArgs.UserToken);
                    break;

                case SocketAsyncOperation.Receive:
                    OnReceiveComplete((IncomingBuffer)asyncArgs.UserToken);
                    break;

                default:
                    //TODO: Error
                    throw new Exception("Not send or receive");
            }
        }

        internal void ReceiveAsync(Connection connection)
        {
            IncomingBuffer buffer;

            if (IncomingBufferPool.TryPop(out buffer))
            {
                buffer.Connection = connection;

                var isDone = !connection.Socket.ReceiveAsync(buffer.AsyncArgs);
                if (isDone)
                {
                    OnReceiveComplete(buffer);
                }
            }
            else
            {
                //TODO: Error
            }
        }

        internal void SendAsync(OutgoingMessage message)
        {
            if (!message.Connection.Sending)
            {
                lock (message.Connection)
                {
                    message.Connection.Sending = true;
                }
            }

            // Common case
            if (message.SendDataBuffer == null)
            {
                if (message.SendDataBytesSent != 0)
                {
                    message.AsyncArgs.SetBuffer(message.BufferOffset + message.SendDataBytesSent, message.BufferSize - message.SendDataBytesSent);
                }
            }
            else
            {
                var dataOffset = message.SendDataOffset + message.SendDataBytesSent;
                var sendLength = Math.Min(message.SendDataBytesRemaining, message.BufferSize);
                message.AsyncArgs.SetBuffer(message.BufferOffset, sendLength);
                System.Buffer.BlockCopy(message.SendDataBuffer, dataOffset, message.BufferHandle, message.BufferOffset, sendLength);
            }

            var isDone = !message.Connection.Socket.SendAsync(message.AsyncArgs);
            if (isDone)
            {
                OnSendComplete(message);
            }
        }

        void OnReceiveComplete(IncomingBuffer buffer)
        {
            if (buffer.AsyncArgs.BytesTransferred == 0)
            {
                IncomingBufferPool.TryPush(buffer);
                return;
            }

            var connection = buffer.Connection;

            lock (IncomingBufferQueueSync)
            {
                IncomingBufferQueue.Enqueue(buffer);
            }

            ReceiverEvent.Set();
            ReceiveAsync(connection);
        }

        void OnSendComplete(OutgoingMessage message)
        {
            if (message.AsyncArgs.SocketError != SocketError.Success)
            {
                return;
            }

            message.SendDataBytesRemaining -= message.AsyncArgs.BytesTransferred;
            message.SendDataBytesSent += message.AsyncArgs.BytesTransferred;

            if (message.SendDataBytesRemaining > 0)
            {
                SendAsync(message);
            }
            else
            {
                var connection = message.Connection;

                if (!OutgoingMessagePool.TryPush(message))
                {
                    //TODO: Error
                }

                lock (connection)
                {
                    if (connection.SendQueue.Count > 0)
                    {
                        SendAsync(connection.SendQueue.Dequeue());
                    }
                    else
                    {
                        connection.Sending = false;
                    }
                }
            }
        }
    }
}
