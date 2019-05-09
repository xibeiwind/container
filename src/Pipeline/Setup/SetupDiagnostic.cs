﻿using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Unity.Builder;
using Unity.Exceptions;
using Unity.Lifetime;
using Unity.Resolution;

namespace Unity
{
    [SecuritySafeCritical]
    public class SetupDiagnostic : SetupPipeline
    {
        #region Implementation

        protected override ResolveDelegate<BuilderContext> TransientLifetime(ref PipelineBuilder builder)
        {
            var type = builder.Type;
            var name = builder.Name;
            var registration = builder.Registration;
            var pipeline = builder.Pipeline() ?? ((ref BuilderContext c) => throw new ResolutionFailedException(type, name, error));

            return (ref BuilderContext context) =>
            {
#if !NET40
                // Check call stack for cyclic references
                var value = GetPerResolveValue(context.Parent, context.Type, context.Name);
                if (LifetimeManager.NoValue != value) return value;
#endif
                // Execute pipeline
                try
                {
                    // In Sync mode just execute pipeline
                    if (!context.Async) return pipeline(ref context);

                    // Async mode
                    var parent = IntPtr.Zero;
                    var list = context.List;
                    var unity = context.ContainerContext;
                    var overrides = context.Overrides;
#if !NET40
                    unsafe
                    {
                        var thisContext = this;
                        parent = new IntPtr(Unsafe.AsPointer(ref thisContext));
                    }
#endif
                    // Create and return a task that creates an object
                    return Task.Factory.StartNew(() =>
                    {
                        var c = new BuilderContext
                        {
                            List = list,
                            Type = type,
                            ContainerContext = unity,
                            Registration = registration,
                            Overrides = overrides,
                            DeclaringType = type,
                            Parent = parent,
                        };

                        // Build the type
                        return pipeline(ref c);
                    });
                }
                catch (Exception ex) when (ex is InvalidRegistrationException || ex is CircularDependencyException)
                {
                    ex.Data.Add(Guid.NewGuid(), null == context.Name
                        ? (object)context.Type
                        : new Tuple<Type, string?>(context.Type, context.Name));

                    if (null != context.DeclaringType) throw;

                    var message = CreateMessage(ex);
                    throw new ResolutionFailedException(context.Type, context.Name, message, ex);
                }
            };
        }

        protected override ResolveDelegate<BuilderContext> DefaultLifetime(ref PipelineBuilder builder)
        {
            var type = builder.Type;
            var name = builder.Name;
            var pipeline = builder.Pipeline() ?? ((ref BuilderContext c) => throw new ResolutionFailedException(type, name, error));

            return (ref BuilderContext context) =>
            {
#if !NET40
                // Check call stack for cyclic references
                var value = GetPerResolveValue(context.Parent, context.Type, context.Name);
                if (LifetimeManager.NoValue != value) return value;
#endif
                try
                {
                    // Build the type
                    return pipeline(ref context);
                }
                catch (Exception ex) when (ex is InvalidRegistrationException || ex is CircularDependencyException)
                {
                    ex.Data.Add(Guid.NewGuid(), null == context.Name
                        ? (object)context.Type
                        : new Tuple<Type, string?>(context.Type, context.Name));

                    if (null != context.DeclaringType) throw;

                    var message = CreateMessage(ex);
                    throw new ResolutionFailedException(context.Type, context.Name, message, ex);
                }
            };
        }

        #endregion


        #region Validation

#if !NET40

        [SecuritySafeCritical]
        private void Validate(IntPtr parent, Type type, string? name)
        {
            if (IntPtr.Zero == parent) return;

            unsafe
            {
                var parentRef = Unsafe.AsRef<BuilderContext>(parent.ToPointer());
                if (type != parentRef.Type || name != parentRef.Name) Validate(parentRef.Parent, type, name);

                throw new CircularDependencyException(parentRef.Type, parentRef.Name);
            }
        }

        [SecuritySafeCritical]
        object GetPerResolveValue(IntPtr parent, Type type, string? name)
        {
            if (IntPtr.Zero == parent) return LifetimeManager.NoValue;

            unsafe
            {
                var parentRef = Unsafe.AsRef<BuilderContext>(parent.ToPointer());
                if (type != parentRef.Type || name != parentRef.Name)
                    return GetPerResolveValue(parentRef.Parent, type, name);

                var lifetimeManager = (LifetimeManager?)parentRef.Get(typeof(LifetimeManager));
                var result = null == lifetimeManager ? LifetimeManager.NoValue : lifetimeManager.GetValue();
                if (LifetimeManager.NoValue != result) return result;

                throw new CircularDependencyException(parentRef.Type, parentRef.Name);
            }
        }
#endif
        #endregion


        #region Error Message

        private static string CreateMessage(Exception ex, string? message = null)
        {
            const string line = "_____________________________________________________";

            var builder = new StringBuilder();
            builder.AppendLine(ex.Message);
            if (null == message)
            {
                builder.AppendLine(line);
                builder.AppendLine("Exception occurred while:");
            }

            foreach (DictionaryEntry item in ex.Data)
                builder.AppendLine(DataToString(item.Value));

            if (null != message)
            {
                builder.AppendLine(line);
                builder.AppendLine(message);
            }

            return builder.ToString();
        }

        private static string DataToString(object value)
        {
            switch (value)
            {
                case ParameterInfo parameter:
                    return $"    for parameter:  {parameter.Name}";

                case ConstructorInfo constructor:
                    var ctorSignature = string.Join(", ", constructor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    return $"   on constructor:  {constructor.DeclaringType.Name}({ctorSignature})";

                case MethodInfo method:
                    var methodSignature = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    return $"        on method:  {method.Name}({methodSignature})";

                case PropertyInfo property:
                    return $"    for property:   {property.Name}";

                case FieldInfo field:
                    return $"       for field:   {field.Name}";

                case Type type:
                    return $"\n• while resolving:  {type.Name}";

                case Tuple<Type, string?> tuple:
                    return $"\n• while resolving:  {tuple.Item1.Name} registered with name: {tuple.Item2}";

                case Tuple<Type, Type> tuple:
                    return $"        mapped to:  {tuple.Item1?.Name}";
            }

            return value.ToString();
        }

        #endregion
    }
}
