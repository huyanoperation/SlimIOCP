﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;

namespace SlimIOCP
{
    internal class Receiver : BaseReceiver
    {
        readonly Peer peer;

        internal Receiver(Peer peer)
        {
            this.peer = peer;
        }

        internal override void ReceiveLoop()
        {
            Queue<IncomingBuffer> queue = null;

            while (true)
            {
                lock (peer.IncomingBufferQueueSync)
                {
                    if (peer.IncomingBufferQueue.Count > 0)
                    {
                        queue = peer.IncomingBufferQueue;

                        if (!peer.IncomingBufferQueuePool.TryPop(out peer.IncomingBufferQueue))
                        {
                            //TODO: Error
                        }
                    }
                }

                if (queue != null)
                {
                    while (queue.Count > 0)
                    {
                        var buffer = queue.Dequeue();
                        var bufferHandle = buffer.BufferHandle;
                        var bufferOffset = buffer.BufferOffset;
                        var bufferLength = buffer.AsyncArgs.BytesTransferred;

                        var connection = buffer.Connection;

                        while (bufferLength > 0)
                        {
                            if (connection.Message == null)
                            {
                                if (!peer.IncomingMessagePool.TryPop(out connection.Message))
                                {
                                    //TODO: Error
                                }
                            }

                            connection.Message = Receive(connection.Message, bufferHandle, ref bufferOffset, ref bufferLength);

                            if (connection.Message.IsDone)
                            {
                                /*
                                lock (connection.ReceiveQueue)
                                {
                                    //connection.ReceiveQueue.Enqueue(connection.Message);
                                }
                                */

                                // Queue into received messages
                                lock (peer.ReceivedMessages)
                                {
                                    peer.ReceivedMessages.Enqueue(connection.Message);
                                }

                                // Signal wait event
                                peer.ReceivedMessageEvent.Set();

                                // Clear message on connection
                                connection.Message = null;
                            }
                        }

                        if (!peer.IncomingBufferPool.TryPush(buffer))
                        {
                            //TODO: Error
                        }
                    }

                    if (!peer.IncomingBufferQueuePool.TryPush(queue))
                    {
                        //TODO: Error
                    }
                }

                peer.ReceiverEvent.Reset();

                if (peer.IncomingBufferQueue.Count == 0)
                {
                    peer.ReceiverEvent.WaitOne();
                }
            }
        }
    }
}
