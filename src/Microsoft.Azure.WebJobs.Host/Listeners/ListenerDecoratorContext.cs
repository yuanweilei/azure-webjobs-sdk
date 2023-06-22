﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    /// <summary>
    /// Context class for <see cref="IListenerDecorator.Decorate(ListenerDecoratorContext)"/>.
    /// </summary>
    public class ListenerDecoratorContext
    {
        /// <summary>
        /// Constructs an instances.
        /// </summary>
        /// <param name="functionDefinition">The function the specified listener is for.</param>
        /// <param name="listener">The listener to decorate.</param>
        public ListenerDecoratorContext(IFunctionDefinition functionDefinition, IListener listener) 
        {
            FunctionDefinition = functionDefinition;
            Listener = listener;
        }

        /// <summary>
        /// Gets the <see cref="IFunctionDefinition"/> the specified listener is for.
        /// </summary>
        public IFunctionDefinition FunctionDefinition { get; }

        /// <summary>
        /// Gets the listener to decorate.
        /// </summary>
        public IListener Listener { get; }
    }
}
