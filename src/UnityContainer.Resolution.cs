using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Exceptions;
using Unity.Registration;
using Unity.Resolution;
using Unity.Utility;

namespace Unity
{
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

    /// <summary>
    /// A simple, extensible dependency injection container.
    /// </summary>
    public partial class UnityContainer
    {
        #region Check if can resolve

        internal bool CanResolve(Type type, string? name)
        {
#if NETSTANDARD1_0 || NETCOREAPP1_0
            var info = type.GetTypeInfo();
#else
            var info = type;
#endif
            if (info.IsClass)
            {
                // Array could be either registered or Type can be resolved
                if (type.IsArray)
                {
                    return IsRegistered(type, name) || CanResolve(type.GetElementType(), name);
                }

                // Type must be registered if:
                // - String
                // - Enumeration
                // - Primitive
                // - Abstract
                // - Interface
                // - No accessible constructor
                if (DelegateType.IsAssignableFrom(info) ||
                    typeof(string) == type || info.IsEnum || info.IsPrimitive || info.IsAbstract
#if NETSTANDARD1_0 || NETCOREAPP1_0
                    || !info.DeclaredConstructors.Any(c => !c.IsFamily && !c.IsPrivate))
#else
                    || !type.GetTypeInfo().DeclaredConstructors.Any(c => !c.IsFamily && !c.IsPrivate))
#endif
                    return IsRegistered(type, name);

                return true;
            }

            // Can resolve if IEnumerable or factory is registered
            if (info.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();

                if (genericType == typeof(IEnumerable<>) || IsRegistered(genericType, name))
                {
                    return true;
                }
            }

            // Check if Type is registered
            return IsRegistered(type, name);
        }

        #endregion


        #region Resolving Enumerable

