using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotificationManager.Tasks
{
    public enum MessageType
    {
        Text,
        ClientMethodInvocation,
        ConnectionEvent
    }

    public sealed class Message
    {
        public MessageType messageType { get; set; }
        public string data { get; set; }
    }
}
