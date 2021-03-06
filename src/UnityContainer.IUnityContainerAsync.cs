﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Builder;
using Unity.Exceptions;
using Unity.Injection;
using Unity.Lifetime;
using Unity.Registration;
using Unity.Resolution;
using Unity.Storage;

namespace Unity
{

    public partial class UnityContainer : IUnityContainerAsync
    {
        #region Registration

        #region Type

        /// <inheritdoc />
        Task IUnityContainerAsync.RegisterType(IEnumerable<Type>? interfaces, Type type, string? name, ITypeLifetimeManager? lifetimeManager, params InjectionMember[] injectionMembers)
        {
            return Task.Factory.StartNew((object status) =>
            {
                var types = status as Type[];

                Type? typeFrom = null;
                var registeredType = typeFrom ?? type;

                // Validate input
                //if (null == registeredType) throw new InvalidOperationException($"At least one of Type arguments '{nameof(typeFrom)}' or '{nameof(typeTo)}' must be not 'null'");

                try
                {
                    // Lifetime Manager
                    var manager = lifetimeManager as LifetimeManager ?? Context.TypeLifetimeManager.CreateLifetimePolicy();
                    if (manager.InUse) throw new InvalidOperationException(LifetimeManagerInUse);
                    manager.InUse = true;

                    // Create registration and add to appropriate storage
                    var container = manager is SingletonLifetimeManager ? _root : this;
                    Debug.Assert(null != container);

                    // If Disposable add to container's lifetime
                    if (manager is IDisposable disposableManager)
                        container.LifetimeContainer.Add(disposableManager);

                    // Add or replace existing 
                    var registration = new ExplicitRegistration(container, name, type, manager, injectionMembers);
                    var previous = container.Register(registeredType, name, registration);

                    // Allow reference adjustment and disposal
                    if (null != previous && 0 == previous.Release()
                        && previous.LifetimeManager is IDisposable disposable)
                    {
                        // Dispose replaced lifetime manager
                        container.LifetimeContainer.Remove(disposable);
                        disposable.Dispose();
                    }

                    // Add Injection Members
                    if (null != injectionMembers && injectionMembers.Length > 0)
                    {
                        foreach (var member in injectionMembers)
                        {
                            member.AddPolicies<BuilderContext, ExplicitRegistration>(
                                registeredType, type, name, ref registration);
                        }
                    }

                    // Check what strategies to run
                    registration.Processors = Context.TypePipelineCache;

                    // Raise event
                    //container.Registering?.Invoke(this, new RegisterEventArgs(registeredType,
                    //                                                          typeTo,
                    //                                                          name,
                    //                                                          manager));
                }
                catch (Exception ex)
                {
                    var builder = new StringBuilder();

                    builder.AppendLine(ex.Message);
                    builder.AppendLine();

                    var parts = new List<string>();
                    var generics = null == typeFrom ? type?.Name : $"{typeFrom?.Name},{type?.Name}";
                    if (null != name) parts.Add($" '{name}'");
                    if (null != lifetimeManager && !(lifetimeManager is TransientLifetimeManager)) parts.Add(lifetimeManager.ToString());
                    if (null != injectionMembers && 0 != injectionMembers.Length)
                        parts.Add(string.Join(" ,", injectionMembers.Select(m => m.ToString())));

                    builder.AppendLine($"  Error in:  RegisterType<{generics}>({string.Join(", ", parts)})");
                    throw new InvalidOperationException(builder.ToString(), ex);
                }
            }, ValidateTypes(interfaces, type));
        }

        #endregion


        #region Factory

