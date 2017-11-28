using DiagnosticsHelper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NotificationManager.Tasks
{
    public delegate void HandlerEvent(IList<object> obj);

    public sealed class InvocationDescriptor
    {
        public string methodName { get; set; }
        public IList<object> arguments { get; set; }
    }

    public sealed class InvocationHandler
    {
        public HandlerEvent handler { get; set; }
        public IList<Type> parameterTypes { get; set; }

        public InvocationHandler(HandlerEvent handler, IList<Type> parameterTypes)
        {
            this.handler = handler;
            this.parameterTypes = parameterTypes;
        }
    }

    public sealed class InvocationManager
    {
        private Dictionary<string, InvocationHandler> handlers = new Dictionary<string, InvocationHandler>();

        public void On(string methodName, HandlerEvent handler)
        {
            if (handlers.ContainsKey(methodName))
                return;

            var invocationHandler = new InvocationHandler(handler, new Type[] { });
            handlers.Add(methodName, invocationHandler);
        }

        public void ReceiveMessage(Message message)
        {
            switch (message.messageType)
            {
                case MessageType.Text:
                case MessageType.ConnectionEvent:
                    Diag.DebugPrint($"Received message: {message.data}");
                    break;
                case MessageType.ClientMethodInvocation:
                    var descriptor = JsonConvert.DeserializeObject<InvocationDescriptor>(message.data);
                    Invoke(descriptor);
                    break;
            }
        }

        private void Invoke(InvocationDescriptor invocationDescriptor)
        {
            var handlerRegistered = handlers.ContainsKey(invocationDescriptor.methodName);
            if (handlerRegistered)
                handlers[invocationDescriptor.methodName].handler(invocationDescriptor.arguments);
        }
    }
}
