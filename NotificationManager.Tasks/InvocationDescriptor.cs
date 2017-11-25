using System;
using System.Collections.Generic;

namespace NotificationManager.Tasks
{
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

    public delegate void HandlerEvent(IList<object> obj);
}
