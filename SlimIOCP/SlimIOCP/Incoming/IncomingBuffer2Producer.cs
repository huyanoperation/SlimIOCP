﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace SlimIOCP
{
    internal class IncomingBuffer2Producer : MessageBufferProducer<IncomingBuffer2>
    {
        readonly Peer peer;

        public IncomingBuffer2Producer(Peer peer)
        {
            this.peer = peer;
        }

        protected override IncomingBuffer2 Create()
        {
            var asyncArgs = new SocketAsyncEventArgs();
            var buffer = new IncomingBuffer2(peer, asyncArgs);

            asyncArgs.UserToken = buffer;
            asyncArgs.Completed += new EventHandler<SocketAsyncEventArgs>(peer.OnComplete);

            return buffer;
        }
    }
}