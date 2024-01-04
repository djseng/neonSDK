//-----------------------------------------------------------------------------
// FILE:        ServiceContainer.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Diagnostics;
using System.Collections;
using System.ComponentModel;

namespace Neon.Common
{
    /// <summary>
    /// This class combines the capabilities of a <see cref="IServiceCollection"/> and
    /// <see cref="IServiceProvider"/> into a single object that implements the
    /// combined <see cref="IServiceContainer"/> interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design of <see cref="IServiceCollection"/> and <see cref="IServiceProvider"/> seems
    /// somewhat limited.  This assumes that applications explicitly initialize a <see cref="IServiceCollection"/>
    /// instance during startup and then call <c>BuildServiceProvider()</c> to return the <see cref="IServiceProvider"/>
    /// that can actually be used to find a service at runtime.
    /// </para>
    /// <para>
    /// This works fine for lots of applications, but with a framework like Neon, it is
    /// useful to have a global service provider that allows the client to register
    /// default services for applications that are not coded to be aware of dependency
    /// injection.  The problem with the Microsoft DependencyInjection design is that
    /// additional services registered after a <c>BuildServiceProvider()</c> call will 
    /// not be returned by the service provider.
    /// </para>
    /// <para>
    /// This class combines both these capabilities into a single class such that 
    /// services can be registered and located dynamically without ever having to
    /// call <c>BuildServiceProvider()</c>.
    /// </para>
    /// <note>
    /// The <c>BuildServiceProvider()</c> extension methods still work the same and
    /// return only a point-in-time snapshot of the services.  You may not need to
    /// call these though, because you can call <see cref="GetService(Type)"/> directly.
    /// </note>
    /// </remarks>
    /// <threadsafety instance="true"/>
    public class ServiceContainer : IServiceContainer
    {
        private object                  syncRoot = new object();
        private ServiceCollection       services = new ServiceCollection();
        private ServiceProvider         provider = null;

        // Implementation Note:
        // --------------------
        // I'm going to use a monitor to protect the services collection and the
        // cached service provider and we'll rebuild the provider as necessary
        // after any changes to the services.  This should is clean and should
        // be relatively efficient for most common use cases.

        // $todo(jefflill)
        //
        // Using [syncRoot] to implement thread safety via a [Monitor] may introduce
        // some performance overhead for ASP.NET sites with lots of traffic.  It
        // may be worth investigating whether a [SpinLock] might be better or perhaps
        // even reimplementing this using a concurrent collections.

        /// <summary>
        /// Constructor.
        /// </summary>
        public ServiceContainer()
        {
        }

        /// <summary>
        /// Returns a clone of the instance.  This can be useful for unit testing to save
        /// the service container before executing a test and then restoring it afterwards.
        /// </summary>
        /// <returns>The cloned instance.</returns>
        public ServiceContainer Clone()
        {
            var clone = new ServiceContainer();

            foreach (var item in this)
            {
                clone.Add(item);
            }

            return clone;
        }

        //---------------------------------------------------------------------
        // IServiceCollection implementation.

        /// <inheritdoc/>
        public ServiceDescriptor this[int index]
        {
            get
            {
                lock (syncRoot)
                {
                    return services[index];
                }
            }

            set
            {
                lock (syncRoot)
                {
                    services[index] = value;
                    provider        = null;
                }
            }
        }

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                lock (syncRoot)
                {
                    return services.Count;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsReadOnly
        {
            get
            {
                lock (syncRoot)
                {
                    return services.IsReadOnly;
                }
            }
        }

        /// <inheritdoc/>
        public void Add(ServiceDescriptor item)
        {
            lock (syncRoot)
            {
                // Remove any existing descriptors with the same service type.
                // This allows the initialization of default services that 
                // may be overridden later.
                //
                // I believe there should only ever be one service with any
                // given service type in the collection, but ServicesCollection
                // doesn't seem to enforce this, so I'm going to check for and
                // remove multiple instances.

                var delList = new List<ServiceDescriptor>();

                foreach (var existing in services)
                {
                    if (existing.ServiceType == item.ServiceType)
                    {
                        delList.Add(existing);
                    }
                }

                foreach (var existing in delList)
                {
                    services.Remove(existing);
                }

                ((IList<ServiceDescriptor>)services).Add(item);
                provider = null;
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            lock (syncRoot)
            {
                services.Clear();
                provider = null;
            }
        }

        /// <inheritdoc/>
        public bool Contains(ServiceDescriptor item)
        {
            lock (syncRoot)
            {
                return services.Contains(item);
            }
        }

        /// <inheritdoc/>
        public void CopyTo(ServiceDescriptor[] array, int arrayIndex)
        {
            lock (syncRoot)
            {
                services.CopyTo(array, arrayIndex);
            }
        }

        /// <inheritdoc/>
        public IEnumerator<ServiceDescriptor> GetEnumerator()
        {
            lock (syncRoot)
            {
                var enumerator = services.GetEnumerator();
                var items      = new List<ServiceDescriptor>();

                while (enumerator.MoveNext())
                {
                    items.Add(enumerator.Current);
                }

                return items.GetEnumerator();
            }
        }

        /// <inheritdoc/>
        public void Insert(int index, ServiceDescriptor item)
        {
            lock (syncRoot)
            {
                services.Insert(index, item);
                provider = null;
            }
        }

        /// <inheritdoc/>
        public bool Remove(ServiceDescriptor item)
        {
            lock (syncRoot)
            {
                provider = null;

                return services.Remove(item);
            }
        }

        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            lock (syncRoot)
            {
                services.RemoveAt(index);
                provider = null;
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc/>
        public int IndexOf(ServiceDescriptor item)
        {
            lock (syncRoot)
            {
                return services.IndexOf(item);
            }
        }

        /// <summary>
        /// This is a convenience generic method that returns the service
        /// already cast to requested type.
        /// </summary>
        /// <typeparam name="TService">Specifies the desired service type.</typeparam>
        /// <returns>The service instance if it exists or <c>null</c>.</returns>
        public TService GetService<TService>()
        {
            return (TService)GetService(typeof(TService));
        }

        //---------------------------------------------------------------------
        // IServiceProvider implementation.

        /// <inheritdoc/>
        public object GetService(Type serviceType)
        {
            lock (syncRoot)
            {
                if (provider == null)
                {
                    provider = this.BuildServiceProvider();
                }

                return provider.GetService(serviceType);
            }
        }
    }
}