        internal IEnumerable<TElement> ResolveEnumerable<TElement>(Func<Type, ImplicitRegistration, object?> resolve, string? name)
        {
            object? value;
            var set = new HashSet<string?>();
            int hash = typeof(TElement).GetHashCode();

            // Iterate over hierarchy
            for (UnityContainer? container = this; null != container; container = container._parent)
            {
                // Skip to parent if no data
                if (null == container._metadata || null == container._registry) continue;

                // Hold on to registries
                var registry = container._registry;

                // Get indexes and iterate over them
                var length = container._metadata.GetEntries<TElement>(hash, out int[]? data);
                if (null != data && null != registry)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (set.Add(registration.Name))
                        {
                            try
                            {
                                value = resolve(typeof(TElement), registration);
                            }
                            catch (ArgumentException ex) when (ex.InnerException is TypeLoadException)
                            {
                                continue;
                            }

                            yield return (TElement)value;
                        }
                    }
                }
            }

            // If nothing registered attempt to resolve the type
            if (0 == set.Count)
            {
                try
                {
                    var registration = GetRegistration(typeof(TElement), name);
                    value = resolve(typeof(TElement), registration);
                }
                catch
                {
                    yield break;
                }

                yield return (TElement)value;
            }
        }

        internal IEnumerable<TElement> ResolveEnumerable<TElement>(Func<Type, ImplicitRegistration, object?> resolve,
                                                                   Type typeDefinition, string? name)
        {
            object? value;
            var set = new HashSet<string?>();
            int hashCode = typeof(TElement).GetHashCode();
            int hashGeneric = typeDefinition.GetHashCode();

            // Iterate over hierarchy
            for (UnityContainer? container = this; null != container; container = container._parent)
            {
                // Skip to parent if no data
                if (null == container._metadata || null == container._registry) continue;

                // Hold on to registries
                var registry = container._registry;

                // Get indexes for bound types and iterate over them
                var length = container._metadata.GetEntries<TElement>(hashCode, out int[]? data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (set.Add(registration.Name))
                        {
                            try
                            {
                                value = resolve(typeof(TElement), registration);
                            }
                            catch (ArgumentException ex) when (ex.InnerException is TypeLoadException)
                            {
                                continue;
                            }

                            yield return (TElement)value;
                        }
                    }
                }

                // Get indexes for unbound types and iterate over them
                length = container._metadata.GetEntries(hashGeneric, typeDefinition, out data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (set.Add(registration.Name))
                        {
                            try
                            {
                                var item = container.GetOrAdd(typeof(TElement), registration.Name, registration);
                                value = resolve(typeof(TElement), item);
                            }
                            catch (MakeGenericTypeFailedException) { continue; }
                            catch (InvalidRegistrationException)   { continue; }

                            yield return (TElement)value;
                        }
                    }
                }
            }

            // If nothing registered attempt to resolve the type
            if (0 == set.Count)
            {
                try
                {
                    var registration = GetRegistration(typeof(TElement), name);
                    value = resolve(typeof(TElement), registration);
                }
                catch
                {
                    yield break;
                }

                yield return (TElement)value;
            }
        }

        #endregion


        #region Resolving Array

        internal Type GetTargetType(Type argType)
        {
            Type next;
            for (var type = argType; null != type; type = next)
            {
                var info = type.GetTypeInfo();
                if (info.IsGenericType)
                {
                    if (IsRegistered(type)) return type;

                    var definition = info.GetGenericTypeDefinition();
                    if (IsRegistered(definition)) return definition;

                    next = info.GenericTypeArguments[0];
                    if (IsRegistered(next)) return next;
                }
                else if (type.IsArray)
                {
                    next = type.GetElementType();
                    if (IsRegistered(next)) return next;
                }
                else
                {
                    return type;
                }
            }

            return argType;
        }

        internal IEnumerable<TElement> ResolveArray<TElement>(Func<Type, ImplicitRegistration, object?> resolve, Type type)
        {
            object? value;
            var set = new HashSet<string?>();
            int hash = type.GetHashCode();

            // Iterate over hierarchy
            for (UnityContainer? container = this; null != container; container = container._parent)
            {
                // Skip to parent if no data
                if (null == container._metadata || null == container._registry) continue;

                // Hold on to registries
                var registry = container._registry;

                // Get indexes and iterate over them
                var length = container._metadata.GetEntries(hash, type, out int[]? data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (null != registration.Name && set.Add(registration.Name))
                        {
                            try
                            {
                                value = resolve(typeof(TElement), registration);
                            }
                            catch (ArgumentException ex) when (ex.InnerException is TypeLoadException)
                            {
                                continue;
                            }

                            yield return (TElement)value;
                        }
                    }
                }
            }
        }

        internal IEnumerable<TElement> ResolveArray<TElement>(Func<Type, ImplicitRegistration, object?> resolve,
                                                              Type type, Type typeDefinition)
        {
            object? value;
            var set = new HashSet<string>();
            int hashCode = type.GetHashCode();
            int hashGeneric = typeDefinition.GetHashCode();

            // Iterate over hierarchy
            for (UnityContainer? container = this; null != container; container = container._parent)
            {
                // Skip to parent if no data
                if (null == container._metadata || null == container._registry) continue;

                // Hold on to registries
                var registry = container._registry;

                // Get indexes for bound types and iterate over them
                var length = container._metadata.GetEntries(hashCode, type, out int[]? data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (null != registration.Name && set.Add(registration.Name))
                        {
                            try
                            {
                                value = resolve(typeof(TElement), registration);
                            }
                            catch (ArgumentException ex) when (ex.InnerException is TypeLoadException)
                            {
                                continue;
                            }

                            yield return (TElement)value;
                        }
                    }
                }

                // Get indexes for unbound types and iterate over them
                length = container._metadata.GetEntries(hashGeneric, typeDefinition, out data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (null != registration.Name && set.Add(registration.Name))
                        {
                            try
                            {
                                var item = container.GetOrAdd(typeof(TElement), registration.Name, registration);
                                value = resolve(typeof(TElement), item);
                            }
                            catch (MakeGenericTypeFailedException) { continue; }
                            catch (InvalidRegistrationException)   { continue; }

                            yield return (TElement)value;
                        }
                    }
                }
            }
        }

        internal IEnumerable<TElement> ComplexArray<TElement>(Func<Type, ImplicitRegistration, object?> resolve, Type type)
        {
            object? value;
            var set = new HashSet<string?>();
            int hashCode = type.GetHashCode();

            // Iterate over hierarchy
            for (UnityContainer? container = this; null != container; container = container._parent)
            {
                // Skip to parent if no data
                if (null == container._metadata || null == container._registry) continue;

                // Hold on to registries
                var registry = container._registry;

                // Get indexes and iterate over them
                var length = container._metadata.GetEntries(hashCode, type, out int[]? data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (null != registration.Name && set.Add(registration.Name))
                        {
                            try
                            {
                                var item = container.GetOrAdd(typeof(TElement), registration.Name, registration);
                                value = resolve(typeof(TElement), item);
                            }
                            catch (ArgumentException ex) when (ex.InnerException is TypeLoadException)
                            {
                                continue;
                            }

                            yield return (TElement)value;
                        }
                    }
                }
            }
        }

        internal IEnumerable<TElement> ComplexArray<TElement>(Func<Type, ImplicitRegistration, object?> resolve,
                                                              Type type, Type typeDefinition)
        {
            object? value;
            var set = new HashSet<string?>();
            int hashCode = type.GetHashCode();
            int hashGeneric = typeDefinition.GetHashCode();

            // Iterate over hierarchy
            for (UnityContainer? container = this; null != container; container = container._parent)
            {
                // Skip to parent if no data
                if (null == container._metadata || null == container._registry) continue;

                // Hold on to registries
                var registry = container._registry;

                // Get indexes for bound types and iterate over them
                var length = container._metadata.GetEntries(hashCode, type, out int[]? data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (null != registration.Name && set.Add(registration.Name))
                        {
                            try
                            {
                                var item = container.GetOrAdd(typeof(TElement), registration.Name);
                                value = resolve(typeof(TElement), item);
                            }
                            catch (ArgumentException ex) when (ex.InnerException is TypeLoadException)
                            {
                                continue;
                            }

                            yield return (TElement)value;
                        }
                    }
                }

                // Get indexes for unbound types and iterate over them
                length = container._metadata.GetEntries(hashGeneric, typeDefinition, out data);
                if (null != data)
                {
                    for (var i = 1; i < length; i++)
                    {
                        var index = data[i];
                        var registration = (ExplicitRegistration)registry.Entries[index].Value;

                        if (null != registration.Name && set.Add(registration.Name))
                        {
                            try
                            {
                                var item = container.GetOrAdd(typeof(TElement), registration.Name);
                                value = (TElement)resolve(typeof(TElement), item);
                            }
                            catch (MakeGenericTypeFailedException) { continue; }
                            catch (InvalidRegistrationException)   { continue; }

                            yield return (TElement)value;
                        }
                    }
                }
            }


        }

        #endregion


        #region Resolve Delegate Factories

        private static ResolveDelegate<BuilderContext> OptimizingFactory(ref BuilderContext context)
        {
            throw new NotImplementedException();
            //var counter = 3;
            //var type = context.Type;
            //var registration = context.Registration;
            //ResolveDelegate<BuilderContext>? seed = null;
            //var chain = ((UnityContainer) context.Container)._processorsChain;

            //// Generate build chain
            //foreach (var processor in chain) seed = processor.GetResolver(type, registration, seed);

            //// Return delegate
            //return (ref BuilderContext c) => 
            //{
            //    // Check if optimization is required
            //    if (0 == Interlocked.Decrement(ref counter))
            //    {
            //        Task.Factory.StartNew(() => {

            //            // Compile build plan on worker thread
            //            var expressions = new List<Expression>();
            //            foreach (var processor in chain)
            //            {
            //                foreach (var step in processor.GetExpressions(type, registration))
            //                    expressions.Add(step);
            //            }

            //            expressions.Add(BuilderContextExpression.Existing);

            //            var lambda = Expression.Lambda<ResolveDelegate<BuilderContext>>(
            //                Expression.Block(expressions), BuilderContextExpression.Context);

            //            // Replace this build plan with compiled
            //            registration.Set(typeof(ResolveDelegate<BuilderContext>), lambda.Compile());
            //        });
            //    }

            //    return seed?.Invoke(ref c);
            //};
        }

        internal ResolveDelegate<BuilderContext> CompilingFactory(ref BuilderContext context)
        {
            throw new NotImplementedException();
            //var expressions = new List<Expression>();
            //var type = context.Type;
            //var registration = context.Registration;

            //foreach (var processor in _processorsChain)
            //{
            //    foreach (var step in processor.GetExpressions(type, registration))
            //        expressions.Add(step);
            //}

            //expressions.Add(BuilderContextExpression.Existing);

            //var lambda = Expression.Lambda<ResolveDelegate<BuilderContext>>(
            //    Expression.Block(expressions), BuilderContextExpression.Context);

            //return lambda.Compile();
        }

        internal ResolveDelegate<BuilderContext> ResolvingFactory(ref BuilderContext context)
        {
            throw new NotImplementedException();
            //ResolveDelegate<BuilderContext>? seed = null;
            //var type = context.Type;
            //var registration = context.Registration;

            //foreach (var processor in _processorsChain)
            //    seed = processor.GetResolver(type, registration, seed);

            //return seed ?? ((ref BuilderContext c) => null);
        }

        #endregion
    }
    
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8603 // Possible null reference return.
}