        /// <inheritdoc />
        Task IUnityContainerAsync.RegisterFactory(IEnumerable<Type>? interfaces, string? name, Func<IUnityContainer, Type, string?, object?> factory, IFactoryLifetimeManager? lifetimeManager)
        {
            // Validate input
            if (null == interfaces) throw new ArgumentNullException(nameof(interfaces));
            if (null == factory) throw new ArgumentNullException(nameof(factory));

            return Task.Factory.StartNew(() =>
            {
                // TODO: implementation required
                var type = interfaces.First();

                // Lifetime Manager
                var manager = lifetimeManager as LifetimeManager ?? Context.FactoryLifetimeManager.CreateLifetimePolicy();
                if (manager.InUse) throw new InvalidOperationException(LifetimeManagerInUse);
                manager.InUse = true;

                // Target Container
                var container = manager is SingletonLifetimeManager ? _root : this;
                Debug.Assert(null != container);

                // If Disposable add to container's lifetime
                if (manager is IDisposable managerDisposable)
                    container.LifetimeContainer.Add(managerDisposable);

                // Create registration
                var registration = new ExplicitRegistration(container, name, type, manager);

                // Factory resolver
                var resolver = lifetimeManager is PerResolveLifetimeManager
                    ? (ResolveDelegate<BuilderContext>)((ref BuilderContext c) =>
                    {
                        c.Existing = factory(c.Container, c.Type, c.Name);
                        c.Set(typeof(LifetimeManager), new InternalPerResolveLifetimeManager(c.Existing));
                        return c.Existing;
                    })
                    : ((ref BuilderContext c) => factory(c.Container, c.Type, c.Name));
                registration.Set(typeof(ResolveDelegate<BuilderContext>), resolver);

                // Build Pipeline
                PipelineBuilder builder = new PipelineBuilder(registration, container, Context.FactoryPipelineCache);
                registration.Pipeline = builder.Pipeline();

                // Register
                var previous = container.Register(type, name, registration);

                // Allow reference adjustment and disposal
                if (null != previous && 0 == previous.Release()
                    && previous.LifetimeManager is IDisposable disposable)
                {
                    // Dispose replaced lifetime manager
                    container.LifetimeContainer.Remove(disposable);
                    disposable.Dispose();
                }

                // TODO: Raise event
                // container.Registering?.Invoke(this, new RegisterEventArgs(type, type, name, manager));
            });
        }

        #endregion


        #region Instance

        /// <inheritdoc />
        Task IUnityContainerAsync.RegisterInstance(IEnumerable<Type>? interfaces, string? name, object? instance, IInstanceLifetimeManager? lifetimeManager)
        {
            // Validate input
            throw new NotImplementedException();
        }

        #endregion


        #endregion


        #region Resolution

        /// <inheritdoc />
        Task<object?> IUnityContainerAsync.Resolve(Type type, string? name, params ResolverOverride[] overrides)
        {
            // Setup Context
            var registration = GetRegistration(type ?? throw new ArgumentNullException(nameof(type)), name);

            // Check if pipeline exists
            if (null == registration.Pipeline)
            {
                // Start a new Task to create and execute pipeline
                return Task.Factory.StartNew(() =>
                {
                    var context = new BuilderContext
                    {
                        List = new PolicyList(),
                        Type = type,
                        Overrides = overrides,
                        Registration = registration,
                        ContainerContext = Context,
                    };

                    try
                    {
                        // Execute pipeline
                        return context.Pipeline(ref context);
                    }
                    catch (Exception ex)
                    when (ex is InvalidRegistrationException || 
                          ex is CircularDependencyException || 
                          ex is ObjectDisposedException)
                    {
                        var message = CreateMessage(ex);
                        throw new ResolutionFailedException(context.Type, context.Name, message, ex);
                    }
                });
            }

            // Existing pipeline
            var context = new BuilderContext
            {
                Async = true,

                List = new PolicyList(),
                Type = type,
                Overrides = overrides,
                Registration = registration,
                ContainerContext = Context,
            };

            try
            {
                // Execute pipeline
                var task = context.Pipeline(ref context);

                // Make sure it is indeed a Task
                Debug.Assert(task is Task<object?>);

                // Return Task
                return (Task<object?>)task;
            }
            catch (Exception ex)
            when (ex is InvalidRegistrationException || ex is CircularDependencyException)
            {
                var message = CreateMessage(ex);
                throw new ResolutionFailedException(context.Type, context.Name, message, ex);
            }
        }

        public Task<IEnumerable<object>> Resolve(Type type, Regex regex, params ResolverOverride[] overrides)
        {
            throw new NotImplementedException();
        }

        #endregion


        #region Child container management

        /// <inheritdoc />
        IUnityContainerAsync IUnityContainerAsync.CreateChildContainer() => CreateChildContainer();

        /// <inheritdoc />
        IUnityContainerAsync? IUnityContainerAsync.Parent => _parent;

        #endregion
    }

}
